# Issue #16 — 遷移並保存 v1.3 使用事件

## Goal

在不破壞既有設定與事件的前提下升級 SQLite schema，保存 v1.3 原始使用事件，
讓後續統計可重算；寫入失敗不得刪除有效資料或阻塞提醒核心。

## Dependencies and governing rules

- Blocked by #11、#12、#14。
- 現有 SQLite `settings` key/value table 與 `PRAGMA user_version = 1` 必須保留相容。
- 事件統計採 append-only 原始事件；統計不能只依賴不可重算的 aggregate snapshot。
- `BreakCompleted` 與 `IdleStarted` 是可信重設；Passive Pause、Snooze、Ignored、
  AutoDismissed、BreakCancelled 不是。
- 開工前建立／核准「事件來源式統計資料模型」ADR，並 review ADR-0001 的
  whole-database recovery 限制。

## Scope

- Infrastructure 的 versioned migration runner 與 usage-event repository。
- 新安裝建立最新 schema；舊 schema 原地、交易式升級。
- 至少保存：PassivePauseDetected、IdleStarted/Ended、BreakCompleted/Cancelled、
  ReminderSnoozed/Ignored/AutoDismissed、cooldown 相關事件與
  RestDebtLevelChanged。
- 保存事件發生時間、必要的非敏感 event payload 與可重算排序資訊。

## Privacy boundary

不得保存 window title、輸入、clipboard、screen content、URL、document name。
Foreground process name 只有在 opt-in 開啟時才可保存，且不是本票新增的預設行為。

## Execution checklist

- [ ] 盤點 #11/#12/#14 實際事件契約，不從 UI 字串反推資料。
- [ ] 寫 ADR：event envelope、timestamp/時區、schema version、migration rollback、
      retention 與失敗降級。
- [ ] schema 使用 append-only 原始事件，建立每日／event type／debt 查詢必要索引。
- [ ] migration 在 transaction 中執行，成功後才提升 `PRAGMA user_version`。
- [ ] 新安裝可直接建立最新 schema；既有 v1 database 保留合法 settings。
- [ ] future schema、BUSY/LOCKED/permission/I/O error 不得 downgrade 或刪庫。
- [ ] event write failure 回報可診斷但不敏感的錯誤，提醒核心繼續安全運作。
- [ ] repository 查詢有 deterministic ordering，供 #17 重算。
- [ ] recovery 不得因單一壞 event 清除整個有效資料庫；若需改 ADR-0001，明確記錄。

## Acceptance checklist

- [ ] fresh install、v1→latest、重複啟動 migration 均通過 integration test。
- [ ] migration 失敗會 rollback，原資料與 schema version 保持一致。
- [ ] 每種 v1.3 event 可寫入、重開 repository 後查詢。
- [ ] payload/schema 靜態與動態測試確認沒有禁止資料欄位。
- [ ] operational failure 不觸發 corruption recovery 或資料刪除。
- [ ] timestamp 與排序足以支援跨日／時區重算。

## Verification

- [ ] Infrastructure migration/repository integration tests
- [ ] privacy field-name/content tests
- [ ] `dotnet build RestCue.sln`
- [ ] `dotnet test RestCue.sln --no-build`
- [ ] `git diff --check`

## Data/schema impact

有：SQLite schema version 提升、新增 usage-event table/index。Spec/ADR 必須記錄
from/to version、rollback、相容性與使用者資料保留行為。

## Completion report

- [ ] Changes
- [ ] Tests
- [ ] Known limitations（含 migration/recovery）
- [ ] Data/schema impact（列出 version、tables、indexes、payload）
