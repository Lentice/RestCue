# OpenCode fifth follow-up — issue #12 state-transition blockers

Use the `implement` SKILL. Continue the current issue #12 implementation in this
working tree. Do not commit or modify GitHub.

## High: EndFocusMode bypasses an unexpired retry deadline

`EndFocusMode()` currently enters `PendingReminder` directly whenever total Need
is due. It must rejoin the same normal Working evaluation path:

- If cooldown/effective debt deadline is still in the future, end Focus into
  `Working` and remain suppressed.
- If the effective deadline expired during Focus, ending Focus performs one
  normal reevaluation and may enter `PendingReminder`.
- If no cooldown is active, total Need/current application interval governs.

Avoid duplicating retry logic. Route the transition through the same private
evaluation helper/path used by Working ticks. Add fake-clock tests for ending
Focus immediately before and exactly at the effective deadline.

## High: Resume resets Need and retry clocks

`Pause()` is a freeze, not a trusted reset. `Resume()` must not call
`ResetCycle()`. Preserve:

- `AccumulatedWorkTime`;
- `cooldownUntil`;
- `nextDebtDeadline`.

Pause stops effective-work accumulation. Resume should establish a safe fresh tick
baseline (no elapsed paused time added), then perform/allow normal retry/Need
reevaluation without clearing state.

Replace the old `Resume_after_pause_starts_fresh_cycle` expectation with product
semantics. Add tests covering accumulated Need preservation, no accumulation
during Pause, unexpired cooldown after Resume, and deadline expired during Pause.

## Medium: prevent stale debt deadline before cooldown

`SetNextDebtDeadline()` is a retry-cooldown seam. A deadline supplied while no
cooldown is active must not remain and influence a future Ignore. Use the simplest
explicit contract: store a supplied deadline only while `cooldownUntil` is active;
otherwise clear/ignore it. Document this contract in ADR 0003 and test:

- setting before Ignore does not cause immediate retry after Ignore;
- setting during active cooldown still selects the earlier effective deadline;
- null clears the active supplied deadline.

## Settings compatibility coverage

Add focused SQLite settings repository tests for:

- custom `RetryCooldown` JSON round-trip;
- older settings JSON without `RetryCooldown` loading the default 20 minutes.

No schema migration/version change is expected.

## Verification

Run only targeted/basic tests. Do not update final verification checkboxes; the
supervising agent owns final build/test/diff-check, completion report, commit, and
GitHub closure.

