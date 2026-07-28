# OpenCode handoff — RestCue issue #15

Use the **implement** SKILL to implement GitHub issue #15 in this repository.

## Read first

1. `AGENTS.md`
2. `docs/specs/issue-15-presentation-intensity-and-tray.md`
3. `docs/agents/domain.md`
4. `docs/product/design-spec.md`
5. Relevant v1.3 product-contract sections 5.5.1–5.5.3, 5.6, FR-004,
   FR-004a, 10.4, and C-003
6. `docs/adr/0004-rest-debt-levels.md`
7. Relevant issue #8 fullscreen/application-rule implementation and tests
8. Current tray, lifecycle, reminder-window, and `WorkCycleTracker` production
   wiring and tests

There is no root `CONTEXT.md`; follow `docs/agents/domain.md`.

## Constraints

- Implement only issue #15. Do not add settings UI/persistence (#18/#19), event
  persistence (#16), or recompute debt (#14).
- Keep Need, Timing, and Presentation Intensity independent.
- Use a pure Core presentation-intensity policy with explicit safe channel caps.
- The effective allow-list is the minimum of debt recommendation, context cap,
  and user cap. Unknown/invalid values must fail safely, never escalate.
- Maximum-wait expiry affects Timing only. Every popup/sound production path must
  still pass the effective-intensity gate.
- Fullscreen and TrayOnly permit only a static tray state. Silent, Pause, and
  Focus Mode must not create popup/sound attempts. Never clear debt to suppress
  presentation.
- Provide non-color tray differentiation for Level 0–4 and Disabled (plus existing
  mode states where applicable) using static shape/badge and accessible Tooltip
  text. No animation, flashing, red dot, unread count, countdown, focus stealing,
  modal UI, or sensitive data.
- Prefer production seams and mapping functions that App tests can exercise
  without instantiating real WPF/NotifyIcon windows.
- Avoid speculative abstractions. Preserve unrelated changes/files.
- Add the next ADR only if a durable architectural decision not already captured
  by ADR-0004 is necessary.
- Update checklist/completion report only for verified work.
- Do not commit; the supervising agent owns the final full suite and commit.

## Test budget

Use TDD at Core/App production seams where practical. Run focused Core/App tests
and targeted builds only. Do not run the full solution test suite.

Report changed files, focused tests, known limitations (especially what cannot be
automated about real Windows tray/screen-reader behavior), and data/schema impact.
