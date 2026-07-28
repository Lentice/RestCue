# OpenCode review follow-up 3 — RestCue issue #13

Continue with the `implement` SKILL and fix both remaining review findings.

## 1. Pause must reject Idle (medium correctness)

`WorkCycleTracker.Pause()` uses a deny-list and currently accepts `Idle`. The issue
spec explicitly allows Pause only from `Working`, `PendingReminder`,
`ReminderVisible`, and `Snoozed`.

- Change Pause validation to an explicit legal-source allow-list.
- Add `Idle` to invalid-transition coverage.
- Preserve all existing legal-source behavior and Need/cooldown semantics.

## 2. Popup withdrawal must be regression-tested through production code (medium)

Current tests cover production event wiring and tray mapping, but deleting
`CloseReminderIfOpen()` from `MainWindow.Pause()` or `StartFocusMode()` would still
leave all tests green. The spec requires evidence that entering Pause/Focus withdraws
an existing popup, while Core tests cover suppression during the mode and legal
Timing after exit.

Create the smallest production seam that owns **both** of these operations for each
mode entry:

1. withdraw/close the active reminder presentation;
2. invoke the corresponding Core transition.

Have `MainWindow.Pause()` and `MainWindow.StartFocusMode()` call that seam. Tests must
call the same production seam with a real `WorkCycleTracker` (fake clock) and a
tracking close callback, proving for both Pause and Focus:

- close is invoked exactly once;
- Core reaches the correct phase;
- Need is preserved;
- no `ReminderShown` occurs during the mode.

Do not reintroduce a trivial wrapper that tests only an Action. The seam must execute
the real Core transition as well, so removing either cleanup or transition breaks the
test. Keep exception/UI status handling in MainWindow if that is the smallest design.
The existing Core tests remain responsible for the exit Timing path.

## Boundaries

- Keep the solution simple and issue-scoped.
- Do not change spec checkboxes.
- Do not commit, push, or close.
- Run targeted Core/App tests and `git diff --check`.
- Report changes, tests, known limitations, and data/schema impact.
