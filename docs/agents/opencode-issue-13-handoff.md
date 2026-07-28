# OpenCode handoff — RestCue issue #13

Use the `implement` SKILL and implement issue #13 in this repository.

## Required reading

Read these files completely before editing:

- `AGENTS.md`
- `docs/specs/issue-13-pause-focus-time-semantics.md`
- `docs/product/design-spec.md`
- `docs/adr/0002-windows-last-input-activity-boundary.md`
- `docs/adr/0003-reminder-retry-cooldown-clock-separation.md`
- `docs/architecture/overview.md`

The complete GitHub issue has no comments. Its blocker #11 is closed. The local issue
spec is the implementation contract and contains the required checklists.

## Instructions

- Implement only issue #13. Do not expand scope into debt levels, settings UI, or
  presentation-intensity policy.
- Preserve the product privacy and non-interruption guardrails in `AGENTS.md`.
- Keep all time behavior in Core and use `IClock`; do not add real delays or UI timers
  for domain timing.
- Prefer the smallest change that satisfies the spec and existing architecture.
- Add focused fake-clock Core tests and only the App wiring tests needed by the spec.
- Run basic/targeted tests only. Do not run the full solution test suite; the
  supervising agent will run full build and tests before commit.
- Do not commit, push, close the issue, or modify unrelated files. The supervising
  agent owns review, full verification, commit, and issue closure.
- When finished, report: changed files and behavior, targeted tests run/results,
  known limitations, and data/schema impact.

## Completion boundary

Do not mark checkboxes in the issue spec as complete. The supervising agent will
update them only after independent review and full verification.
