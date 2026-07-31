# ADR-0004: Rest debt levels — Need/Timing/Intensity separation

## Context

Issue #14 introduces a four-level rest-debt model that quantifies the user's rest
Need as a function of total effective work time since the last trusted reset. This
replaces the previous binary notion of "Need met / not met" with a graduated scale
that guides Presentation Intensity without dictating Timing.

The product spec (sections 5.5, 5.6, 10.3, 10.4 of design-spec-v1.3) requires
three independent variables:

- **Need** – total effective work time since last trusted reset.
- **Timing** – whether it is a good moment to attempt a reminder (natural pause,
  max wait, fullscreen, lock, sleep, Pause, Focus Mode, application rules).
- **Intensity** – which presentation channel is allowed (tray icon only, edge popup,
  sound), determined by debt level, context caps, and user channel caps.

## Decision

### Debt level model

Four levels derived from total AccumulatedWorkTime since the last trusted reset
(BreakCompleted, IdleStarted, new cycle, or explicit reset):

| Level | Default threshold | Role |
|-------|------------------|------|
| 0     | < workInterval   | No meaningful rest need |
| 1     | workInterval     | Tray static micro-state |
| 2     | 35 min           | More distinct tray cue, no popup |
| 3     | 45 min           | Timing may show edge popup |
| 4     | 60 min           | Timing + user cap may add light sound |

Level 1 must always equal the base work reminder interval. Application rule
`effectiveWorkInterval` overrides are Timing-only and must not alter debt
thresholds.

### Policy independence

A static `DebtPolicy` class in `RestCue.Core.Policies` provides:
- `Evaluate(accumulatedWorkTime, l1, l2, l3, l4)` → `RestDebtLevel`
- `GetNextThreshold(currentLevel, l1, l2, l3, l4)` → `TimeSpan?`
- `ValidateThresholds(l1, l2, l3, l4)` – rejects non-positive, non-increasing.

The policy is pure logic with no IClock, WPF, or infrastructure dependency.

### Integration with WorkCycleTracker

The tracker owns a `RestDebtLevel` property and a
`RestDebtLevelChanged` event (with `Previous`/`Current` typed args).

- Debt evaluation runs inside `Tick()` after `AccumulateIfWorking`.
- A level change fires exactly one event; unchanged levels fire nothing.
- A large fake-clock jump (e.g., +65 min in one delta) fires one
  `previous → final` event, not intermediate events.
- `ResetCycle()` and `EnterIdle()` set debt to Level0 and fire an event only
  when coming from a non-zero level.
- Repeated reset at the same level emits nothing.
- The debt deadline is updated on level change: the next threshold's remaining
  work time is expressed as a monotonic elapsed-time deadline via the existing
  `SetNextDebtDeadline` seam (see ADR-0003). This ensures that during
  cooldown, crossing a debt threshold triggers a normal reminder re-evaluation
  even if the retry cooldown has not expired.

### Debt deadline integration with cooldown

When cooldown is active and the debt level changes, `UpdateDebtDeadline()`
computes a new `nextDebtDeadline` on the monotonic timeline:
`clock.Elapsed + remainingEffectiveWorkToNextThreshold` (ADR-0008). The existing
`EarlierOf(cooldownUntil, nextDebtDeadline)` logic in
`TryEnterPendingReminderFromWorking` picks whichever expires first.

Because AccumulatedWorkTime does not advance during Pause, unavailable,
lock, or sleep, the debt deadline does not implicitly advance during
those periods. The deadline is recalculated on the first work Tick
after phase transitions (Resume, HandleUnlock, HandleResume).

### effectiveWorkInterval and debt

The `effectiveWorkInterval` override from foreground application rules
controls only the Timing gate in `TryEnterPendingReminderFromWorking`.
It does not change debt level thresholds, the `UpdateDebtDeadline()`
computation, or the `RestDebtLevelChanged` event. Debt always uses the
base `workInterval` as its Level 1 threshold.

## Consequences

- Debt levels are observable via the `RestDebtLevelChanged` event and
  the `RestDebtLevel` property on `WorkCycleTracker`.
- The tracker constructor now accepts three optional debt thresholds
  (defaulting to 35, 45, 60 min).
- DebtPolicy is independently unit-testable with 19 focused tests.
- All existing issue #11–#13 tests continue to pass (297 Core tests).
- Future issues (#15 UI/intensity mapping, #16 persistence, #18/#19
  settings) can consume the event and property without modifying the
  debt computation.
- No schema change: `RestDebtLevelChanged` is an in-memory event only.

## Addendum (issue #34): the debt deadline is armed at cooldown start

The two sections above describe the debt deadline as a side effect of a level change:
"The debt deadline is updated on level change" and "when cooldown is active and the debt
level changes, `UpdateDebtDeadline()` computes a new `nextDebtDeadline`". That trigger
point is wrong. Arming only after a threshold has been crossed makes the deadline point
at the *following* threshold, so the intent recorded in ADR-0003 — the cooldown must not
delay re-evaluation past the next debt-level threshold — was never met.

The deadline is now armed when the retry cooldown starts, by `Ignore` and by
auto-dismiss, from the accumulated work time at that moment. `UpdateDebtDeadline()`
survives as a safety net for level changes that happen during an already-running
cooldown, with one added rule: it never pushes a deadline that has already come due out
into the future. Debt evaluation runs before the retry gate inside a single `Tick`, so
without that rule the recomputation would consume the crossing it was armed for and the
one-level-late behaviour would persist.

At Level 4 there is no further threshold, `GetNextThreshold` returns null, and the retry
cooldown governs alone. Everything else here stands: thresholds, the debt-level to
Presentation Intensity mapping, and the rule that `effectiveWorkInterval` affects Timing
only are all unchanged.

## Review Trigger

Review when:
- Issue #15 introduces the mapping from debt level to Presentation
  Intensity channel.
- Issue #18/#19 add persistence and UI for configurable debt thresholds.
- Dogfooding shows that the default thresholds (20/35/45/60 min) need
  adjustment.
