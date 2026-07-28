# OpenCode second follow-up — issue #12 review blockers

Use the `implement` SKILL. Continue the current issue #12 implementation in this
working tree. Do not commit or modify GitHub.

The second independent review found that issue #12 is still not closeable. Fix
all findings below and add tests that would have failed before the fix.

## High: debt deadline is incorrectly gated by the ordinary work interval

`TickWorking` currently evaluates `EarlierOf(cooldownUntil, nextDebtDeadline)`
only inside:

```text
AccumulatedWorkTime - lastReminderWorkTime >= effectiveWorkInterval
```

That is wrong. During an active retry cooldown, a supplied next-debt deadline
must independently make the reminder attempt eligible for one normal
reevaluation, even if the ordinary work interval since the last visible attempt
has not elapsed.

Implement these semantics with the minimum simple code:

- The normal reminder path remains eligible when the existing work-interval
  condition is met.
- While `cooldownUntil` is active, the effective retry deadline is the earlier of
  cooldown and supplied debt deadline.
- Reaching that effective deadline independently makes the retry path eligible.
- Before that deadline, an otherwise-due normal reminder remains suppressed.
- A supplied debt deadline without an active retry cooldown must not delay or
  replace the normal reminder path.
- When eligible, transition once to `PendingReminder`; all existing Timing and
  Intensity rules remain downstream and are not bypassed.

Add a fake-clock test where the supplied debt deadline occurs before BOTH the
cooldown deadline and the ordinary work-interval eligibility. Immediately before
must remain Working; exactly at must enter PendingReminder. This test must fail
against the current implementation.

## Medium: prove tray command integration

The current App tests only verify enabled state and interface compilation. Add a
focused integration test at the established seam that raises
`BreakNowRequested` and proves the wired status-window command is invoked. Also
retain Core coverage proving a Working tracker with an active cooldown can enter
BreakInProgress via `ManualStartBreak`.

Do not introduce WPF UI automation if the existing App lifecycle test seam can
prove the event binding without it.

## Medium: constrain manual-break phase semantics

Do not silently unpause or leave Idle by completing a manual break.

- Disable the tray command in `Paused`, `Idle`, `Disabled`, and
  `BreakInProgress`.
- Restrict `ManualStartBreak()` consistently to active phases where a manual
  Break Guide is meaningful and completion returning to Working is valid:
  `Working`, `PendingReminder`, `ReminderVisible`, `Snoozed`, and `FocusMode`.
- Add rejection tests for Paused and Idle.
- Update phase-mapping tests and ADR wording so they match exactly.

## Verification and report

Run only targeted/basic tests. Report changes, tests/results, known limitations,
data/schema impact, and any remaining unchecked spec item.

