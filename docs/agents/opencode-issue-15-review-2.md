# Issue #15 Review 2 — Presentation intensity and tray (post-fix)

Fresh independent review after fixes against: `docs/specs/issue-15-presentation-intensity-and-tray.md`, `docs/agents/opencode-issue-15-review.md`, `docs/adr/0004-rest-debt-levels.md`, `docs/product/design-spec.md`, `AGENTS.md`.

Base commit: `e3298b6`. Uncommitted diff: 10 files changed, 222 insertions, 36 deletions.

---

## Previously-fixed findings (not re-reported)

| Review 1 # | Severity | Resolution |
|---|---|---|
| C1 | Critical | `SetIntensityCaps` now wired in `ApplyForegroundContext()` at `MainWindow.xaml.cs:344-347` |
| C2 | Critical | `OnDebtLevelChanged` gates `SetStatusText` on `_lastPhase` being an active phase at `App.xaml.cs:216-217` |
| C3 | Critical | `PresentationIntensityPolicyTests.cs` with 27 tests covering policy, fullscreen, rule, effective, and cap-constant values |
| H1 | High | `Effective()` now clamps via `Math.Clamp(min, None, PopupAndSound)` at `PresentationIntensityPolicy.cs:60` |
| H2 | High | Level 1 now uses distinct `SystemIcons.Shield` icon at `WindowsTrayIcon.cs:128` |
| H3 | High | `PresentationIntensityAppTests.cs` with 17 tests covering `GetStatusTextForDebtLevel`, `GetStatusTextForPhase`, and wiring stubs |
| M1 | Medium | Old `isReminderSuppressed` gate removed from `EnterReminderVisible`; intensity gate is now the sole decider |
| M2 | Medium | `IStatusWindow` now includes `DebtLevelChanged` event and `CurrentDebtLevel` property |
| M3 | Medium | Spec execution checklist updated to reflect verified items |

## New findings

### F1. Ordering bug: `SetIntensityCaps` called after `UpdateForegroundContext` — context release evaluates with stale caps

**File:** `src/RestCue.App/MainWindow.xaml.cs:338-348`  
**Evidence:**
```csharp
// 1. Old mechanism with context release (uses stale _contextCap)
workCycleTracker.UpdateForegroundContext(...);

// 2. New caps set too late
workCycleTracker.SetIntensityCaps(combinedCap, PresentationIntensityPolicy.DefaultUserCap);
```

**Root cause:** `ApplyForegroundContext()` calls the old `UpdateForegroundContext()` first, then the new `SetIntensityCaps()` second. Inside `UpdateForegroundContext` (`WorkCycleTracker.cs:421-425`), when the context transitions from suppressed to non-suppressed (e.g., exiting fullscreen), it calls `EnterReminderVisible(now)`. At that point `_contextCap` still holds the **previous** cap value (e.g., `TrayOnly` from fullscreen), so `GetEffectiveIntensity()` computes the wrong effective intensity and incorrectly keeps the reminder suppressed.

Then `SetIntensityCaps` updates `_contextCap` to the correct value (`PopupAndSound`), but only checks tray-cue visibility change (`WorkCycleTracker.cs:135-136`). It does **not** promote a suppressed reminder when the effective crosses from `< EdgePopup` to `>= EdgePopup`.

**Consequence:** A reminder that should be shown after exiting fullscreen (because debt recommends `EdgePopup`+ and the new caps allow it) stays suppressed until the next tick's `TickPending` re-checks max-wait/natural-pause. In marginal cases where the user exits fullscreen before max-wait has elapsed, the reminder can remain suppressed for the full pending duration, defeating the intensity policy.

**Required fix:** Reorder `ApplyForegroundContext` to call `SetIntensityCaps` **before** `UpdateForegroundContext`, so the caps are current when the release path evaluates `EnterReminderVisible`:

```csharp
// Set caps FIRST
var fsCap = PresentationIntensityPolicy.FromFullscreenState(context.FullscreenState);
var ruleCap = PresentationIntensityPolicy.FromApplicationRuleType(rule?.RuleType ?? ApplicationRuleType.Normal);
var combinedCap = (PresentationIntensity)Math.Min((int)fsCap, (int)ruleCap);
workCycleTracker.SetIntensityCaps(combinedCap, PresentationIntensityPolicy.DefaultUserCap);

// Then update foreground context (release will use correct caps)
workCycleTracker.UpdateForegroundContext(...);
```

---

### F2. Dead private method `Clamp` in `PresentationIntensityPolicy`

**File:** `src/RestCue.Core/Policies/PresentationIntensityPolicy.cs:64-67`  
**Evidence:** The private `Clamp` method exists but `Effective()` now uses `Math.Clamp(min, (int)None, (int)PopupAndSound)` directly at line 60. `Clamp` is never called anywhere.

**Required fix:** Remove the unused `Clamp` method (lines 64-67).

---

## Verification results

| Check | Result |
|---|---|
| `dotnet build RestCue.sln` | Passed (0 errors, 0 warnings) |
| `PresentationIntensityPolicyTests` (27) | Passed — all 27 / 27 |
| `PresentationIntensityAppTests` (17) | Passed — all 17 / 17 |
| Full `RestCue.App.Tests` (62) | Passed — all 62 / 62 |
| Full `RestCue.Core.Tests` (333) | 107 failed — **all pre-existing** (WorkCycleTracker tests expecting direct `ReminderVisible` transition; known `ReachReminderVisible` baseline issue from #14 test migration, documented in spec Verification section) |
| `git diff --check` | Clean (no whitespace errors) |

## Summary

| # | Severity | Area | Finding |
|---|---|---|---|
| F1 | High | Production wiring | `SetIntensityCaps` called after `UpdateForegroundContext` → context release evaluates with stale caps; also missing EdgePopup promotion in `SetIntensityCaps` |
| F2 | Low | Dead code | Unused private `Clamp` method in `PresentationIntensityPolicy` |

All 9 findings from the original review are confirmed fixed. The two remaining issues are new: an ordering/promotion bug (F1) and dead code (F2). Neither blocks the functional correctness of the intensity gate under steady-state or max-wait-triggered paths, but F1 can cause a 1-tick-to-indefinite delay in popup visibility after exiting fullscreen depending on timing.

**REVIEW NOT OK** — 2 actionable findings (1 High, 1 Low).
