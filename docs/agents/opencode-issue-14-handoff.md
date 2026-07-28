# OpenCode handoff — RestCue issue #14

Use the **implement** SKILL to implement GitHub issue #14 in this repository.

## Required source of truth

Read completely before editing:

1. `AGENTS.md`
2. `docs/specs/issue-14-rest-debt-levels.md`
3. `docs/agents/domain.md`
4. `docs/product/design-spec.md`
5. Relevant sections 5.5, 5.6, 10.3, 10.4, and C-002 of
   `docs/product/windows-eye-care-assistant-design-spec-v1.3.md`
6. `docs/adr/0003-reminder-retry-cooldown-clock-separation.md`
7. Current `WorkCycleTracker` implementation and tests

There is no root `CONTEXT.md`; proceed as `docs/agents/domain.md` instructs.

## Implementation constraints

- Implement only issue #14. Do not implement #15 UI/intensity mapping, #16
  persistence, or #18/#19 settings persistence/UI.
- Need, Reminder Timing, and Presentation Intensity must remain independent.
- Use the baseline configured work interval for debt Level 1. Foreground application
  `effectiveWorkInterval` overrides are Timing-only and must not alter debt.
- Use `IClock`; no real delays, UI timers, WPF, Win32, or Infrastructure dependency
  in the debt policy.
- A large fake-clock jump emits one previous-to-final level event, not intermediate
  events. A trusted reset from a nonzero level emits one current-to-Level0 event.
  Repeated evaluation/reset at the same level emits nothing.
- Preserve issue #11–#13 semantics and existing tests.
- Integrate with #12's next-debt-deadline seam without allowing wall-clock time
  during Pause/unavailable/lock/sleep to count as effective work.
- Add ADR-0004 (or the next valid ADR number) describing the four-level debt model
  and the Need/Timing/Intensity separation.
- Update the issue spec checklist and completion report only for work actually
  completed and verified.
- Keep the implementation minimal; do not introduce speculative abstractions.
- Do not modify or stage unrelated files.
- Do not commit. The supervising agent will run the complete suite and make the
  final commit.

## Testing budget

Use TDD at the Core seams where practical. Run only focused Core tests and a
targeted build needed for quick feedback. Do **not** run the full solution test
suite; the supervising agent will do that before commit.

At the end, report changed files, focused tests run, known limitations, and
data/schema impact.
