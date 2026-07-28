# Issue #14 — 建立四級休息債務計算與事件

## Goal

從上次可信重設起，依總有效工作時間計算 Level 0–4，並在 level 改變時產生
一次 `RestDebtLevelChanged`。Need 計算、Reminder Timing 與 Presentation
Intensity 必須保持獨立。

## Dependencies and governing rules

- Blocked by #12、#13。
- 預設門檻：20/35/45/60 分鐘；Level 1 必須等於工作提醒間隔。
- Level 0 是未達 Level 1；Level 1/2 只建議 tray 靜態狀態，Level 3 才建議
  edge popup，Level 4 才可在其他上限允許時加入輕提示音。
- level 只描述 Need，不得直接決定 Timing 或突破 fullscreen/Silent/user cap。

## Scope

- Core 的 debt value object/policy、level transition event 與 tracker integration。
- `BreakCompleted`、`IdleStarted`、App 新週期／明確重設是可信重設。
- Pause 凍結；Focus Mode 累積；Passive Pause、Snooze、Ignore、AutoDismissed、
  BreakCancelled、情境切換均不重設。

## Out of scope

- Level 對 UI 通道的映射（#15）。
- 門檻 persistence/UI（#18/#19）。
- 事件 persistence（#16）。

## Execution checklist

- [ ] 建立獨立、無 UI/Win32 相依的 debt policy。
- [ ] 驗證四個門檻嚴格遞增，且 Level 1 等於工作提醒間隔。
- [ ] 使用同一 `IClock` 時間軸累積有效工作，但保存獨立 Need 值。
- [ ] 每次 level 實際改變只產生一次帶 previous/current 的事件。
- [ ] 大幅跳時可跨多級，但最終狀態正確；明確決定事件是逐級或單次並測試。
- [ ] 可信重設回到 Level 0，重複 reset 不產生假變更。
- [ ] 與 #12 的 next-threshold seam 整合，使 cooldown 可提早重新評估。
- [ ] 新增 ADR，說明 Need/Timing/Intensity 三層與 debt 模型。

## Acceptance checklist

- [ ] Level 0–4 精確邊界均有 fake-clock 測試。
- [ ] 所有 level 從上次可信重設後的總有效工作時間計算。
- [ ] Pause、Focus Mode、Idle、BreakCompleted 與非重設事件符合契約。
- [ ] 每次 level 改變產生正確 `RestDebtLevelChanged`，不重複。
- [ ] policy 可獨立單元測試，不依賴 App、WPF 或 Infrastructure。

## Verification

- [ ] Core targeted tests
- [ ] `dotnet build RestCue.sln`
- [ ] `dotnet test RestCue.sln --no-build`
- [ ] `git diff --check`

## Data/schema impact

新增記憶體內事件型別；不改 SQLite。Schema/event persistence 由 #16。

## Completion report

- [ ] Changes
- [ ] Tests
- [ ] Known limitations
- [ ] Data/schema impact
