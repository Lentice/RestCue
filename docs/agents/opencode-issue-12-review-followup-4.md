# OpenCode fourth follow-up — issue #12 closure blockers

Use the `implement` SKILL. Continue the current issue #12 implementation in this
working tree. Do not commit or modify GitHub.

## High: Need is still based on the last visible reminder attempt

The product contract is unambiguous: Need is total effective work since the last
trusted reset (`BreakCompleted`, Idle/new cycle/explicit reset). Showing,
ignoring, or auto-dismissing an attempt does not establish a new Need baseline.

Remove the `lastReminderWorkTime` coupling from Need eligibility. The normal Need
condition must compare the current total `AccumulatedWorkTime` against the current
`effectiveWorkInterval`. A visible reminder may not reset or offset this value.

Consequences:

- At ordinary cooldown expiry, unchanged/default interval remains due because
  Need was already due before the ignored attempt, so enter `PendingReminder`.
- If an application rule changes to a custom interval greater than total
  `AccumulatedWorkTime`, ordinary cooldown expiry remains Working until total
  accumulated Need reaches that custom interval.
- A supplied earlier debt deadline still establishes eligibility independently.

Update ADR/spec/tests to use the trusted-reset Need baseline. Remove tests or
names that encode “interval since last visible reminder.”

## High: preserve retry/debt clocks across suppression modes

Entering and leaving Focus Mode must not clear `cooldownUntil` or
`nextDebtDeadline`; Focus continues Need accumulation but suppresses active
reminders. Preserve these retry clocks through Focus Mode and re-evaluate normally
after Focus ends.

Pause also must not reset Need or retry clocks; it stops effective-work
accumulation and reminders, then resumes with the prior deadlines/state. Disabled
may clear them because enabling establishes a new cycle.

Add fake-clock tests for Ignore → Focus Mode → deadline passes → End Focus Mode,
including an earlier supplied debt deadline. Verify no visible reminder during
Focus and normal reevaluation after ending it. Add focused Pause preservation
coverage.

## High: make manual Break Now click ordering race-safe

`MainWindow.StartBreakNow()` currently closes the guide before validating the
core transition and does not immediately update phase/tray state. Fix ordering:

1. Attempt `ManualStartBreak()` first. If invalid, return without closing any
   existing guide.
2. Immediately call `UpdateCycleStatus()` after successful transition so tray
   command availability reflects `BreakInProgress` before another click.
3. Then close/replace/show the guide as needed.

Add a regression test at the smallest testable App seam proving a second manual
start cannot close the active guide or leave stale command availability. Avoid
WPF UI automation; extract a small coordinator/helper only if necessary and keep
it internal.

## Acceptance coverage and API hygiene

- Add retry-path tests for an application rule that suppresses or caps
  presentation (Silent and/or TrayOnly), not only fullscreen.
- Make the extracted break-now command binder internal rather than public; add
  `InternalsVisibleTo` only if that matches the repository's minimal test seam.

## Verification

Run only targeted/basic tests. Do not mark final verification checkboxes; the
supervising agent owns the final full build/test/diff-check, spec checklist,
commit, and GitHub issue closure.

Report changes, tests/results, limitations, and data/schema impact.

