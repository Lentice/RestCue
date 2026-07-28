# Issue #11 — Passive Pause 與可信重設語義

## 來源、範圍與完整規則

- GitHub Issue #11（`修正 Passive Pause 與可信重設語義`）的 acceptance criteria 是本文件的來源。
- 活動資料只可使用 Windows 最後輸入後經過時間；不得讀取或保存輸入內容。
- Passive Pause 預設 20 秒，可設定 10–120 秒；Idle 預設 2 分鐘，可設定
  1–10 分鐘，且 Passive Pause 必須嚴格小於 Idle。
- 恢復輸入後若 Need 仍到期，回到 `PendingReminder` 等待下一次自然停頓；
  不得在恢復輸入的瞬間彈出提醒。

本票只修正 Core `WorkCycleTracker` 的 Passive Pause 與可信重設語義，以及必要的單元測試。不要新增 UI、持久化、統計資料表、輸入資料蒐集或其他 scope。

## 決策

1. 短暫無輸入達 `PassiveBreakThreshold` 時，記錄 `PassivePauseDetected`；它只影響提醒呈現（包括可隱藏已顯示的提醒）。
2. `PassivePauseDetected` 不是 `BreakCompleted`：不得清除休息需求、重設工作週期、歸零累積有效工作時間，或宣稱完成休息。
3. 可信重設事件僅為：完整 `BreakCompleted`，以及閒置達 `IdleThreshold` 後進入 `Idle`。
4. `IdleThreshold` 與 `PassiveBreakThreshold` 維持獨立；設定驗證要求嚴格的 `PassiveBreakThreshold < IdleThreshold`。
5. 所有時間邊界以既有 fake clock 測試；不得引入真實時間依賴。
6. 同一個 sample 同時超過 Passive Pause 與 Idle 門檻時，Idle 判定優先；從
   `PendingReminder` 或 `ReminderVisible` 達 Idle Threshold 都必須進入 `Idle`
   並可信重設，不能被較小的 Passive Pause Threshold 提前 return。
7. Passive Pause 期間經過的時間不得只因舊 `pendingSinceUtc` 已超過 maximum
   reminder wait，就在輸入恢復瞬間顯示提醒；恢復後必須重新等待合法 Timing。

## Acceptance checklist

- [ ] 達 Passive Pause Threshold 時發出／記錄 `PassivePauseDetected`，且能隱藏已過期或可見的提醒。
- [ ] Passive Pause 不清除休息需求、不重設工作週期，也不發出 `BreakCompleted`。
- [ ] 只有 `BreakCompleted` 或進入 `Idle` 會可信地重設休息需求。
- [ ] `PassiveBreakThreshold == IdleThreshold` 被設定驗證拒絕。
- [ ] 使用 fake clock 覆蓋 Passive Pause、Idle 與提醒可見狀態的精確邊界。
- [ ] 精確 Idle Threshold 從 `PendingReminder` 與 `ReminderVisible` 均優先進入
      `Idle`，清除 Need 但不冒充 `BreakCompleted`。
- [ ] Passive Pause 後恢復輸入不因暫停期間的舊 maximum-wait deadline 立即顯示提醒。
- [ ] 相關既有測試更新為 v1.3 用語與行為，無遺留以 `PassiveBreakCompleted` 表示完成休息的 API／斷言。
- [ ] 最低限度：執行受影響的 Core 測試。

## 非目標與限制

- 不改變 Pause、Focus Mode、全螢幕降級或前景程式規則。
- 不新增使用者活動資料；持續遵守隱私與不搶焦點產品 guardrails。
- 最終整體 `dotnet build RestCue.sln` 與 `dotnet test RestCue.sln` 由主代理執行。

## 資料／schema 影響

無。此票只改變記憶體內的狀態機語義與其測試。

## Execution checklist

- [ ] 開工前確認 GitHub #8 已關閉，並讀取 Issue #11 全文與 comments。
- [ ] 將舊 `PassiveBreakCompleted` 命名與完成語義移除或遷移為
      `PassivePauseDetected`。
- [ ] 讓 visible reminder 在 Passive Pause 時安靜隱藏，但 Need 保持到期。
- [ ] 恢復輸入時回到 pending，等待下一次自然停頓，不立即彈窗。
- [ ] 在 tracker 分支中先判斷 Idle，再判斷 Passive Pause，避免可信重設不可達。
- [ ] 讓 `IdleStarted` 與 `BreakCompleted` 成為唯一執行中可信重設路徑。
- [ ] 更新設定 validator，使 Passive Pause Threshold 嚴格小於 Idle Threshold。
- [ ] 更新相關文件與已知限制，不擴大到 persistence/UI。

## Completion report

- [ ] Changes
- [ ] Tests（命令與結果）
- [ ] Known limitations
- [ ] Data/schema impact（應為 None）
