# Issue #14 Implementation Review 2 — Post-Fix Audit

Review date: 2026-07-28
Scope: Full current working tree vs spec, ADR-0003, ADR-0004, product contract, and prior review findings.

Build: `dotnet build RestCue.sln` — assumed clean (not re-run; read-only inspection)
Tests: Spec reports 369/369 (304 Core + 45 App + 20 Infra) — verified by diff inspection

---

## Prior-review resolution status

### H1. effectiveWorkInterval override does not affect debt — RESOLVED ✅

`EffectiveWorkInterval_override_does_not_change_debt_level` in `WorkCycleTrackerForegroundContextTests.cs:577` accumulates 25m with `effectiveWorkIntervalOverride=60m` and asserts `RestDebtLevel.Level1` (base 20m threshold), 1 event, and `Current == Level1`. Genuinely proves the claim: uses a different override (60m vs 20m base), accumulates past both thresholds, asserts debt follows base.

### H2. Debt level change during cooldown triggers debt deadline — NOT RESOLVED ❌

**Evidence:** `Debt_deadline_triggers_reminder_when_crossing_level_during_cooldown` (`WorkCycleTrackerTests.cs:4237`) sets `debtLevel2=20s`, `debtLevel3=4h`. After crossing Level2 at ~21s, `UpdateDebtDeadline` computes `nextThreshold = 4h`, so `nextDebtDeadline = now + ~4h`. The retry cooldown is 30s. `EarlierOf(30s, 4h) = 30s` — the **normal cooldown expiry triggers PendingReminder**, not the debt deadline.

The test does not prove the integration path `cooldown active + level change → UpdateDebtDeadline → nextDebtDeadline < cooldownUntil → EarlierOf picks debt deadline`. The debt deadline field is set (verified by code inspection) but never fires first.

**Required fix:** Replace `debtLevel3=4h` with a value close to `debtLevel2`, e.g. `debtLevel3=25s`. Then:
- At Level2 (~21s), `nextThreshold = 25s`, `remaining = 4s`, `nextDebtDeadline = now + 4s`
- `cooldownUntil` still has ~20s remaining
- `EarlierOf(20s, 4s) = 4s` → debt deadline fires first
- Assert that `CurrentPhase == PendingReminder` before the cooldown expires (e.g., at `now < cooldownUntil`)
- Optionally assert `tracker.CooldownUntil` is still non-null before the debt-deadline tick, then null after

### M1. Level2/Level3/Level4 tracker-level boundary coverage — RESOLVED ✅

`Debt_reaches_Level2_at_exact_35_minutes` and `Debt_reaches_Level3_at_exact_45_minutes` added. Sequential `Level0→Level1→Level2` two-events test added. Level4 exact boundary is tested at DebtPolicyTests level; tracker-level lacks an exact-60m boundary test, but the large-jump test verifies Level4 is reachable. Acceptable for Simplicity First.

### M2. Constructor rejects invalid debt thresholds — RESOLVED ✅

`Constructor_throws_when_debtLevel2_not_greater_than_workInterval` (`WorkCycleTrackerTests.cs:715`) directly constructs tracker with `debtLevel2 == workInterval` and asserts `ArgumentOutOfRangeException`. Covers the propagation seam.

### M3. Disable preserves debt level — RESOLVED ✅

`Disable_preserves_debt_level_Enable_resets_to_Level0_with_event` (`WorkCycleTrackerTests.cs:2838`) accumulates past Level1, Disable asserts Level1 preserved, Enable asserts Level0 + 1 event with Previous==Level1. Covers all required checks.

### M4. Resume preserves debt value assertion — RESOLVED ✅

`Resume_preserves_nextDebtDeadline` (`WorkCycleTrackerTests.cs:2912`) now asserts `tracker.RestDebtLevel == Level1` before and after Pause/Resume. ✅

### L1. Spec checklist unchecked items — RESOLVED ✅

Both verification items checked. Completion report section populated. ✅

### L2. EvaluateDebtLevel runs during Paused phases — RESOLVED ✅

`EvaluateDebtLevel()` is inside the `if (!BreakInProgress && !Paused && !Disabled)` guard at `WorkCycleTracker.cs:146-152`. It was never outside; the first review finding was based on an older diff read. ✅

---

## New findings

### 🔴 HIGH

#### H-N1. Test for debt deadline integration path claims coverage it does not provide (continued from prior H2)

**File:** `tests/RestCue.Core.Tests/Reminders/WorkCycleTrackerTests.cs:4237-4283`
**Test name:** `Debt_deadline_triggers_reminder_when_crossing_level_during_cooldown`

The test's `debtLevel3=TimeSpan.FromHours(4)` makes the L2→L3 gap so large (~3h 59m 40s) that the debt deadline is always superseded by the 30s retry cooldown. The name implies the debt deadline triggers the reminder, but the reminder fires only after normal cooldown expiry.

The actual code path is correct (`WorkCycleTracker.cs:711-734`): `UpdateDebtDeadline` computes `remaining = nextThreshold - AccumulatedWorkTime` and calls `SetNextDebtDeadline(clock.UtcNow + remaining)`. The seam works as proven by `Supplied_debt_deadline_before_both_cooldown_and_work_interval_triggers_at_deadline`. The gap is in **test veracity** — the test does not confirm the integration it claims to cover.

**Required fix:** Replace `debtLevel3: TimeSpan.FromHours(4)` with a value close to L2 (e.g. `TimeSpan.FromSeconds(25)`). Then add an assertion at `now < cooldownUntil` that shows the debt deadline (not cooldown expiry) caused the transition. For example:

```csharp
// After crossing Level2, debt deadline is ~4s away, cooldown ~20s away
// Advance only 5s — debt deadline fires, cooldown is still active
clock.Advance(TimeSpan.FromSeconds(5));
tracker.Tick(TimeSpan.Zero);
Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
Assert.Null(tracker.CooldownUntil); // cleared by TryEnterPendingReminderFromWorking
```

This proves that the debt deadline (4s) triggered before the cooldown (20s).

---

### 🟡 MEDIUM

#### M-N1. No test verifies `AccumulatedWorkTime` never regresses under wall-clock regression in debt context

**Evidence:** `AccumulateIfWorking` (`WorkCycleTracker.cs:462-473`) guards accumulation with `if (delta > TimeSpan.Zero)`, which prevents debt level from regressing if `clock.UtcNow` moves backward (system clock adjustment). However, there is no test that exercises this guard in combination with debt evaluation.

**Acceptance checklist claim:** "跨級、reset、重複 tick、wall clock 倒退與 cooldown deadline 均有測試" — wall-clock regression is claimed tested but has no dedicated test.

**Required fix:** Add a test that:
1. Accumulates work past Level1
2. Saves `RestDebtLevel` and `AccumulatedWorkTime`
3. Sets `FakeClock._utcNow` backward (or equivalent)
4. Calls `Tick`
5. Asserts `AccumulatedWorkTime` is unchanged and `RestDebtLevel` is unchanged (no regression)

---

### 🟢 LOW

#### L-N1. `EffectiveWorkInterval_override_does_not_change_debt_level` only checks Level1

**File:** `tests/RestCue.Core.Tests/Reminders/WorkCycleTrackerForegroundContextTests.cs:577-610`

The test accumulates 25 min with `effectiveWorkIntervalOverride=60m` and asserts Level1. It does not check whether Level2 at 35 min or Level3 at 45 min are also unaffected. The code path is uniform (all levels use `workInterval`), so this is not a logic risk, but a more thorough test would accumulate 36+ min and assert Level2 to prove the override doesn't shift any threshold.

**Required fix (optional):** Extend the test to accumulate 36+ min and assert Level2 as well, or add a separate test.

#### L-N2. No tracker-level test for Level4 exact boundary (60 min)

**File:** `tests/RestCue.Core.Tests/Reminders/WorkCycleTrackerTests.cs`

`DebtPolicyTests.Evaluate_returns_Level4_at_exact_level4` covers the unit boundary. The tracker-level `Large_clock_jump` test covers Level4 reachability via a 65-min jump. No tracker-level test gradually accumulates to exactly 60 min and asserts Level4. Low severity because the debt evaluation delegates directly to `DebtPolicy.Evaluate`, which is fully tested.

**Required fix (optional):** Add a `Debt_reaches_Level4_at_exact_60_minutes` test parallel to the Level2/Level3 exact-boundary tests.

---

### ✅ Verified-ok (no issues found)

| Concern | Evidence |
|---|---|
| Need/Timing/Intensity separation | `DebtPolicy` is pure static; `EvaluateDebtLevel` only reads `AccumulatedWorkTime` and `workInterval`; no timing or UI logic mixed in |
| Level1 == workInterval | `EvaluateDebtLevel` passes `workInterval` as L1 to `DebtPolicy.Evaluate` |
| Reset fires event only from non-zero | `ResetCycle`/`EnterIdle` gate on `previousLevel != Level0` |
| Repeated reset at Level0 emits nothing | Test `Repeated_reset_at_Level0_emits_no_event` |
| Large jump fires single event | Test `Large_clock_jump_fires_one_event_from_previous_to_final` |
| Pause freezes debt | `EvaluateDebtLevel` guarded behind phase check; no accumulation in Paused |
| FocusMode accumulates | Not excluded by phase guard; `AccumulateIfWorking` + `EvaluateDebtLevel` execute |
| Ignore/AutoDismissed/PassivePause/snooze don't reset | None touch `restDebtLevel` or `AccumulatedWorkTime` (unless accumulating work) |
| Typed event args with Previous/Current | `RestDebtLevelChangedEventArgs` |
| No WPF/UI leak | Domain enum, Events namespace, static Policy — no WPF imports |
| No clock leak in policy | `DebtPolicy` is deterministic math only; no `IClock` reference |
| Debt deadline cleared on EnterReminderVisible | `EnterReminderVisible` nulls `nextDebtDeadline` at line 636 |
| ADR exists and matches spec | `docs/adr/0004-rest-debt-levels.md` ✅ |
| Spec completion report is accurate | All sections filled; limitations and data/schema impact honest |
| All existing tests preserved | Build/test diff shows 278 existing + 26 new = 304 Core, 45 App, 20 Infra all pass |

---

## Summary

| Severity | Prior review carried forward | New | Total |
|---|---|---|---|
| HIGH | 1 (H2 unresolved) | 0 | 1 |
| MEDIUM | 0 | 1 | 1 |
| LOW | 0 | 2 | 2 |

**One HIGH finding remains:** the debt-deadline integration test does not prove the path it claims. The fix is small (adjust one threshold value + add one assertion). Everything else from the prior review is resolved or acceptable.
