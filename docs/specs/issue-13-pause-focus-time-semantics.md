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
- App 對模式切換事件的最小 wiring：進入 Pause 或 Focus Mode 時關閉現有主動
  提醒呈現，期間不得重新顯示；離開模式後只依 Core phase 決定後續呈現。
- 模式期間 Lock、Idle、Disable 與 activity unavailable 的既有安全語義。

## Out of scope

- Pause duration 選單與設定 UI（#19）。
- 債務 level 的門檻與事件（#14）。
- 提醒呈現強度（#15）。

## Execution checklist

- [x] 明確保存 Pause 前的 Need 值，Pause 期間不回填經過時間。
- [x] Pause 可從 `Working`、`PendingReminder`、`ReminderVisible`、`Snoozed`
  進入；清除當次 reminder/snooze 呈現狀態，但保留 Need 與 retry cooldown。
- [x] Resume 先回到 `Working`，再由下一次正常 tick 依保存的 Need、retry
  cooldown 與 Timing 規則決定是否進入 `PendingReminder`，不得直接顯示提醒。
- [x] Focus Mode 期間有效工作時間與 Need 正常累積。
- [x] Focus Mode 可從 `Working`、`PendingReminder`、`ReminderVisible`、
  `Snoozed` 進入；清除當次 reminder/snooze 呈現狀態，但保留 Need 與 retry
  cooldown。
- [x] Focus Mode 期間不顯示 popup、tray cue 或播放主動提示音。
- [x] 結束 Focus Mode 時若 Need 已到期且 retry gate 允許，只建立一個
  `PendingReminder` attempt；若尚未到期或仍在 cooldown 則回到 `Working`。
- [x] 結束 Focus Mode 不得直接進入 `ReminderVisible`；自然停頓、最大等待、
  全螢幕、應用程式規則與呈現上限仍由既有正常路徑判斷。
- [x] Focus Mode 期間跨過多個週期不得形成補發佇列。
- [x] Disable 建立新週期的既有語義不被 Pause/Focus 改寫。
- [x] Lock、Idle 與 activity unavailable 仍採既有安全重設／停止累積語義，
  不因模式恢復而回填無法證明的有效工作時間。
- [x] 所有判斷使用 `IClock`，不加入真實 delay。

## Acceptance checklist

- [x] Pause 停止有效工作與債務累積，並保留既有 Need。
- [x] Focus Mode 持續累積，但完全抑制主動提醒。
- [x] Focus Mode 結束後最多一次到期提醒，且仍等待合法 Timing。
- [x] fake-clock 覆蓋各合法來源 phase、進入、期間、精確到期、跨多週期、
  恢復、retry cooldown、鎖定、Idle 與 unavailable 邊界。
- [x] App 測試證明進入 Pause/Focus 會撤除現有主動呈現，模式期間沒有 popup
  或 tray cue，離開後不會繞過 Core/Timing 路徑。
- [x] 既有非法 phase transition 測試仍通過。

## Verification

- [x] Core targeted tests
- [x] App command wiring tests（若有變更）
- [x] `dotnet build RestCue.sln`
- [x] `dotnet test RestCue.sln --no-build`
- [x] `git diff --check`

## Data/schema impact

無。

## Completion report

- [x] Changes — Pause 僅接受四個合法來源 phase 並凍結 Need；Focus Mode 持續
  累積且在 Idle threshold 可信重設。放棄模式前的提醒 attempt 時清除 stale
  suppression flag，離開 Focus 最多回到一個 `PendingReminder`。App 使用可測的
  production seams 撤除 popup、連接 tray commands，並清除模式中的 tray cue。
- [x] Tests — fake-clock 覆蓋合法／非法來源、Need 與 cooldown 保留、精確到期、
  多週期、Idle、lock/unavailable、stale suppression 與合法 Timing；App 覆蓋
  production command wiring、popup cleanup/Core transition seam 及 phase-to-tray
  呈現。完整 build 0 warnings/errors；solution tests 335/335 通過。
- [x] Known limitations — 未在自動化測試中建立真實 WPF/NotifyIcon 視窗；使用
  production seams 與 tracking fakes 驗證撤除、狀態映射及不繞過 Core Timing。
- [x] Data/schema impact — 無。
