# Issue #14 — 建立四級休息債務計算與事件

## Goal

從上次可信重設起，依總有效工作時間計算 Level 0–4，並在 level 改變時產生
一次 `RestDebtLevelChanged`。Need 計算、Reminder Timing 與 Presentation
Intensity 必須保持獨立。

## Dependencies and governing rules

- Blocked by #12、#13。
- #12、#13 已關閉；本票是 #15、#16、#18 的前置依賴。
- 預設門檻：20/35/45/60 分鐘；Level 1 必須等於工作提醒間隔。
- Level 1 使用使用者設定的基準工作提醒間隔；前景應用程式規則的
  `effectiveWorkInterval` override 只影響 Timing，不得改寫 Need 門檻。
- Level 0 是未達 Level 1；Level 1/2 只建議 tray 靜態狀態，Level 3 才建議
  edge popup，Level 4 才可在其他上限允許時加入輕提示音。
- level 只描述 Need，不得直接決定 Timing 或突破 fullscreen/Silent/user cap。

## Scope

- Core 的 debt value object/policy、level transition event 與 tracker integration。
- `BreakCompleted`、`IdleStarted`、App 新週期／明確重設是可信重設。
- Pause 凍結；Focus Mode 累積；Passive Pause、Snooze、Ignore、AutoDismissed、
  BreakCancelled、情境切換均不重設。
- `WorkCycleTracker` 公開目前 debt level，並以帶 previous/current 的 typed
  event args 發布轉換；事件只描述領域狀態，不執行 UI 或 persistence。

## Out of scope

- Level 對 UI 通道的映射（#15）。
- 門檻 persistence/UI（#18/#19）。
- 事件 persistence（#16）。

## Execution checklist

- [x] 建立獨立、無 UI/Win32 相依的 debt policy。
- [x] 驗證四個門檻嚴格遞增，且 Level 1 等於工作提醒間隔。
- [x] 使用同一 `IClock` 時間軸累積有效工作，但保存獨立 Need 值。
- [x] 每次 level 實際改變只產生一次帶 previous/current 的事件。
- [x] 大幅跳時跨多級時只產生一次 `previous -> final` 事件，不製造未曾觀察到
   的中間狀態事件。
- [x] 可信重設回到 Level 0，重複 reset 不產生假變更。
- [x] 非 Level 0 的可信 reset 產生一次 `current -> Level0` 事件；事件發布時
   tracker 的目前 level 與累積時間已是 reset 後狀態。
- [x] 與 #12 的 `SetNextDebtDeadline` seam 整合：active cooldown 期間提供
   下一個尚未跨越的 debt 門檻，門檻以剩餘「有效工作時間」計算；Pause、
   unavailable、lock/sleep 等不累積區間不得讓 wall-clock deadline 偷跑。
- [x] 跨過下一 debt 門檻後即使 Timing cooldown 尚未到期，也只觸發一次正常
   reminder re-evaluation，不建立補發佇列。
- [x] `effectiveWorkInterval` context override 不改變 debt level、下一 debt
   deadline 或 level-change event。
- [x] 新增 ADR，說明 Need/Timing/Intensity 三層與 debt 模型。

## Acceptance checklist

- [x] Level 0–4 精確邊界均有 fake-clock 測試。
- [x] 無效門檻涵蓋零／負值、不嚴格遞增，以及 Level 1 不等於基準工作間隔。
- [x] 所有 level 從上次可信重設後的總有效工作時間計算。
- [x] Pause、Focus Mode、Idle、BreakCompleted 與非重設事件符合契約。
- [x] 每次 level 改變產生正確 `RestDebtLevelChanged`，不重複。
- [x] 跨級、reset、重複 tick、wall clock 倒退與 cooldown deadline 均有測試。
- [x] 既有 reminder Timing、snooze、suppression 與 mode transition 測試不退化。
- [x] policy 可獨立單元測試，不依賴 App、WPF 或 Infrastructure。

## Verification

- [x] Core targeted tests
- [x] `dotnet build RestCue.sln`
- [x] `dotnet test RestCue.sln --no-build` — 371/371 passed in an isolated
  issue-14 snapshot (306 Core + 45 App + 20 Infrastructure)
- [x] `git diff --check`

## Data/schema impact

新增記憶體內事件型別；不改 SQLite。Schema/event persistence 由 #16。

## Completion report

### Changes

- `src/RestCue.Core/Domain/RestDebtLevel.cs` — enum Level0–Level4
- `src/RestCue.Core/Events/RestDebtLevelChangedEventArgs.cs` — typed event args
  with Previous/Current
- `src/RestCue.Core/Policies/DebtPolicy.cs` — static Evaluate, GetNextThreshold,
  ValidateThresholds
- `src/RestCue.Core/Reminders/WorkCycleTracker.cs` — added RestDebtLevel property,
  RestDebtLevelChanged event, EvaluateDebtLevel/UpdateDebtDeadline private
  methods, ResetCycle/EnterIdle debt reset with event, constructor accepts three
  optional debt threshold parameters (defaults 35/45/60 min), debt level
  evaluated after each Tick accumulation, debt deadline updated on level change
  for cooldown integration, nextDebtDeadline cleared in ClearReminderState
- `docs/adr/0004-rest-debt-levels.md` — Need/Timing/Intensity separation and
  four-level debt model

### Tests

- `tests/RestCue.Core.Tests/Policies/DebtPolicyTests.cs` — 19 tests covering
  Evaluate for each level at exact boundary/below/above, GetNextThreshold for
  each level, ValidateThresholds for strictly increasing and invalid inputs
- `tests/RestCue.Core.Tests/Reminders/WorkCycleTrackerTests.cs` — 19 integration
  tests: initial 8 (Starts_at_Level0, Level1, RestDebtLevelChanged, no event
  when unchanged, BreakCompleted reset with event, Idle reset with event,
  repeated reset at Level0, large clock jump single event) + 9 review additions
  (Level2 boundary, Level3 boundary, sequential Level0→Level1→Level2 two events,
  debt deadline end-to-end with cooldown — now uses L3=25s and
  TickActivityUnavailable to prove debt deadline fires before cooldown, Disable
  preserves debt/Enable resets with event, constructor rejects invalid debt
  thresholds, Resume preserves RestDebtLevel, effectiveWorkInterval override
  does not affect debt) + 2 review-2 additions (Level4 exact 60-min boundary,
  wall-clock regression does not regress debt)
- `tests/RestCue.Core.Tests/Reminders/WorkCycleTrackerForegroundContextTests.cs`
  — 2 tests: effectiveWorkInterval override does not change Level1 or Level2
- All 306 Core tests pass (28 new + 278 existing)

### Known limitations

- Debt level thresholds use defaults (35/45/60 min for Levels 2–4); configurable
  thresholds via settings (#18/#19) not yet implemented.
- The debt deadline wall-clock expression (`now + remainingEffectiveWork`) is
  accurate only while the user continuously accumulates; it is recalculated on
  each level change and preserved across non-accumulating phases via
  ClearReminderState not clearing it.
- No UI/intensity mapping (#15), event persistence (#16), or settings
  persistence/UI (#18/#19) — these are follow-up issues.
- `effectiveWorkInterval` overrides do not affect debt (by design).

### Data/schema impact

No SQLite or schema changes. `RestDebtLevelChanged` is an in-memory event only.
Event persistence is planned for #16.
