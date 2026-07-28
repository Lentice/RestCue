# OpenCode follow-up — issue #12 review blockers

Use the `implement` SKILL. Continue the current issue #12 implementation in this
working tree. Do not commit or modify GitHub.

The supervising subagent reviewed the latest result and found two High-severity
blockers. Fix the implementation and tests; do not merely rename tests or change
the ADR/spec to describe current behavior.

## Blocker 1: earlier-of cooldown or next debt threshold

The current `WorkRemainingUntilNextDebtThreshold` property is not the required
seam. It derives an output from `effectiveWorkInterval` and
`lastReminderWorkTime`; issue #14 cannot provide the next debt threshold/deadline.
`TickWorking` still returns until `cooldownUntil`, so a threshold earlier than the
cooldown cannot cause reevaluation.

Implement the minimum explicit seam through which #14 can supply the next debt
threshold timing/deadline. The active retry deadline must be the earlier of:

- Ignore/AutoDismissed retry cooldown deadline; and
- the supplied next debt threshold deadline.

At that earlier instant, perform one normal reevaluation. Do not bypass natural
pause, maximum wait, fullscreen/application rules, or presentation caps. Do not
implement debt-level calculation itself.

Replace the misleading
`Cooldown_debt_threshold_earlier_than_cooldown_starts_flow_after_expiry` test with
fake-clock coverage that proves the supplied threshold actually ends suppression
before the cooldown deadline. Cover immediately before, exactly at, and after the
effective earlier deadline as appropriate.

Update ADR 0003 so every statement matches the implementation.

## Blocker 2: user-accessible manual Break Guide

`ManualStartBreak()` exists only in Core and is unreachable from the application.
Wire a user command through the established tray architecture:

- add the minimal event/command to `ITrayIcon`;
- add a non-modal “start break now” tray menu item to `WindowsTrayIcon`;
- bind it in `App.WireTrayCommands()` / `MainWindow`;
- invoke `WorkCycleTracker.ManualStartBreak()` and display the existing Break
  Guide without stealing focus or blocking input.

Add focused App tests that prove the tray command is wired and can start Break
Guide while the tracker is in cooldown/Working state. Follow existing patterns;
do not add speculative UI abstractions.

## Verification and report

Run only targeted/basic tests. Report:

- Changes made
- Basic tests and results
- Known limitations
- Data/schema impact
- Any remaining unchecked spec item

