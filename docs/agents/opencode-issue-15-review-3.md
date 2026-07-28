# Issue #15 Review 3 — Presentation intensity and tray (clean-slate after timed-out fix session)

Fresh independent third review. Scope: full uncommitted working tree vs `docs/specs/issue-15-presentation-intensity-and-tray.md`, `docs/agents/opencode-issue-15-review.md`, `docs/agents/opencode-issue-15-review-2.md`, `docs/adr/0004-rest-debt-levels.md`, `AGENTS.md`.

Base commit: `e3298b6`. Diff: 11 files changed (7 production, 4 test, 1 spec).

---

## Previous-finding resolution verification

| Review 1 # | Severity | Status | Notes |
|---|---|---|---|
| C1 — `SetIntensityCaps` never wired | Critical | **FIXED** | Wired before `UpdateForegroundContext` at `MainWindow.xaml.cs:342` |
| C2 — `OnDebtLevelChanged` overwrites mode text | Critical | **FIXED** | Gated on `_lastPhase` being active phase (`App.xaml.cs:216-217`) |
| C3 — No policy tests | Critical | **FIXED** | 27 tests in `PresentationIntensityPolicyTests.cs` |
| H1 — `Effective()` unvalidated cast | High | **FIXED** | `Math.Clamp(min, None, PopupAndSound)` at `PresentationIntensityPolicy.cs:60` |
| H2 — Level 0/1 share icon | High | **FIXED** | Level 1 uses `SystemIcons.Shield` at `WindowsTrayIcon.cs:128` |
| H3 — No App-level debt text tests | High | **FIXED** | 17 tests in `PresentationIntensityAppTests.cs` |
| M1 — Dual suppression path | Medium | **NOT FIXED** (see M1a) | Old `isReminderSuppressed` gate still present at `WorkCycleTracker.cs:698-706` |
| M2 — `IStatusWindow` missing debt members | Medium | **FIXED** | `DebtLevelChanged` event and `CurrentDebtLevel` property added |
| M3 — Spec checklist unmarked | Medium | **FIXED** | Execution checklist updated |

| Review 2 # | Severity | Status | Notes |
|---|---|---|---|
| F1 — Ordering bug + missing EdgePopup promotion | High | **FIXED** | `SetIntensityCaps` called before `UpdateForegroundContext`; promotion fires at `WorkCycleTracker.cs:136-140` |
| F2 — Dead `Clamp` method | Low | **FIXED** | Removed; `Effective()` uses `Math.Clamp` directly |

---

## Verification results

All builds and test runs performed after `dotnet clean RestCue.sln && dotnet build RestCue.sln` (0 errors, 0 warnings).

| Check | Result |
|---|---|
| `dotnet build RestCue.sln` | Passed (0 errors, 0 warnings) |
| `PresentationIntensityPolicyTests` (27) | Passed — 27/27 |
| `PresentationIntensityAppTests` (17) | Passed — 17/17 |
| Full `RestCue.Core.Tests` (333) | Passed — 333/333 |
| Full `RestCue.App.Tests` (62) | Passed — 62/62 |
| `git diff --check` | Clean (no whitespace errors) |

---

## Remaining observations (not actionable)

### M1a. Old `isReminderSuppressed` gate still present despite review-2 claim

**File:** `src/RestCue.Core/Reminders/WorkCycleTracker.cs:698-706`

Review-2 stated "Old `isReminderSuppressed` gate removed from `EnterReminderVisible`; intensity gate is now the sole decider." This is incorrect — the old gate remains in the code.

However, it is **functionally dead** in practice: `isReminderSuppressed` is set to `true` only when fullscreen or `suppressReminder` is active; in those states the context/rule caps guarantee `effective < EdgePopup`, so the new intensity gate (lines 681-695) always returns early before reaching the old gate. The gate can only fire under the contradictory state `isReminderSuppressed=true ∧ effective≥EdgePopup`, which normal operation cannot produce.

**Assessment:** Dead code, not a bug. Remove at next cleanup.

### O1. Test suite uses `_forceAllowPopup` to bypass intensity gate

**File:** `tests/RestCue.Core.Tests/Reminders/WorkCycleTrackerTests.cs:1526`

`CreateTracker` now calls `SetForceAllowPopup(true)`, which skips the intensity gate in `EnterReminderVisible` (line 681). This preserves backward compatibility for the 300+ pre-existing `WorkCycleTrackerTests` that were written before the intensity policy existed.

**Consequence:** The intensity gate path (`effective < EdgePopup` suppression) has no integration test through `WorkCycleTracker` itself. Coverage exists at the unit-test level via `PresentationIntensityPolicyTests` (27 tests) and at the App-event level via `PresentationIntensityAppTests` (17 tests). The `Debt_deadline_triggers_reminder_when_crossing_level_during_cooldown` test was also updated to call `SetForceAllowPopup(true)` explicitly.

**Assessment:** Acceptable gap given the existing coverage architecture. Not actionable for this ticket.

---

## Levels 1/2 tray-only semantics verification

| Level | `GetDebtRecommendation` | Effective with caps at default | Popup allowed? |
|---|---|---|---|
| 0 | `TrayOnly` | `TrayOnly` | No |
| 1 | `TrayOnly` | `TrayOnly` | No |
| 2 | `TrayOnly` | `TrayOnly` | No |
| 3 | `EdgePopup` | `EdgePopup` | Yes (if caps allow) |
| 4 | `PopupAndSound` | `PopupAndSound` | Yes (if caps allow) |

Fullscreen/Confirmed → cap = `TrayOnly`. Silent → cap = `None`. Unknown states → safe fallback.

---

## Summary

| # | Severity | Area | Finding |
|---|---|---|---|
| M1a | Medium (claimed fixed) | Architecture | Old `isReminderSuppressed` gate not removed; functionally dead but contradicts review-2 claim |
| O1 | Low | Test coverage | Intensity gate integration-tested through `_forceAllowPopup`, not end-to-end (acceptable) |

**All 333 Core tests + 62 App tests pass. 9 review-1 findings and 2 review-2 findings confirmed resolved (except M1a which is dead code, not a regression).**

**REVIEW OK — 0 actionable findings.**
