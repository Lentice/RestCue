# ADR-0003: Reminder retry cooldown clock separation

## Context

Issue #12 separates the rest-Need clock from the reminder-retry clock. Previously it
was ambiguous whether Ignore and AutoDismissed reset the entire work cycle, including
Need accumulation. The spec (R-18) resolved that they must not: only BreakCompleted
and Idle are trusted Need-reset events. Ignore and AutoDismissed end only the current
reminder attempt and start a retry cooldown.

The cooldown must not delay re-evaluation past the next debt-level threshold. The
default retry cooldown is 20 minutes, same as the default work interval, but is a
separate configurable setting (range 1–60 minutes).

## Decision

WorkCycleTracker owns a single `TimeSpan? cooldownUntil` field expressed as an
`IClock`-based deadline, not a UI timer. It is a point on the monotonic elapsed-time
timeline (ADR-0008); it was wall-clock until issue #40. The field is set on Ignore and AutoDismissed,
cleared when a reminder is shown (EnterReminderVisible), idle is entered, or the
cycle is reset.

The retry gate logic lives in `TryEnterPendingReminderFromWorking`, a private method
shared by `TickWorking` and `EndFocusMode`. While `cooldownUntil` is active, the
effective retry deadline is `EarlierOf(cooldownUntil, nextDebtDeadline)`. Before that
deadline all transitions to PendingReminder are suppressed, including an otherwise-due
normal reminder. When the effective deadline is reached, both deadlines are cleared
and one normal re-evaluation occurs via transition to PendingReminder (which then
respects natural pause, maximum wait, fullscreen, application rules, and presentation
caps). The retry gate is independent from the work-interval condition: a supplied
`nextDebtDeadline` can trigger re-evaluation even if the ordinary work interval
since the last visible attempt has not elapsed.

A public `SetNextDebtDeadline(TimeSpan?)` method exposes the seam for issue #14
(debt-level calculation). The supplied deadline is stored only while `cooldownUntil`
is active; calling it without an active cooldown clears the stored deadline. #14
calls this method to supply the time at which the next debt threshold will be
reached. A supplied debt deadline without an active retry cooldown (`cooldownUntil`
is null) does not delay or replace the normal reminder path. The tick loop
re-evaluates every second, so the earlier-of is governed by whichever deadline
expires first.

Need (`AccumulatedWorkTime >= effectiveWorkInterval`) is total effective work since
the last trusted reset (BreakCompleted or Idle/new cycle). Showing, ignoring, or
auto-dismissing a reminder does not reset Need. This decoupling ensures that once
the work interval has been exceeded, subsequent retries are gated only by the retry
clock and the supplied debt deadline, not by a recurring Need window.

Pause is a freeze, not a trusted reset. `Pause()` calls `ClearReminderState()` which
preserves retry clocks; `Resume()` preserves `AccumulatedWorkTime`, `cooldownUntil`,
and `nextDebtDeadline` and re-enters Working without calling `ResetCycle()`.
If the effective retry deadline expired during Pause, `Resume()` allows the next
tick's normal reevaluation to enter `PendingReminder`.

Cooldown and snooze are independent: snooze sets its own exclusive `snoozeUntilUtc`
deadline and is not affected by retry cooldown.

A `ManualStartBreak()` method on `WorkCycleTracker` allows the user to start a break
from the tray icon even during cooldown, restricted to active phases (Working,
PendingReminder, ReminderVisible, Snoozed, FocusMode). It throws from Paused, Idle,
Disabled, and BreakInProgress. The App layer wires this through a `BreakNowRequested`
event on `ITrayIcon`, a "立即休息" menu item in `WindowsTrayIcon`, and a
`StartBreakNow()` method on `MainWindow` that calls `ManualStartBreak()` and shows
the existing non-modal Break Guide. The tray command is disabled in Paused, Idle,
Disabled, and BreakInProgress.

## Alternatives

- Using a UI DispatcherTimer to track cooldown was rejected because core timing
  must use `IClock` for testability.
- Clearing cooldown in Ignore/AutoDismissed only on the tick that crosses the next
  deadline was rejected in favour of explicit `cooldownUntil = null` at
  EnterReminderVisible for clarity.
- Removing the cooldown check entirely and letting Ignore/AutoDismissed simply reset
  `lastReminderWorkTime` was rejected because it would let repeated ignore/dismiss
  produce back-to-back reminders without any minimum gap.

## Consequences

- All cooldown edge cases (before/at/after expiry, combined with debt threshold) are
  testable with FakeClock.
- The cooldown does not need its own timer or background job; it piggybacks on the
  existing 1 Hz tick loop.
- Ignore and AutoDismissed remain distinct `ReminderResult` values for statistics.
- Need and retry cooldown are genuinely decoupled: Need grows monotonically during
  cooldown while no new reminder is shown.

## Review Trigger

Review when issue #14 introduces multi-level debt threshold re-evaluation, or if
dogfooding shows that users expect a minimum-guaranteed silent period longer than
the cooldown setting.

## Addendum (issue #34): the threshold deadline is armed at cooldown start

The decision above describes `SetNextDebtDeadline` as a seam that issue #14 calls to
supply the next threshold time, and ADR-0004 implemented that call as a side effect of
a rest-debt level change. That made every escalation one level late: the deadline was
only armed *after* a threshold had been crossed, so it pointed at the threshold after
the one that should have triggered re-evaluation, and the escalation override never
fired earlier than the cooldown it was meant to pre-empt.

`Ignore` and `TryAutoDismiss` now arm the deadline themselves, from the accumulated
work time at the moment the cooldown starts. Recomputation on level change is retained
only as a safety net.

The storage rule stated above — the supplied deadline is kept only while `cooldownUntil`
is active — makes ordering a correctness requirement: `cooldownUntil` must be assigned
*before* the deadline is armed, or the arming is silently discarded. For the same
reason, the safety-net recomputation must not push a deadline that has already come due
out into the future; it would otherwise skip the very crossing the deadline was armed
for, because debt evaluation runs before the retry gate within a single tick.

Because the deadline advances with elapsed time while `AccumulatedWorkTime` does not, the two can
drift apart by the work that a phase transition does not credit — `Ignore` and
`TryAutoDismiss` both drop their accumulation bookkeeping, costing one tick. The
deadline therefore lands at or slightly before the threshold. Arriving early is
harmless; arriving late was the defect.
