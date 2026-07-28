# Issue #16 — 遷移並保存 v1.3 使用事件

## Goal

在不破壞既有設定與事件的前提下升級 SQLite schema，保存 v1.3 原始使用事件，
讓後續統計可重算；寫入失敗不得刪除有效資料或阻塞提醒核心。

## Dependencies and governing rules

- Blocked by #11、#12、#14。
- #11、#12、#14 已關閉；事件來源必須直接使用其 typed Core events/results。
- 現有 SQLite `settings` key/value table 與 `PRAGMA user_version = 1` 必須保留相容。
- 事件統計採 append-only 原始事件；統計不能只依賴不可重算的 aggregate snapshot。
- `BreakCompleted` 與 `IdleStarted` 是可信重設；Passive Pause、Snooze、Ignored、
  AutoDismissed、BreakCancelled 不是。
- 開工前建立／核准「事件來源式統計資料模型」ADR，並 review ADR-0001 的
  whole-database recovery 限制。

## Scope

- Infrastructure 的 versioned migration runner 與 usage-event repository。
- 新安裝建立最新 schema；舊 schema 原地、交易式升級。
- 保存事件：PassivePauseDetected、IdleStarted、IdleEnded、BreakStarted、
  BreakCompleted、BreakCancelled、ReminderDismissed (with ReminderResult typed payload)、
  CooldownStarted、CooldownEnded、RestDebtLevelChanged、ReminderShown、Paused、
  Resumed、FocusModeStarted、FocusModeEnded、Disabled、Enabled。
  IdleStarted/IdleEnded/BreakCancelled/CooldownStarted/CooldownEnded 是新增加到
  WorkCycleTracker 的 truthful event seam（見 WorkCycleTrackerNewEventSeamTests）。
- 保存事件發生時間、必要的非敏感 event payload 與可重算排序資訊。Payload 使用
  封閉的 discriminated union（ReminderDismissedPayload, RestDebtLevelChangedPayload），
  不接受任意 JsonElement 或 metadata bag。
- schema 由 v1 升至 v2；`usage_events.id INTEGER PRIMARY KEY` 作同 timestamp
  的 deterministic tie-breaker，所有時間以 UTC round-trip 格式正規化保存。

## Privacy boundary

不得保存 window title、輸入、clipboard、screen content、URL、document name。
Foreground process name 只有在 opt-in 開啟時才可保存，且不是本票新增的預設行為。

## Execution checklist

- [x] 盤點 #11/#12/#14 實際事件契約，不從 UI 字串反推資料。
- [x] 寫 ADR：event envelope、timestamp/時區、schema version、migration rollback、
      retention 與失敗降級。
- [x] 建立封閉的 event type 與 typed payload 契約；不得接受任意欄位 bag、
  UI 文案或 exception message 作為持久化 payload。
- [x] schema 使用 append-only `usage_events`，至少包含 `id`、`occurred_utc`、
  `event_type`、nullable typed payload；建立 chronological 與 event type + time
  索引。Debt level 存於 JSON payload 中，無獨立索引；查詢時先以 time range + event type
  過濾後再 client-side 解析 debt level，避免儲存可由事件重算的 aggregate。
- [x] migration 在 transaction 中執行，成功後才提升 `PRAGMA user_version`。
- [x] 新安裝可直接建立最新 schema；既有 v1 database 保留合法 settings。
- [x] migration runner 只接受 version 0、1、2；future version 明確拒絕且不寫入。
- [x] future schema、BUSY/LOCKED/permission/I/O error 不得 downgrade 或刪庫。
- [x] event write failure 回報可診斷但不敏感的錯誤，提醒核心繼續安全運作。
- [x] repository 查詢有 deterministic ordering，供 #17 重算。
- [x] App wiring 只訂閱 production Core events；相同事件不得因 UI refresh 重複寫入。
- [x] recovery 不得因單一壞 event 或壞 settings JSON 清除整個有效資料庫；
   修訂 ADR-0001，將設定文件 recovery 與 database corruption recovery 分離。

## Acceptance checklist

- [x] fresh install、v1→latest、重複啟動 migration 均通過 integration test。
- [x] migration 失敗會 rollback，原資料與 schema version 保持一致。
- [x] 每種 v1.3 event 可寫入、重開 repository 後查詢。
- [x] 同 timestamp 事件依 `id` 穩定排序；UTC 跨日／offset 輸入正規化後可重算。
- [x] payload/schema 靜態與動態測試確認沒有禁止資料欄位。
- [x] operational failure 不觸發 corruption recovery 或資料刪除。
- [x] timestamp 與排序足以支援跨日／時區重算。
- [x] App repository failure 測試證明事件不重複、Core phase/Need 照常推進，
   且 diagnostic 不含 payload、路徑或敏感 exception 內容。

## Verification

- [x] Infrastructure migration/repository integration tests
- [x] privacy field-name/content tests
- [x] `dotnet build RestCue.sln`
- [x] `dotnet test RestCue.sln --no-build`（454/454：345 Core、69 App、
  40 Infrastructure）
- [x] `git diff --check`

## Data/schema impact

有：SQLite schema version 提升、新增 usage-event table/index。Spec/ADR 必須記錄
from/to version、rollback、相容性與使用者資料保留行為。

## Completion report

### Changes

- Core: 新增 `UsageEventType` enum（17 個類型）、`UsageEvent` record、
  `IUsageEventRepository` interface、`UsageEventPayload` discriminated union
  （`ReminderDismissedPayload`, `RestDebtLevelChangedPayload`）
- Core: `WorkCycleTracker` 新增 6 個 truthful event seam：`BreakStarted`,
  `BreakCancelled`, `IdleStarted`, `IdleEnded`, `CooldownStarted`, `CooldownEnded`
- Infrastructure: `SchemaMigrator`（transaction-based v0/v1→v2）、
  `SqliteUsageEventRepository`（typed payload serialization, UTC normalisation,
  `ReadWrite` mode, diagnostic seam）
- Infrastructure: `SqliteSettingsRepository` 使用 `SchemaMigrator`，分離 DB
  corruption recovery 與 settings document recovery。`SettingsLoadResult` 補上
  `RecoveredFromCorruption` 註解。
- App: `BackgroundUsageEventWriter`（bounded channel, single consumer, drainable,
  `Action<string> onError` diagnostic seam）。`WireUsageEventPersistence` 訂閱全部
  17 個 tracker events，使用 typed payload 建構。
- ADR: 0005 記錄 schema v2、envelope、recovery、privacy、UTC normalisation、
  `ReadWrite` mode、typed payload contract、BackgroundUsageEventWriter。
- Spec: checklist 全數完成，事件列表反映實際 seam，payload 說明已更新。

### Tests

- 6 SchemaMigrator integration tests
- 10 SqliteUsageEventRepository integration tests
- 9 SqliteSettingsRepository tests
- 12 WorkCycleTracker new event seam tests
- 7 BackgroundUsageEventWriter ordering/drain/lifecycle tests
- Full solution 454/454 tests pass

### Known limitations

- **Malformed event payload**: `QueryAsync` throws `JsonException` on
  unparseable payload. DB preserved but query fails. Could be refined to skip
  bad rows in a future issue.
- **No retention/deletion policy**: Events accumulate indefinitely.
- **BreakCancelled**: Not fired for `HandleLock`/`HandleSleep` (those paths
  specifically exclude `BreakInProgress` from cancellation). Only fired from
  `HandleResume`/`HandleUnlock`/`EnterIdle` when they interrupt a break.
- **IdleEnded**: Fired in `TickIdle()` just before `ResetCycle()`. Not fired
  for direct `ResetCycle()` calls from other paths (e.g., HandleLock, HandleSleep).
  The next lifecycle event captures the transition.
- **CooldownEnded**: Fired from `EnterReminderVisible`,
  `TryEnterPendingReminderFromWorking`, `ResetCycle`, `EnterIdle`, and `Disable`
  when `cooldownUntil` is cleared. These are all truthful moments.

### Data/schema impact

- Schema version: 1 → 2
- New table: `usage_events`
  - `id INTEGER PRIMARY KEY AUTOINCREMENT`
  - `occurred_utc TEXT NOT NULL`
  - `event_type TEXT NOT NULL`
  - `payload TEXT`
- Indexes: `idx_usage_events_occurred_utc`, `idx_usage_events_type_time`
- Upgrade: v1 databases upgrade in-place preserving settings; fresh databases
  create v2 directly; future versions (>2) rejected without writes.
- Payload types: `ReminderDismissedPayload`, `RestDebtLevelChangedPayload`
  (serialized as JSON in `payload` column)
