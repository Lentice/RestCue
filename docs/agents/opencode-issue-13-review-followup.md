# OpenCode review follow-up — RestCue issue #13

Continue the current issue #13 implementation using the `implement` SKILL. An
independent reviewer found the issues below. Fix all of them without expanding scope.

## Required findings

### 1. Production wiring is not tested (high)

The new tests in `ApplicationLifecycleTests.cs` manually subscribe lambdas instead
of invoking the production `App.WireTrayCommands()` path. They would pass if the
production handlers were deleted or wired incorrectly; the two “mutual exclusion”
tests only count unrelated calls.

Extract the relevant production subscription into the smallest internal static seam
(similar to `WireBreakNowCommand`), call that seam from `WireTrayCommands()`, and make
tests call that exact production seam. Remove redundant tests that only reproduce
implementation in the test.

### 2. Active presentation suppression needs a real testable seam (high)

The issue spec requires evidence that entering Pause or Focus:

- withdraws an active popup/tray cue;
- does not show a popup/cue while the mode is active;
- does not bypass Core/Timing after exit.

Current counter-only `FakeStatusWindow` tests cannot observe this. Use the smallest
production seam that tests real behavior; do not duplicate behavior in tests or
introduce a speculative architecture. Existing `MainWindow.Pause()` and
`StartFocusMode()` call `CloseReminderIfOpen()`, and production phase handling resets
tray suppression. Make these outcomes testable at an appropriate existing seam. If
direct WPF window tests are impractical, extract only the mode-command/presentation
coordination required by this issue into a small production collaborator and test
that collaborator. Preserve non-modal/non-focus-stealing behavior.

### 3. Stale suppressed attempt bypasses Timing (medium/high correctness)

`Pause()` and `StartFocusMode()` call `ClearReminderState()`, but that method leaves
`hasSuppressedReminder` set. Scenario:

1. A pending attempt becomes suppressed by fullscreen/application context.
2. Enter Pause or Focus; timestamps are cleared but the per-attempt suppressed flag
   survives.
3. Resume/end Focus into Pending.
4. Foreground suppression is removed.
5. `UpdateForegroundContext()` sees the stale flag and immediately calls
   `EnterReminderVisible()`, bypassing the required natural-pause/maximum-wait route.

Clear only the abandoned per-attempt suppressed reminder/cue state when entering
Pause/Focus. Preserve the current foreground suppression context so new attempts
remain correctly capped. Add fake-clock regressions for both Pause and Focus:
suppressed pending -> enter mode -> exit -> unsuppress must stay on the normal Timing
path and not emit `ReminderShown` early.

### 4. Core tests assert too little (medium)

The new source-phase tests only assert the final enum. Strengthen or replace them to
prove:

- Need is preserved;
- retry cooldown is preserved/gates re-entry;
- snooze/reminder attempt state is abandoned;
- no stale reminder callback occurs after advancing the fake clock;
- Focus ending produces at most one pending attempt and never directly visible.

Prefer a few behavior-focused tests over many phase-only tests.

## Boundaries

- Re-read `docs/specs/issue-13-pause-focus-time-semantics.md` and relevant production
  code before editing.
- Keep changes minimal and only for issue #13.
- Do not change the spec checkboxes.
- Do not commit, push, or close the issue.
- Run only targeted Core/App tests and `git diff --check`.
- Report changed behavior, targeted test results, known limitations, and data/schema
  impact.
