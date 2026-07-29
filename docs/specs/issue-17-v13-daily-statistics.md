# Issue #17 — 顯示 v1.3 每日統計

## Goal

完全由 #16 的原始事件重算使用者指定本地日期的統計，並提供只在使用者主動
開啟時顯示的頁面；不得創造醫療健康分數或主動摘要。

## Dependencies and governing rules

- Blocked by #16。
- 統計只由使用者主動開啟，不提供每日自動摘要、badge 或未讀提示。
- 主動 Ignored 與被動 AutoDismissed 必須分開，前者可能反映意願，後者可能反映
  提醒能見度；不得合併成「忽略率」。
- 疲勞、完成率等數字不可描述為醫療結果。

## Scope

- Core/Application query service：以指定 `TimeZoneInfo` 與日期建立查詢區間。
- 聚合有效工作時間、BreakCompleted、Idle reset、PassivePauseDetected、
  Snooze、Ignored、AutoDismissed、最長連續工作、平均週期與 debt level/result。
- App 的今日統計被動檢視與合理空狀態。

## Event mapping

| Statistic | Source events / rule |
|---|---|
| 有效工作時間 | 只累計 `Enabled` 且不在 `Idle`、`Paused`、`FocusMode`、`Disabled` 或 break 區段的事件間隔，並裁切到查詢日 UTC 邊界 |
| 完整休息 | `BreakCompleted` 次數 |
| 明確離席重設 | 完整 `IdleStarted` → `IdleEnded` 區段的 `IdleEnded` 次數 |
| 被動停頓 | `PassivePauseDetected` 次數，不視為完成或 reset |
| 延後／主動忽略／逾時未回應 | `ReminderDismissedPayload.Result` 分別計數 |
| 最長連續工作 | 可信 reset（`BreakCompleted`、完整 Idle）或非工作狀態切斷的工作區段最大值 |
| 平均工作週期 | 查詢日內已結束工作區段的算術平均；沒有已結束區段時為空值 |
| 債務歷程 | `RestDebtLevelChangedPayload` 依 `(occurred_utc, id)` 顯示 |
| 各時段提醒結果 | `ReminderShown` 與其後第一個 dismissal／break 結果依序配對；未配對保留為未完成 |

未知 event type、無法解碼 payload 或 repository failure 不得被悄悄當成零；
query service 回傳 partial/failure 狀態供 UI 顯示安全文案。跨日狀態所需的起始
狀態，必須查詢日界前的最後相關事件重建，不得假設每日午夜重設。

## Out of scope

- 主動通知、每日摘要、紅點、成就、排名或醫療／健康分數。
- 將衍生統計另存為新的真相資料。
- 資料透明檢視（#20）與匯出（#21）。

## Execution checklist

- [x] 定義原始 event → daily statistic 的完整 mapping 與忽略未知 event 策略。
- [x] 查詢 API 接受 local date 與 timezone，不使用 UI 當下時間硬切日期。
- [x] 正確處理跨午夜 session、DST invalid/ambiguous time 與目前未完成 session。
- [x] Ignored 與 AutoDismissed 分開；Passive Pause 不列入完成休息。
- [x] Idle reset 與 BreakCompleted 分開呈現。
- [x] debt level 變化歷史與各 level 提醒結果可重算。
- [x] UI 只在使用者操作後載入；無資料與 partial-data failure 有清楚非責備文案。
- [x] 不因開啟頁面寫入 usage event 或改變統計。

## Acceptance checklist

- [x] 刪除任何衍生 cache 後，統計可由同一組原始事件得到相同結果。
- [x] 本地日界、跨日、DST 與不同 timezone 有 deterministic tests。
- [x] Ignored、AutoDismissed、PassivePauseDetected、Idle、BreakCompleted 不混算。
- [x] 沒有醫療暗示、健康分數、紅點或主動 popup。
- [x] 空資料、部分日期與 repository failure 均有安全行為。

## Verification

- [x] Core aggregation unit tests (25 tests, all pass)
- [ ] Infrastructure query integration tests（本票未新增；既有 repository round-trip 測試不等同統計查詢整合測試）
- [ ] App view/view-model tests（目前無專用 view-model 測試）
- [x] `dotnet build RestCue.sln`
- [x] `dotnet test RestCue.sln --no-build` (420 Core + 76 App + 53 Infrastructure = 549 pass)
- [x] `git diff --check`

## Data/schema impact

無 schema 變更；只讀取 v2 usage_events 表。無新索引。

## Completion report

- [x] Changes
- [x] Tests
- [x] Known limitations（含 timezone/DST）
- [x] Data/schema impact
