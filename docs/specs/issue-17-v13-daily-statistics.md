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

## Out of scope

- 主動通知、每日摘要、紅點、成就、排名或醫療／健康分數。
- 將衍生統計另存為新的真相資料。
- 資料透明檢視（#20）與匯出（#21）。

## Execution checklist

- [ ] 定義原始 event → daily statistic 的完整 mapping 與忽略未知 event 策略。
- [ ] 查詢 API 接受 local date 與 timezone，不使用 UI 當下時間硬切日期。
- [ ] 正確處理跨午夜 session、DST invalid/ambiguous time 與目前未完成 session。
- [ ] Ignored 與 AutoDismissed 分開；Passive Pause 不列入完成休息。
- [ ] Idle reset 與 BreakCompleted 分開呈現。
- [ ] debt level 變化歷史與各 level 提醒結果可重算。
- [ ] UI 只在使用者操作後載入；無資料與 partial-data failure 有清楚非責備文案。
- [ ] 不因開啟頁面寫入 usage event 或改變統計。

## Acceptance checklist

- [ ] 刪除任何衍生 cache 後，統計可由同一組原始事件得到相同結果。
- [ ] 本地日界、跨日、DST 與不同 timezone 有 deterministic tests。
- [ ] Ignored、AutoDismissed、PassivePauseDetected、Idle、BreakCompleted 不混算。
- [ ] 沒有醫療暗示、健康分數、紅點或主動 popup。
- [ ] 空資料、部分日期與 repository failure 均有安全行為。

## Verification

- [ ] Core aggregation unit tests
- [ ] Infrastructure query integration tests
- [ ] App view/view-model tests
- [ ] `dotnet build RestCue.sln`
- [ ] `dotnet test RestCue.sln --no-build`
- [ ] `git diff --check`

## Data/schema impact

預期無 schema 變更；只讀取 #16 原始事件。若需 index，只能在明確量測後於
separate migration 說明。

## Completion report

- [ ] Changes
- [ ] Tests
- [ ] Known limitations（含 timezone/DST）
- [ ] Data/schema impact
