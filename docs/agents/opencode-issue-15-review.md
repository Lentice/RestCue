# Issue #15 Review — Presentation intensity and tray

Reviewed against: `docs/specs/issue-15-presentation-intensity-and-tray.md`, `docs/agents/opencode-issue-15-handoff.md`, `docs/adr/0004-rest-debt-levels.md`, `docs/product/design-spec.md` (sections 5.5.1–5.5.3, 5.6, FR-004, FR-004a, 10.4, C-003), `AGENTS.md`.

Base commit: `5d4bd28` (Fix #14). Diff: 12 files changed, 254 insertions, 30 deletions.

---

## Critical (MUST FIX before closing)

### C1. `SetIntensityCaps` never wired into production code — fullscreen/silent caps are dead code

**File:** `src/RestCue.App/MainWindow.xaml.cs:331-343`  
**Evidence:** `ApplyForegroundContext()` calls `workCycleTracker.UpdateForegroundContext(fullscreen, suppressReminder, trayOnly, customInterval)` — the old mechanism. It never calls `workCycleTracker.SetIntensityCaps(...)`.

**Root cause:** `SetIntensityCaps` exists on `WorkCycleTracker` (`src/RestCue.Core/Reminders/WorkCycleTracker.cs:126-130`) and the policy defines `FromFullscreenState()` and `FromApplicationRuleType()` (`src/RestCue.Core/Policies/PresentationIntensityPolicy.cs:28-49`), but neither is invoked from the main clock tick path. The `_contextCap` / `_userCap` fields always remain at `DefaultContextCap`/`DefaultUserCap` (both `PopupAndSound`).

**Consequence:** The new `EnterReminderVisible` gate (`WorkCycleTracker.cs:650`) only ever considers `GetDebtRecommendation(debtLevel)`. Fullscreen, Silent, and TrayOnly application-rule suppression still relies entirely on the *old* `isReminderSuppressed` flag set by `UpdateForegroundContext`. The spec requires "popup/sound 執行前使用同一 effective intensity 結果做最後 gate" — this is not achieved.

**Required fix:** In `ApplyForegroundContext()`, after computing the foreground context, call:

```
workCycleTracker.SetIntensityCaps(
    PresentationIntensityPolicy.FromFullscreenState(context.FullscreenState),
    PresentationIntensityPolicy.FromApplicationRuleType(rule?.RuleType ?? ApplicationRuleType.Normal));
```

Then verify the old `isReminderSuppressed` path in `EnterReminderVisible` (lines 662–669) is either removed or becomes a secondary fallback that cannot override the intensity gate.

---

### C2. `OnDebtLevelChanged` overwrites mode-phase tray text (Focus Mode, Paused, Idle, Disabled)

**File:** `src/RestCue.App/App.xaml.cs:208-214`  
**Evidence:**
```csharp
private void OnDebtLevelChanged(object? sender, RestDebtLevelChangedEventArgs e)
{
    _trayIcon.SetDebtLevel(e.Current);
    _trayIcon.SetStatusText(GetStatusTextForDebtLevel(e.Current));  // unconditional
}
```

**Scenario:** During Focus Mode, debt accumulates and can cross thresholds. When `RestDebtLevelChanged` fires (e.g., Level1→Level2):
1. `OnDebtLevelChanged` writes `"RestCue – 明顯疲勞 (Level 2)"` to tray text.
2. `OnRestDebtLevelChanged` also calls `UpdateCycleStatus()`, but phase is still FocusMode → no `PhaseChanged` → `ApplyPhaseToTray` is NOT called.
3. Final tray text: `"RestCue – 明顯疲勞 (Level 2)"` instead of `"RestCue – 專注模式"`.

Same bug for Paused, Idle, and Disabled if a debt-level change event fires while in those phases (during Idle the debt is reset to Level0 on entry, so the Idle case specifically triggers it: debt text appears briefly before `PhaseChanged` corrects it — a cosmetic flash, but still wrong).

**Required fix:** Gate the `SetStatusText` call on the current phase:
- If phase is Working/PendingReminder/ReminderVisible/Snoozed → set debt-level text.
- If phase is Paused/FocusMode/Idle/Disabled/BreakInProgress → do NOT overwrite; the phase handler already set the correct text.
Alternatively, unify into a single tray-update method that receives both phase and debt level, similar to `ApplyPhaseToTray`.

---

### C3. No unit tests for `PresentationIntensityPolicy`

**File:** `tests/RestCue.Core.Tests/Policies/` — no `PresentationIntensityPolicyTests.cs` exists.

**Evidence:** `src/RestCue.Core/Policies/PresentationIntensityPolicy.cs` has 6 public methods/accessors. Zero are exercised by any test. The acceptance checklist requires "policy unit tests 覆蓋所有 level × context cap × user cap 關鍵組合".

**Missing coverage:**
- `GetDebtRecommendation` for all 5 levels + unknown fallback
- `Effective(debtRec, contextCap, userCap)` for Level0–4 × 3 cap levels × boundary combinations (e.g., debt=EdgePopup + context=TrayOnly → result=TrayOnly)
- `FromFullscreenState` for Confirmed, Uncertain, NotFullscreen, unknown
- `FromApplicationRuleType` for Normal, TrayOnly, Silent, CustomInterval, unknown
- Fallback safety: unknown enum values must never escalate

**Required fix:** Add `tests/RestCue.Core.Tests/Policies/PresentationIntensityPolicyTests.cs` with data-driven tests covering the above matrix.

---

## High

### H1. `Effective()` returns unvalidated cast — unknown values could escalate

**File:** `src/RestCue.Core/Policies/PresentationIntensityPolicy.cs:56-60`  
**Evidence:**
```csharp
var min = a < b ? (a < c ? a : c) : (b < c ? b : c);
return (PresentationIntensity)min;
```

No bounds check or `Enum.IsDefined` guard. If any input were outside the defined enum range (e.g., `(PresentationIntensity)999`), `min` would be 999 and the cast would produce an undefined value. The comparison `effective < PresentationIntensity.EdgePopup` at `WorkCycleTracker.cs:650` would then evaluate on `int` basis — an undefined value could be greater than `EdgePopup` and escalate permission unexpectedly.

**Practical risk:** Low today because `GetDebtRecommendation` has a safe fallback and caps are validated in `SetIntensityCaps`. But the method itself is a fragile API.

**Required fix:** Clamp to `[None, PopupAndSound]` or validate result with `Enum.IsDefined` + fallback to `None`.

---

### H2. Level 0 and Level 1 share an identical tray icon

**File:** `src/RestCue.App/Lifecycle/WindowsTrayIcon.cs:123-131`  
**Evidence:**
```csharp
return level switch
{
    RestDebtLevel.Level2 => Level2Icon,       // Warning triangle
    RestDebtLevel.Level3 => SuppressedIcon,   // Exclamation
    RestDebtLevel.Level4 => Level4Icon,       // Error X
    _ => NormalIcon                            // Level 0 AND Level 1 both → info "i" icon
};
```

The spec (5.5.3, FR-004a) requires "等級除色調外，須有靜態形狀／徽記或 Tooltip 文字差異" for all levels. Tooltip text distinguishes them (`"監視中 (Level 0)"` vs `"輕微疲勞 (Level 1)"`), but the icon does not. This also violates NFR-005 ("不只用顏色表示狀態") — a user relying on the icon alone sees no difference between Level 0 and Level 1.

**Required fix:** Assign a distinct icon for Level 1, or change Level 0 to use no icon modifier/transparency while Level 1 uses the current `NormalIcon`. Ensure all 5 levels have visually distinct static shapes.

---

### H3. No App-level tests for debt tooltip text or debt-level event wiring

**Evidence (grep):** No test references `GetStatusTextForDebtLevel`, `GetStatusTextForPhase`, `OnDebtLevelChanged`, or `SetDebtLevel` (aside from no-op stubs in fake tray classes).

**Missing coverage in App tests:**
- `GetStatusTextForDebtLevel`: Level0–4 + unknown → correct Chinese text
- `GetStatusTextForPhase`: all phases → correct Chinese text
- Debt level change → tray icon updated to correct icon
- Debt level change → status text updated to correct debt-level text (for active phases)
- Debt level change → status text NOT overwritten during mode phases (see C2)

**Required fix:** Add data-driven unit tests for the two `GetStatusText*` methods and an integration test for the `DebtLevelChanged` handler chain using `FakeTrayIcon`.

---

## Medium

### M1. Dual suppression path in `EnterReminderVisible` creates redundancy and confusion

**File:** `src/RestCue.Core/Reminders/WorkCycleTracker.cs:642-669`  
**Evidence:** The new intensity gate (lines 647–660) and the old `isReminderSuppressed` gate (lines 662–669) both suppress reminders and fire `ReminderSuppressed`. They share `hasSuppressedReminder` and `showTrayCue` state, creating coupling between two conceptually independent mechanisms.

Once C1 is fixed (caps are wired), the old gate becomes dead code for the fullscreen/silent case. However, the old `isReminderSuppressed` flag is still set by `UpdateForegroundContext` and could have semantic meaning outside `EnterReminderVisible`. A review of whether `isReminderSuppressed` is referenced elsewhere is needed.

**Required fix:** After wiring caps (C1), audit `isReminderSuppressed` usage. If it is now fully redundant with the intensity gate, remove the old path and `isReminderSuppressed` field.

---

### M2. `IStatusWindow` interface not updated with `DebtLevelChanged` / `CurrentDebtLevel`

**File:** `src/RestCue.App/Lifecycle/IStatusWindow.cs` (20 lines, only 7 methods, no events or properties)

**Evidence:** `MainWindow` adds `DebtLevelChanged` event and `CurrentDebtLevel` property that are not part of `IStatusWindow`. The test `FakeStatusWindow` in `ApplicationLifecycleTests.cs` doesn't have them either — but that test doesn't exercise debt-level wiring, so no build error. However, this means any other `IStatusWindow` implementer would silently lack debt-level integration.

**Required fix:** Add `event EventHandler<RestDebtLevelChangedEventArgs>? DebtLevelChanged;` and `RestDebtLevel CurrentDebtLevel { get; }` to `IStatusWindow`. Add stub implementations to `FakeStatusWindow`.

---

### M3. Execution checklist in spec still unmarked

**File:** `docs/specs/issue-15-presentation-intensity-and-tray.md:35-49`  
**Evidence:** All `- [ ]` boxes in the execution checklist are unchecked. The completion report (lines 77–80) is also unmarked.

**Required fix:** Update checkboxes to reflect verified items and document known limitations.

---

## Summary

| # | Severity | Area | Finding |
|---|----------|------|---------|
| C1 | Critical | Production wiring | `SetIntensityCaps` never called; fullscreen/silent caps are dead code |
| C2 | Critical | App event handler | `OnDebtLevelChanged` overwrites mode-phase tray text |
| C3 | Critical | Test coverage | No tests for `PresentationIntensityPolicy` (0% coverage of 6 members) |
| H1 | High | Policy safety | `Effective()` returns unvalidated cast |
| H2 | High | Tray icon | Level 0 and Level 1 share identical icon |
| H3 | High | Test coverage | `GetStatusTextForDebtLevel`, `GetStatusTextForPhase`, `OnDebtLevelChanged` untested |
| M1 | Medium | Architecture | Dual suppression paths in `EnterReminderVisible` |
| M2 | Medium | Interface | `IStatusWindow` missing debt-level members |
| M3 | Medium | Documentation | Execution checklist in spec still all unmarked |

**Overall assessment:** The foundation is solid — `PresentationIntensity` enum, the policy class, the tray icon slots, the event chain from `WorkCycleTracker` → `MainWindow` → `App` → tray are all in place. But the implementation is critically incomplete: the new caps are defined but never wired (C1), and the debt-level event can corrupt mode-phase display (C2). Without these fixes the intensity policy has no real effect on fullscreen/silent suppression. Full test coverage for the policy and App seams is also missing (C3, H3).

REVIEW NOT OK — 9 actionable findings, 3 critical.
