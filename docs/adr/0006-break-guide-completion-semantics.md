# ADR-0006: Break guide completion semantics — de-digitisation, cancellation, and safe degradation

## Context

Issue #9 adds a visual break guide that replaces the numeric countdown with a
non-intrusive, numberless experience. The break guide must communicate progress
without displaying digits, percentages, or remaining time. Key semantics:

- Break completion (full duration elapsed) must still trigger a trusted Need reset
  via `WorkCycleTracker.ResetCycle()`, indistinguishable from the previous
  implementation.
- Early break exit (cancel) must preserve accumulated work time and debt levels.
- The guide is purely a UI concern — it must not introduce a second timing
  authority alongside `WorkCycleTracker`.
- No modal/full-screen/masking UI; the existing `WS_EX_NOACTIVATE |
  WS_EX_TOOLWINDOW` window style is sufficient to avoid focus stealing.

## Decision

### 1. Completion always resets Need; cancellation never does

`BreakCompleted` continues to be emitted by `WorkCycleTracker.TickBreak()` after
the full `BreakDuration` elapses. This event triggers `ResetCycle()` which clears
`AccumulatedWorkTime` and `restDebtLevel`. The new `CancelBreak()` method on
`WorkCycleTracker` emits `BreakCancelled` but never calls `ResetCycle()`.

This preserves the invariant that only a fully completed break is a trusted reset
of accumulated rest need.

### 2. Single `CancelBreak()` seam for all early exits

A single `WorkCycleTracker.CancelBreak()` method handles all early break exits:
user clicks the cancel button, `CloseReminderIfOpen()` during `BreakInProgress`,
or application shutdown. It is idempotent (no-op when not in `BreakInProgress`),
preventing double events from overlapping paths.

Callers that close the guide window (`Pause()`, `StartFocusMode()`, `Disable()`,
session lock, suspend) now call `CancelBreak()` before closing, ensuring
`BreakCancelled` fires exactly once across all paths.

### 3. `BreakGuideSession` as a UI-side state machine

A lightweight `BreakGuideSession` class tracks the guide's local phase
(`NotStarted` → `Running` → `Completed`/`Cancelled`) and fires `CueChanged`
events at the start, midpoint (`duration / 2`), and end of the break. The session
uses the same `IClock` as `WorkCycleTracker` to compute elapsed time, ensuring
no independent timing drift.

The session is created and owned by `MainWindow` for each break. Its `Tick()`
method is called by the `DispatcherTimer` every second. The session never
formats or returns remaining time as a string.

### 4. De-digitisation

All UI strings during `BreakInProgress` are replaced with fixed Chinese-language
cues. The `BreakGuideText.ForCue()` static method returns the non-numeric text
for each cue stage. The cue text string is asserted in tests to contain no digits
(including full-width digits).

The reminder window's `ActionButton.Content` and `SnoozeButton.Content` are also
stripped of embedded numeric values (previously `"Start Break (20s)"` and
`"Snooze 5min"`).

### 5. No new ADR needed for `HandleUnlock`/`HandleResume`/`EnterIdle`

Existing lock/sleep/idle paths use `BreakCancelled` followed by `ResetCycle()`
(with the `wasInBreak` flag in `EnterIdle` and the explicit check in
`HandleUnlock`/`HandleResume`). This ticket does not alter those paths; the
semantics remain "cancel + reset" for trusted system events, and only explicit
user cancellation via `CancelBreak()` preserves Need.

## Alternatives considered

- **Reusing the `DispatcherTimer` for timing authority**: rejected because
  `DispatcherTimer` is not testable with fake clocks.
- **Removing `BreakGuideSession` and letting `ReminderWindow` hold state**:
  rejected because state logic should be in Core for testability and consistency.
- **Using a `ProgressBar`**: rejected because any numeric-bound value (even
  normalised 0–1) still communicates a numeric ratio to the user.

## Consequences

- `WorkCycleTracker` gains one new method and one new code path; no existing
  paths change.
- `BreakGuideSession` is a simple, fully testable state machine in Core.
- The reminder window's countdown timer is repurposed as a polling timer for
  `BreakGuideSession.Tick()` — it no longer formats or stores numeric values.
- Any new caller that closes the guide window must call `CancelBreak()` first,
  or the `BreakCancelled` event will not fire for that path. The
  `CloseReminderIfOpen()` helper handles this automatically.
- UI strings are now purely non-numeric; any future localisation must ensure
  the same constraint.

## Review Trigger

Review this decision when:
- Adding audio break guide (issue #10), which may require different cue timing.
- Changing the break completion to allow partial-completion semantics.
- Introducing a settings UI for break guide duration (issue #19): ensure the
  duration value passes through `AppSettings` → `WorkCycleTracker` → session
  without duplication.
