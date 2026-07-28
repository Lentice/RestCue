# OpenCode review follow-up 2 — RestCue issue #13

Continue with the `implement` SKILL. A second independent review found that the first
follow-up still has false-positive tests and incomplete App evidence. Fix every item.

## 1. Correct the stale-suppression regression sequence (high)

The new Pause/Focus regression tests currently unsuppress while still Paused/Focus,
then later perform an unsuppressed-to-unsuppressed update. That does not reproduce the
real bug and can pass even if `hasSuppressedReminder` is not cleared.

For both Pause and Focus tests:

1. Create the suppressed pending attempt.
2. Enter Pause/Focus and keep foreground suppression active.
3. Resume/EndFocus so the Core reaches Working/Pending as appropriate.
4. Subscribe to `ReminderShown`.
5. Remove foreground suppression.
6. Immediately assert the phase is still `PendingReminder` and shown count is zero.
7. Only after a legal natural-pause or maximum-wait fake-clock tick assert Visible
   and exactly one shown event.

The test must fail if the production `hasSuppressedReminder = false` line is removed.

## 2. Test production tray-cue withdrawal during modes (high/medium)

`ModeCoordinator` only wraps an `Action` and its tests prove only that a callback is
called. It does not prove that production `App.OnPhaseChanged` clears an active tray
cue when entering Paused/FocusMode, or that the cue stays cleared during those modes.

Extract the smallest existing production phase/cue application seam from `App` (or
make the current handler logic directly testable) and call it from the real handler.
Tests must use this production seam and a tracking `ITrayIcon` to prove:

- an already-active suppressed tray cue is cleared on `Paused`;
- an already-active suppressed tray cue is cleared on `FocusMode`;
- the mode status/text and command enablement remain correct;
- no mode handler itself reintroduces the suppressed cue.

Pair this App evidence with the corrected Core timing tests; do not attempt WPF UI
automation. Reconsider whether `ModeCoordinator` adds any evidence/value after this
seam exists. If it is only a one-action wrapper with trivial tests, remove it and use
the original direct `CloseReminderIfOpen()` calls; keep the solution simple.

## 3. Fix six-event test mismatch (low)

`WireModeCommands_binds_all_six_events` invokes/asserts only Pause, Resume,
StartFocusMode, and EndFocusMode, while the production seam also wires Disable and
Enable. Either add request methods/counters/assertions for all six or narrow/rename
the seam and test to the four issue-related mode events. Prefer complete coverage of
the production seam if keeping all six.

## Boundaries

- Do not change spec checkboxes.
- Do not commit, push, or close the issue.
- Keep changes issue-scoped and remove redundant/trivial abstractions/tests.
- Run targeted Core/App tests and `git diff --check` only.
- Report changes, tests, known limitations, data/schema impact.
