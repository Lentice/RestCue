# OpenCode handoff — finish issue #12

Use the `implement` SKILL to finish the existing in-progress implementation of GitHub
issue #12 in this working tree.

## Required inputs

Read these files completely before changing code:

- `AGENTS.md`
- `docs/specs/issue-12-reminder-retry-cooldown.md`
- `docs/agents/domain.md`
- `docs/product/design-spec.md`
- The relevant retry cooldown / Need clock sections of
  `docs/product/windows-eye-care-assistant-design-spec-v1.3.md`
- Relevant ADRs under `docs/adr/`

`CONTEXT.md` does not exist. GitHub issue #12 has no comments. The local spec already
contains execution, acceptance, verification, completion-report, and data-impact
checklists.

## Current state

The working tree already contains an unfinished attempt for issue #12. Preserve and
review those changes; do not assume they are correct. The changes currently touch:

- `src/RestCue.App/MainWindow.xaml.cs`
- `src/RestCue.Core/Reminders/WorkCycleTracker.cs`
- `src/RestCue.Core/Settings/AppSettings.cs`
- `src/RestCue.Core/Settings/AppSettingsValidator.cs`
- `tests/RestCue.App.Tests/WindowsTrayIconPhaseMappingTests.cs`
- `tests/RestCue.Core.Tests/Reminders/WorkCycleTrackerForegroundContextTests.cs`
- `tests/RestCue.Core.Tests/Reminders/WorkCycleTrackerTests.cs`

## Task

Implement every unchecked item in `docs/specs/issue-12-reminder-retry-cooldown.md`.
Pay particular attention to:

- Retry cooldown and rest Need are independent clocks.
- Ignore and AutoDismissed preserve accumulated Need and remain distinct results.
- The next retry evaluation occurs at the earlier of cooldown deadline and the next
  debt-level threshold. Issue #14 owns debt-level calculation, so provide only the
  minimum explicit seam needed by #14, with correct current behavior and tests.
- Re-evaluation must still obey natural-pause timing, maximum wait, fullscreen,
  application rules, and presentation caps.
- Snooze remains independent.
- Manual Break Guide remains available during cooldown.
- Core timing uses `IClock`; UI timers are not truth.
- Add the ADR required by the spec.
- Do not add persistence or schema changes.
- Avoid speculative abstractions and out-of-scope features.

Inspect the current tests critically. Remove or correct tests that merely encode an
incorrect implementation. Add focused fake-clock tests for immediately before, exactly
at, and after deadlines, including the earlier debt-threshold case.

## Testing and ownership

Run only targeted/basic tests needed for implementation speed. The supervising agent
will run the complete `dotnet build RestCue.sln`, `dotnet test RestCue.sln --no-build`,
and `git diff --check` before the final commit.

Do not commit, push, modify GitHub, or close the issue. Although the generic `implement`
SKILL normally commits at the end, this instruction overrides that one step because
the supervising agent owns final review, full tests, commit, and issue closure.

When done, report:

- Changes made
- Basic tests run and results
- Known limitations
- Data/schema impact
- Any remaining unchecked spec item
