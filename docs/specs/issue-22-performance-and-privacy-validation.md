# Issue #22 — 自動化驗證核心效能與隱私界線

## Goal

建立可重現、自動化的長時間／資源、狀態轉移、資料與日誌隱私驗證，產出清楚
pass/fail 與量測 artifacts，而不是只留下人工觀察。

## Dependencies and governing rules

- Blocked by #10、#15、#17、#20、#21。
- 背景閒置 CPU 平均目標低於 0.5%，記憶體目標低於 150 MB。
- activity polling 建議每秒一次；不得每秒寫入 SQLite。
- tray 平時只能事件驅動切換靜態 icon；必要淡入淡出不得超過 10 FPS，完成後停止渲染。
- release candidate 必須可在 Windows 10/11 穩定常駐 8 小時，且無 Critical/High
  未決缺陷。

## Scope

- 對 one-second activity polling、event-driven tray updates、SQLite writes、
  CPU/memory/handle growth 建立 harness。
- 對完整 v1.3 state transitions 建立 fake-clock scenario tests。
- 對 logs、database、export artifacts 建立禁止資料掃描。
- 可在 CI 執行的短版與 Windows 長時間本機版。

## Success thresholds

CPU 與 memory 使用上列門檻。Handle/thread/DB growth 沒有固定數值，須先量
baseline，在 test plan 記錄 hardware/OS/build/configuration，並以 8 小時內無
持續單調且無界成長作為最低判定；若觀察到成長，必須建立 issue，不可自行放寬。

## Execution checklist

- [ ] 建立 deterministic state-scenario harness，涵蓋 Working、Passive Pause、
      Idle、Snooze、Ignore、AutoDismissed、Break completed/cancelled、Pause、
      Focus Mode、debt levels、fullscreen/silent downgrade。
- [ ] 以短版測試確認活動來源平均約每秒一次，不因 UI 重複建立 poller。
- [ ] 計數 tray render/update，穩定狀態不得每秒重建 icon 或持續動畫。
- [ ] 計數 SQLite writes；單純 poll 不得每秒寫入無意義 snapshot。
- [ ] 建立 8 小時 soak harness，定期記 CPU、working set、private bytes、handles、
      threads、DB size 與 write counts。
- [ ] 對 logs/DB/export 的 schema 與內容跑 privacy denylist/allowlist 檢查。
- [ ] 故障注入包含 activity unavailable、audio failure、DB locked、fullscreen
      unknown、sleep/large clock gap。
- [ ] test artifacts 不包含禁止資料，並可由另一 agent 重跑。
- [ ] 更新 `docs/testing/test-plan.md` 與 known limitations。

## Acceptance checklist

- [ ] one-second polling 沒有 duplicate loop；穩定 tray 沒有持續更新／動畫。
- [ ] 核准門檻下的 CPU、memory、handle、thread 與 DB growth 測試通過。
- [ ] logs、DB、export 不含 window title、input、clipboard、screen、URL、
      document name；process name 遵守 opt-in。
- [ ] 核心 v1.3 所有主要轉移有自動化 fake-clock 覆蓋。
- [ ] 短版 CI 與長版 Windows soak 均有單一文件化命令與 machine-readable 結果。
- [ ] failure injection 不導致 focus steal、input block、資料刪除或 crash loop。

## Verification

- [ ] `dotnet build RestCue.sln`
- [ ] `dotnet test RestCue.sln --no-build`
- [ ] 執行短版 performance/privacy harness
- [ ] 執行並保存一次完整 8 小時 soak 結果
- [ ] `git diff --check`

## Data/schema impact

無產品 schema 變更。測試 artifacts 只能放明確測試輸出位置且不得含使用者資料。

## Completion report

- [ ] Changes
- [ ] Tests/measurements（含環境、threshold、結果）
- [ ] Known limitations
- [ ] Data/schema impact
