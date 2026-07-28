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

- [x] 以 `IClock` 表達 cooldown deadline，不使用 UI timer 作為真相來源。
- [x] Ignore 不再重設 Need、工作週期或可信重設時間。
- [x] `AutoDismissed` 不再重設 Need、工作週期或可信重設時間。
- [x] 冷卻中抑制新的主動提醒，但允許使用者手動開始 Break Guide。
- [x] 冷卻結束後只重新評估一次，不直接越過 Timing/Intensity。
- [x] 暴露可供 #14 提供「下一債務門檻時間」的最小 seam。
- [x] 不建立 missed-reminder queue；同一時刻最多一個 attempt。
- [x] 新增 ADR，或更新既有 ADR，說明 Need clock 與 retry clock 分離。

## Acceptance checklist

- [x] Ignore 與 `AutoDismissed` 後 Need 繼續累積。
- [x] 冷卻期內無重複可見提示。
- [x] cooldown deadline 與下一債務門檻取較早者重新評估。
- [x] 重新評估仍遵守自然停頓、全螢幕、應用規則與呈現強度。
- [x] fake-clock 覆蓋精確 deadline 前／等於／後與債務門檻較早情境。
- [x] Ignore 與 `AutoDismissed` 仍可被明確區分。

## Verification

- [x] Core targeted tests
- [x] `dotnet build RestCue.sln`
- [x] `dotnet test RestCue.sln --no-build`
- [x] `git diff --check`

## Data/schema impact

無 SQLite 綱要移轉或版本變更。`RetryCooldown` 是序列化設定 JSON 酬載的一部分（`AppSettings.RetryCooldown`），但無須資料庫移轉。

## Completion report

- [x] Changes：新增獨立 retry cooldown 與可由 #14 提供下一債務門檻 deadline
  的 seam；Ignore／AutoDismissed 保留 Need；Focus／Pause 保留 retry 狀態；
  tray 可手動啟動非 modal Break Guide；新增 ADR 0003。
- [x] Tests：Core、App 與 Infrastructure 共 320 項測試通過，並通過完整
  solution build 與 `git diff --check`。
- [x] Known limitations：本票不實作債務等級計算；#14 負責在 active cooldown
  期間提供下一門檻 deadline。Pause 期間到期的 retry 於 Resume 後下一次 tick
  重新評估，最長約 1 秒延遲。
- [x] Data/schema impact：無 SQLite schema migration 或版本變更；
  `RetryCooldown` 會加入序列化 settings JSON，舊 payload 缺少欄位時使用
  20 分鐘預設值。
