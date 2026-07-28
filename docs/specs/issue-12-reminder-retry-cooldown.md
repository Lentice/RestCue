# Issue #12 — 將提醒重試冷卻與休息需求時鐘分離

## Goal

Ignore 與 `AutoDismissed` 只結束目前提醒嘗試並啟動 retry cooldown；Need 與
有效工作時間持續累積。冷卻不得跨過下一債務門檻而不重新評估。

## Dependencies and governing rules

- Blocked by #11。
- Retry cooldown 預設 20 分鐘，可設定 1–60 分鐘。
- Snooze 預設 5 分鐘，可設定 1–30 分鐘，與 Retry cooldown 是不同設定。
- 自然停頓預設 5 秒；最大等待預設 3 分鐘。重新評估仍必須遵守這兩個 Timing
  條件以及 presentation cap。
- 本票需建立或更新 ADR，記錄提醒重試冷卻與休息需求時鐘分離。

## Scope

- Core 建立獨立的 retry cooldown deadline/state。
- Ignore 與 `AutoDismissed` 共用 cooldown 規則，但保留不同 `ReminderResult`。
- Snooze 保持使用 Snooze duration，不混入 retry cooldown。
- 下一債務等級門檻若早於 cooldown deadline，提前觸發重新評估。

## Out of scope

- 債務等級計算本身（#14）、設定持久化（#18）、事件持久化（#16）。
- 補發佇列或一次顯示多個提醒。

## Execution checklist

- [ ] 以 `IClock` 表達 cooldown deadline，不使用 UI timer 作為真相來源。
- [ ] Ignore 不再重設 Need、工作週期或可信重設時間。
- [ ] `AutoDismissed` 不再重設 Need、工作週期或可信重設時間。
- [ ] 冷卻中抑制新的主動提醒，但允許使用者手動開始 Break Guide。
- [ ] 冷卻結束後只重新評估一次，不直接越過 Timing/Intensity。
- [ ] 暴露可供 #14 提供「下一債務門檻時間」的最小 seam。
- [ ] 不建立 missed-reminder queue；同一時刻最多一個 attempt。
- [ ] 新增 ADR，或更新既有 ADR，說明 Need clock 與 retry clock 分離。

## Acceptance checklist

- [ ] Ignore 與 `AutoDismissed` 後 Need 繼續累積。
- [ ] 冷卻期內無重複可見提示。
- [ ] cooldown deadline 與下一債務門檻取較早者重新評估。
- [ ] 重新評估仍遵守自然停頓、全螢幕、應用規則與呈現強度。
- [ ] fake-clock 覆蓋精確 deadline 前／等於／後與債務門檻較早情境。
- [ ] Ignore 與 `AutoDismissed` 仍可被明確區分。

## Verification

- [ ] Core targeted tests
- [ ] `dotnet build RestCue.sln`
- [ ] `dotnet test RestCue.sln --no-build`
- [ ] `git diff --check`

## Data/schema impact

無。本票只建立記憶體內 cooldown；#16 才保存相關事件。

## Completion report

- [ ] Changes
- [ ] Tests
- [ ] Known limitations
- [ ] Data/schema impact
