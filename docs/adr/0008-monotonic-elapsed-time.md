# ADR-0008: Elapsed time is monotonic, civil time is not

## Context

Issue #40. Every duration the app measured was measured against `IClock.UtcNow`, a
wall-clock reading the user can move. Break completion, pause expiry, Focus Mode
expiry, snooze expiry, the retry cooldown, the rest-debt deadline, and work-time
accumulation all derived from it.

Wall-clock time is not monotonic. It steps when the user edits the system time, when
a large time-synchronisation correction lands, and when a virtual machine or laptop
resumes from a suspended state. A forward step during a break made the next poll
conclude that the whole break had elapsed, completing it and performing the trusted
rest reset for a break the user took for seconds; it also inflated accumulated work
time. A backward step made breaks, pauses and Focus Mode all run longer than asked,
with no way for the user to tell why.

Routine time-synchronisation corrections are sub-second and harmless at these scales.
The cases that bite are manual clock edits, virtual-machine restore, and first-boot
corrections on a machine whose real-time clock has drifted badly.

## Decision

Elapsed time and civil time are two different things, and `IClock` exposes both:

- `DateTimeOffset UtcNow` — civil time. Kept only where an actual point on the
  calendar is required. Usage-event timestamps are the case that matters: daily
  statistics must bucket by calendar day, so they stay wall-clock and are unchanged
  by this ADR.
- `TimeSpan Elapsed` — monotonic elapsed time since an arbitrary origin. Only
  differences between two readings are meaningful.

Every "how long has this been going?" question uses `Elapsed`. In
`WorkCycleTracker` that is all nine timing fields — pending-since, break start,
last tick, reminder-visible-since, snooze deadline, cooldown deadline, rest-debt
deadline, Focus Mode deadline and pause deadline — which changed from
`DateTimeOffset?` to `TimeSpan?` and lost their `Utc` suffixes. `BreakGuideSession`
measures its own progress the same way. The public `CooldownUntil` property and
`SetNextDebtDeadline` seam changed type with them; both are points on the monotonic
timeline now, not civil times.

`SystemClock.Elapsed` is backed by `Stopwatch.GetElapsedTime` against a static
origin timestamp, i.e. the high-resolution performance counter. It counts forward
from boot and is unaffected by system time changes. It does not advance while the
machine is suspended, which is what we want: the sleep and resume handlers reset the
cycle rather than crediting suspended time as work or as rest.

## Alternatives

- **`Environment.TickCount64`.** Also monotonic and step-immune, but coarser
  (~15 ms) and offers nothing the performance counter does not.
- **`DateTime.UtcNow` with step detection** — compare successive readings and
  discard implausible deltas. Rejected: it guesses at the user's intent, cannot
  distinguish a step from a long stall, and leaves every consumer to opt in.
- **Keeping a single wall-clock member and correcting deadlines after a step.**
  Rejected: it puts step handling in the state machine rather than at the seam, and
  every future deadline would have to remember to participate.
- **A default interface implementation deriving `Elapsed` from `UtcNow`,** to avoid
  touching the sixteen fake clocks in the test suite. Rejected deliberately: a new
  production clock would silently inherit non-monotonic elapsed time, which is the
  defect this ADR exists to remove.

## Consequences

- Clock-step behaviour is testable. `FakeClock` drives the two readings
  independently — `Advance` moves both, `StepWallClock` moves civil time alone, and
  `AdvanceElapsedOnly` moves elapsed time alone — so a step in one without the other
  is expressible, and `ClockStepScenarioTests` covers it.
- A deadline stored in `WorkCycleTracker` is meaningless outside the process. None
  was ever persisted, and none may be: the monotonic origin does not survive a
  restart.
- Stored data is unaffected. Usage-event timestamps remain wall-clock.
- Suspend still does not advance elapsed time. That is deliberate, but it does mean
  the seam alone does not decide what happens across a suspend; the lock, sleep and
  resume handlers do.

## Review Trigger

Review if a timing value ever needs to be persisted across a restart, or if a
consumer appears whose classification as elapsed or civil time is genuinely
ambiguous.
