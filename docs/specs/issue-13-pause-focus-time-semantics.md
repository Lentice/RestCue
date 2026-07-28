# Issue #13 — 校正 Pause 與 Focus Mode 的有效工作時間

## Goal

讓 Pause 與 Focus Mode 使用不同且可驗證的時間語義：Pause 凍結 Need；
Focus Mode 持續累積 Need 但完全抑制主動提醒，結束後最多產生一次提醒嘗試。

## Dependencies and governing rules

- Blocked by #11。
- Pause 可由 UI 提供 15 分鐘、30 分鐘、1 小時或手動恢復；本票只負責 Core 語義。
- Focus Mode 預設 60 分鐘；期間 Need 與有效工作時間持續累積。
- Pause 保留進入前 Need 並停止累積；Disabled 不保留執行中週期，重新啟用時
  建立新週期；Idle 進入時清除 Need。

## Scope

- Core 的 phase transition、有效工作時間與 Need 累積。
- Pause/Resume 與 Focus Mode start/end 的 fake-clock 測試。
- 模式期間 Lock、Idle、Disable 與 activity unavailable 的既有安全語義。

## Out of scope

- Pause duration 選單與設定 UI（#19）。
- 債務 level 的門檻與事件（#14）。
- 提醒呈現強度（#15）。

## Execution checklist

- [ ] 明確保存 Pause 前的 Need 值，Pause 期間不回填經過時間。
- [ ] Resume 依保存的 Need 回到 `Working` 或 `PendingReminder`。
- [ ] Focus Mode 期間有效工作時間與 Need 正常累積。
- [ ] Focus Mode 期間不顯示 popup、tray cue 或播放主動提示音。
- [ ] 結束 Focus Mode 時若 Need 到期，只建立一個 pending attempt。
- [ ] Focus Mode 期間跨過多個週期不得形成補發佇列。
- [ ] Disable 建立新週期的既有語義不被 Pause/Focus 改寫。
- [ ] 所有判斷使用 `IClock`，不加入真實 delay。

## Acceptance checklist

- [ ] Pause 停止有效工作與債務累積，並保留既有 Need。
- [ ] Focus Mode 持續累積，但完全抑制主動提醒。
- [ ] Focus Mode 結束後最多一次到期提醒，且仍等待合法 Timing。
- [ ] fake-clock 覆蓋進入、期間、精確到期、恢復、鎖定與 unavailable 邊界。
- [ ] 既有非法 phase transition 測試仍通過。

## Verification

- [ ] Core targeted tests
- [ ] App command wiring tests（若有變更）
- [ ] `dotnet build RestCue.sln`
- [ ] `dotnet test RestCue.sln --no-build`
- [ ] `git diff --check`

## Data/schema impact

無。

## Completion report

- [ ] Changes
- [ ] Tests
- [ ] Known limitations
- [ ] Data/schema impact
