# OpenCode handoff — Issue #17

Use the `implement` SKILL. Implement GitHub issue #17, “顯示 v1.3 每日統計”.

Exact model selected by the user: `opencode-go/deepseek-v4-flash`.

## Read first

- `AGENTS.md`
- `docs/agents/domain.md`
- `docs/specs/issue-17-v13-daily-statistics.md`
- `docs/adr/0005-usage-event-persistence.md`
- `docs/product/windows-eye-care-assistant-design-spec-v1.3.md`, especially FR-010
- Existing usage-event domain/repository and App composition/UI code

`CONTEXT.md` is absent; proceed using the documents above.

## Required outcome

Implement every unchecked execution and acceptance item in the issue #17 spec:

- Deterministic daily aggregation from raw v2 usage events only.
- API accepts an explicit local date and `TimeZoneInfo`.
- Correct UTC boundaries, cross-midnight state reconstruction, DST behavior, and
  deterministic ordering.
- Separate BreakCompleted, trusted Idle resets, PassivePauseDetected, Snoozed,
  Ignored, and AutoDismissed.
- Recalculate work duration, longest continuous work, completed-cycle average,
  reminder outcomes, and rest-debt history.
- Unknown/malformed data and repository failure must surface as partial/failure;
  never silently turn them into zero.
- Add a user-initiated statistics view with safe empty/partial/failure copy.
- Opening or refreshing statistics must be read-only and must not emit events.
- No active summary, badge, unread marker, popup, health/medical score, shame, or
  speculative feature.
- No schema change unless an explicit measured need is documented first.

Keep code minimal and respect privacy/focus guardrails. Timing rules must not be
embedded in UI logic. Use testable seams and a fake clock where current-time behavior
is needed.

## Workflow constraints

- You may edit source, tests, spec, and handoff/review documents in this repository.
- Do not commit, push, close the issue, or alter unrelated user changes.
- Use TDD where practical.
- Run only focused/basic tests and a focused build; the controller runs the complete
  solution build/test gate later.
- Update the spec checklist truthfully as implementation progresses, but leave final
  full-suite verification and completion-report items for the controller.
- At the end, report changed files, focused tests, known limitations, data/schema
  impact, and the OpenCode session ID.
