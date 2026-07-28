# Issue #14 Implementation Review

Review date: 2026-07-28
Reviewer: opencode
Scope: uncommitted HEAD changes vs `AGENTS.md`, `docs/specs/issue-14-rest-debt-levels.md`, `docs/product/design-spec.md`, `docs/adr/0003-reminder-retry-cooldown-clock-separation.md`, `docs/adr/0004-rest-debt-levels.md`

Build: ✅ `dotnet build RestCue.sln` — 0 errors, 0 warnings
Tests: ✅ `dotnet test RestCue.sln --no-build` — 297/297 Core, 45/45 App, 20/20 Infrastructure all pass

---

## Findings

### HIGH

#### H1. No test verifies `effectiveWorkInterval` override does not affect debt

**Evidence:**
- ADR-0004 §"effectiveWorkInterval and debt": "does not change debt level thresholds, the UpdateDebtDeadline() computation, or the RestDebtLevelChanged event"
- Issue spec: "effectiveWorkInterval context override 只影響 Timing，不得改寫 Need 門檻"
- Product spec §FR-009: "情境規則屬於「呈現強度」上限，不是「休息需求」的一部分"

**Code inspection:** `WorkCycleTracker.cs:696-701` — `EvaluateDebtLevel` correctly passes `workInterval` (base) not `effectiveWorkInterval`. ✅

**Gap:** `WorkCycleTrackerForegroundContextTests.cs` (620 lines, 0 debt assertions) has zero tests combining `effectiveWorkIntervalOverride` with debt-level assertions. No test proves that setting `effectiveWorkIntervalOverride` to a different value leaves `RestDebtLevel`, `RestDebtLevelChanged`, and debt deadline unchanged.

**Required fix:** Add at least one test in `WorkCycleTrackerForegroundContextTests.cs` (or `WorkCycleTrackerTests.cs`) that:
1. Sets `effectiveWorkIntervalOverride` to `TimeSpan.FromMinutes(60)` (different from default 20 min)
2. Accumulates 25 minutes of work
3. Asserts `RestDebtLevel` is still Level1 (with 20 min threshold), not Level0 (with 60 min threshold)
4. Optionally verifies `RestDebtLevelChanged` event fires at the 20 min mark, not 60 min

---

#### H2. No end-to-end test of debt level change triggering debt deadline during active cooldown

**Evidence:**
- ADR-0003 §Decision: "A supplied debt deadline without an active retry cooldown (cooldownUntil is null) does not delay or replace the normal reminder path"
- Issue spec: "與 #12 的 `SetNextDebtDeadline` seam 整合"
- ADR-0004: "When cooldown is active and the debt level changes, `UpdateDebtDeadline()` computes a new `nextDebtDeadline`"

**Code inspection:** `WorkCycleTracker.cs:712-736` — `UpdateDebtDeadline()` is called from `EvaluateDebtLevel()` (line 708) on every level change. It correctly checks `cooldownUntil.HasValue` before setting debt deadline. The existing tests call `SetNextDebtDeadline` manually to test the seam, but no test exercises the **automatic** path: accumulate enough work to cross a debt level while `cooldownUntil` is already set.

**Gap:** The integration path `cooldown active + debt level change → UpdateDebtDeadline → SetNextDebtDeadline → EarlierOf trigger` is untested end-to-end. The `Large_clock_jump` test (line 4094) tests only the event count, not the deadline integration.

**Required fix:** Add a test that:
1. Sets up a tracker with `workInterval` of 5 min, `retryCooldown` of 30 min, custom debt thresholds (e.g., L2=10 min)
2. Accumulates 5 min, reaches Level1, reaches ReminderVisible, Ignore → cooldown starts
3. Accumulates another 5 min → crosses Level2 debt threshold while cooldown is active
4. Asserts `nextDebtDeadline` is set (via `UpdateDebtDeadline`)
5. Advances clock to debt deadline (not cooldown expiry) and asserts `PendingReminder` is entered

---

### MEDIUM

#### M1. Coverage gap: no test for Level2/Level3/Level4 transitions

**Evidence:**
- Issue spec acceptance: "Level 0–4 精確邊界均有 fake-clock 測試"
- ADR-0004: level table defines all four levels

**Code inspection:** The 8 new tracker tests only cross Level1 (20 min) and Level4 (65 min jump). No test reaches Level2 or Level3 via gradual accumulation, or verifies the event sequence for Level0→Level1→Level2 crossing.

**Required fix:** Add tests that:
- Accumulate 35 min → verify Level2 at exact boundary
- Accumulate 45 min → verify Level3 at exact boundary
- Accumulate through Level1→Level2 with two events fired

---

#### M2. Coverage gap: no `WorkCycleTracker` constructor test for invalid debt thresholds

**Evidence:**
- `WorkCycleTracker.cs:84` — calls `DebtPolicy.ValidateThresholds(workInterval, l2, l3, l4)`
- `DebtPolicyTests.cs` tests `ValidateThresholds` independently, but no test verifies the tracker constructor propagates the validation

**Required fix:** Add a test in `WorkCycleTrackerTests.cs` that constructs the tracker with invalid debt thresholds (e.g., `debtLevel2 <= workInterval`) and asserts `ArgumentOutOfRangeException`.

---

#### M3. Coverage gap: `Disable()` preserves debt level; behavior undocumented in tests

**Evidence:**
- `WorkCycleTracker.cs:359-369` — `Disable()` calls `ClearReminderState()` (which doesn't touch `restDebtLevel`), then nulls cooldown/nextDebtDeadline
- `Enable()` at line 377 calls `ResetCycle()` which sets debt to Level0 and fires event
- There is no debt-level assertion in any existing disable/enable test

The product spec says Disable preserves Paused-like state, so preserving debt is correct. But the behavior is untested.

**Required fix:** Add a test that:
1. Accumulates work past Level1
2. Calls `Disable()`
3. Asserts `RestDebtLevel` is preserved (not reset)
4. Calls `Enable()`
5. Asserts `RestDebtLevel` is Level0 and `RestDebtLevelChanged` fires once

---

#### M4. Coverage gap: `Resume_preserves_nextDebtDebtline` test does not verify exact value

**Evidence:** `WorkCycleTrackerTests.cs:2869-2886` — Sets a debt deadline at `UtcNow + 15s`, pauses, resumes, and only asserts `CooldownUntil` is not null. Does not assert the debt deadline value or verify debt level is unchanged.

**Required fix:** Assert `tracker.RestDebtLevel` remains unchanged after Pause/Resume, and verify the debt deadline or cooldown deadline is reproduced.

---

### LOW

#### L1. Spec checklist has two unchecked verification items

**Evidence:** `docs/specs/issue-14-rest-debt-levels.md:71-72`

- `dotnet test RestCue.sln --no-build` — ✅ passes now (all 362 tests)
- `git diff --check` — should be run

**Required fix:** Check off both boxes in the spec completion report.

---

#### L2. `EvaluateDebtLevel()` runs during Paused/Disabled/BreakInProgress phases

**Evidence:** `WorkCycleTracker.cs:153` — `EvaluateDebtLevel()` is called unconditionally after the accumulation guard. During Paused/Disabled/BreakInProgress, `AccumulateIfWorking` is skipped so `AccumulatedWorkTime` doesn't change, making debt evaluation a no-op.

**Severity:** Low — no observable bug, but slightly imprecise. Ideally guard `EvaluateDebtLevel()` behind the same phase check as accumulation.

---

### No issues found

- **No UI leak** — `RestDebtLevelChanged` is a typed domain event with no WPF dependency. ✅
- **No clock leak** — `DebtPolicy` is pure static logic with no `IClock` dependency. ✅
- **No keyboard/mouse blocking or focus stealing** — debt logic is read-only state. ✅
- **No foreground process opt-in violation** — debt uses only `AccumulatedWorkTime`. ✅
- **Fake clock supported** — all debt tests use `FakeClock`. ✅
- **Need/Timing/Intensity separation** — debt policy is independent. ✅
- **Level 1 = workInterval** — `EvaluateDebtLevel` uses `workInterval`, not `effectiveWorkInterval`. ✅
- **Large jump fires single event** — `Large_clock_jump` test verifies 1 event. ✅
- **Reset fires event only from non-zero** — `EnterIdle()` and `ResetCycle()` gate on `previousLevel != Level0`. ✅
- **Repeated reset at Level0 emits nothing** — `Repeated_reset_at_Level0_emits_no_event` test. ✅
- **No intermediate events on large jump** — single `previous→final` event. ✅
- **Debt deadline cleared on `ClearReminderState`** — via `nextDebtDeadline = null` in `EnterReminderVisible` (called after ClearReminderState paths). ✅
- **Pause freezes debt** — `AccumulateIfWorking` skipped in Paused, `EvaluateDebtLevel` is a no-op. ✅
- **Focus Mode accumulates** — `AccumulateIfWorking` runs in Focus Mode (not excluded). ✅
- **Ignore/AutoDismissed/Dismissed not reset** — debt level unchanged by these operations. ✅
- **Event args typed with Previous/Current** — `RestDebtLevelChangedEventArgs`. ✅
- **No SQLite/schema change** — in-memory event only. ✅
