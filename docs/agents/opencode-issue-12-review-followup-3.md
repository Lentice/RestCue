# OpenCode third follow-up — issue #12 final review blockers

Use the `implement` SKILL. Continue the current issue #12 implementation in this
working tree. Do not commit or modify GitHub.

## High: ordinary cooldown expiry bypasses current application interval

`TickWorking` currently transitions unconditionally to `PendingReminder` when the
effective retry deadline expires. This bypasses a longer
`effectiveWorkInterval` selected by an application rule during cooldown.

Implement these exact semantics:

- Before the earlier effective retry deadline, suppress the normal attempt.
- If the supplied next-debt deadline is the effective deadline and is reached,
  that deadline itself establishes Need eligibility. Clear retry state and enter
  `PendingReminder` once.
- If ordinary cooldown is the effective deadline and is reached, clear retry
  state, then re-evaluate Need using the current `effectiveWorkInterval`. Enter
  `PendingReminder` only if that current interval is due; otherwise remain
  `Working` and let the normal Need condition become due later.
- Neither path bypasses downstream natural pause, maximum wait, fullscreen,
  application presentation policy, or intensity caps.

Add a failing-before-fix test where an application rule changes to a longer custom
interval during cooldown; at ordinary cooldown expiry the tracker must remain
Working, then become Pending only when the custom interval is due.

## Retry-path acceptance coverage

Add focused tests that exercise the new retry path together with:

- maximum wait while continuous input prevents natural pause;
- fullscreen/TrayOnly or Silent suppression/presentation cap;
- the custom application interval case above.

Reuse existing seams and helpers. Avoid duplicating broad generic tests.

## Actual tray integration coverage

The current test only proves that a .NET event invokes a lambda attached by the
test itself. Add focused tests proving:

- the real `WindowsTrayIcon` "立即休息" item raises `BreakNowRequested` when
  clicked; and
- the application command-binding code wires `BreakNowRequested` to
  `IStatusWindow.StartBreakNow`.

Extract only the minimum internal/static command binder needed to test without
launching WPF; use the repository's existing test visibility pattern. Do not add
UI automation.

## Boundary and documentation fixes

- Change the "after deadline" fake-clock test so its first tick occurs after an
  already-passed deadline, rather than first transitioning exactly at the
  deadline and then remaining Pending.
- Update the issue spec Data/schema impact: no SQLite schema migration/version
  change, but `RetryCooldown` is part of the serialized settings JSON payload.
- Do not claim full verification checklist completion; the supervising agent owns
  final full build/test/diff-check and checklist update.

Run only targeted/basic tests. Report changes, test results, limitations, and
data/schema impact.

