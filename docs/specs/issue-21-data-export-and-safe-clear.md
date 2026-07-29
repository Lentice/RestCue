# Issue #21 — 匯出並安全清除本機資料

## Goal

讓使用者匯出允許的本機資料，並經明確二次確認後分別清除 usage history 或
settings。操作完成後 UI 與統計不得顯示 stale data。

## Dependencies and governing rules

- Blocked by #16、#19。
- 所有資料預設只留本機；export 必須由使用者主動觸發並選擇目的地。
- History clear 與 Settings reset 是兩個不同操作，必須分別確認。
- Settings reset 後前景程式名稱 collection 回到預設關閉。

## Scope

- 明確、版本化的 export 格式與 privacy allowlist。
- 分開的 Clear history 與 Reset settings 操作。
- 二次確認、transactional clear、failure recovery 與 UI cache invalidation。

## Out of scope

- 雲端同步、上傳、分享 API、自動匯出或 background export。
- secure erase 的物理磁區保證；若 SQLite 無法保證，文件需精確說明。
- 一鍵同時清除所有資料，除非另有 scope review。

## Implementation guidance for agents

本節是給實作 Agent 的具體施工說明。不要新增本節未列出的檔案或型別。

### 檔案地圖

| 路徑 | 動作 | 內容 |
| --- | --- | --- |
| `src/RestCue.Core/DataManagement/UsageEventExport.cs` | 新增 | 匯出 DTO：`UsageEventExportDocument`（含 `SchemaVersion`、`ExportedAtUtc`、`Events`）與 `UsageEventExportRecord` |
| `src/RestCue.Core/DataManagement/IUsageDataExporter.cs` | 新增 | `Task<ExportResult> ExportAsync(string destinationPath, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)` |
| `src/RestCue.Core/DataManagement/UsageDataExporter.cs` | 新增 | 由 `IUsageEventRepository.QueryAsync` 取事件 → 映射到 allowlist DTO → 交給 `IExportWriter` 落地 |
| `src/RestCue.Core/DataManagement/IExportWriter.cs` | 新增 | 檔案落地抽象（temp file + atomic replace），讓 Core 可測 |
| `src/RestCue.Core/DataManagement/IUsageDataMaintenance.cs` | 新增 | `Task<ClearResult> ClearUsageHistoryAsync(CancellationToken ct = default)`；**只清 usage events** |
| `src/RestCue.Core/DataManagement/ClearResult.cs`、`ExportResult.cs` | 新增 | `record`：`Succeeded`、`AffectedRowCount`／`WrittenPath`、`ErrorMessage`；失敗不丟到 UI 層 |
| `src/RestCue.Infrastructure/DataManagement/AtomicJsonExportWriter.cs` | 新增 | 寫 `{path}.tmp` → `Flush`/`Dispose` → `File.Move(tmp, path, overwrite: true)`；失敗刪 tmp |
| `src/RestCue.Infrastructure/UsageEvents/SqliteUsageDataMaintenance.cs` | 新增 | `DELETE FROM usage_events;` 在 `BeginTransactionAsync` 中執行，`ReadWrite` 模式 |
| `src/RestCue.Core/Settings/ISettingsRepository.cs` | 不改 | settings reset 用既有 `SaveAsync(AppSettings.Default)`，不新增介面成員 |
| `src/RestCue.App/DataManagementWindow.xaml` / `.xaml.cs` | 新增 | 匯出／清除歷史／重設設定三個獨立按鈕 + 各自二次確認 |
| `src/RestCue.App/Lifecycle/ITrayIcon.cs` | 修改 | 新增 `event EventHandler? DataManagementRequested;` |
| `src/RestCue.App/Lifecycle/WindowsTrayIcon.cs` | 修改 | 在 `ContextMenuStrip` 加「匯出／清除資料」menu item |
| `src/RestCue.App/App.xaml.cs` | 修改 | `internal void WireDataManagementCommand(ITrayIcon)`；清除成功後重建 `StatisticsWindow`／`TransparencyWindow` 的資料來源 |
| `docs/privacy.md` | 修改 | 補述 SQLite `DELETE` 不保證物理抹除、是否執行 `VACUUM`、`-wal` 檔殘留 |

### 可重用的既有型別

- `IUsageEventRepository.QueryAsync(from, to)`（`src/RestCue.Core/UsageEvents/`）是
  匯出的唯一資料來源。匯出前重新查詢，不要輸出 UI 已快取的物件。
- `UsageEvent`（`Id`、`OccurredUtc`、`EventType`、`Payload`）與
  `UsageEventPayload` 的兩個子型別（`ReminderDismissedPayload`、
  `RestDebtLevelChangedPayload`）就是全部可匯出的內容。ADR-0005 已保證
  payload 是封閉型別、不含禁止欄位——匯出時**逐一顯式映射**這兩個子型別，
  不要 `JsonSerializer.Serialize(usageEvent)` 整包丟出去。
- `AppSettings.Default`（`CollectForegroundProcessNames` 預設為 `false`）就是
  settings reset 的目標值；`SqliteSettingsRepository.SaveAsync` 已含
  `AppSettingsValidator` 驗證，直接用。
- `SchemaMigrator.LatestSchemaVersion`（值 2）用來在匯出文件記錄來源 schema。
- `DailyStatisticsService`（in-flight #17）與 #20 的 transparency service 都是
  從原始 `usage_events` 即時重算／重查的。因此清除歷史後它們**自然**回到空值，
  唯一要處理的是 App 層持有的 window／service 實例與其已渲染文字。

不要建立：第二條聚合路徑、任何 `statistics_cache`／`export_log`／
`derived_daily_totals` 表、`IUsageEventRepository` 的 delete 成員（維護操作
與寫入路徑分開，避免 `BackgroundUsageEventWriter` 誤用）。

### 實作順序

1. **匯出格式與落地路徑決策（先定，再寫程式）**：
   - 格式：UTF-8（無 BOM）JSON，單一 `UsageEventExportDocument` 物件。
   - `SchemaVersion`：獨立的 export 格式版本，值為 `1`，**不要**沿用
     `SchemaMigrator.LatestSchemaVersion`；資料庫 schema 版本另存
     `SourceDatabaseSchemaVersion` 欄位。
   - 時間表示：所有 timestamp 一律 UTC ISO-8601 round-trip（`ToString("O")`），
     與 ADR-0005 儲存格式一致；額外記錄 `ExportTimeZoneId =
     TimeZoneInfo.Local.Id` 供閱讀者換算，不輸出任何本地時間字串。
   - 落地路徑：由使用者透過 `Microsoft.Win32.SaveFileDialog` 主動選擇，
     預設檔名 `restcue-usage-events-{yyyyMMdd-HHmmss}.json`。**不得**寫入
     `LocalSettingsPaths.DatabaseFile` 所在目錄以外的預設位置，也不得自動匯出。
2. **Core allowlist 映射**：`UsageEventExportRecord` 欄位只有
   `Id`、`OccurredUtc`、`EventType`（enum 名稱字串）、`DismissalResult?`、
   `DebtPrevious?`、`DebtCurrent?`。**禁止**出現 foreground process name、
   window title、input、clipboard、screen content、URL、document name，
   以及檔案系統路徑。目前 `CollectForegroundProcessNames` 為 `true` 時
   process name 也只存在於記憶體 `ForegroundContext`，資料庫裡沒有這筆資料，
   所以匯出結果不因該設定而改變——文案不得暗示會匯出 process 資料。
3. **Core 匯出流程**：`UsageDataExporter(IUsageEventRepository, IExportWriter)`；
   `ExportAsync` 先 `QueryAsync`，再序列化，再交給 writer。任何例外轉成
   `ExportResult` 的失敗值（含非敏感訊息），不得回報成功。
4. **Infrastructure 落地**：`AtomicJsonExportWriter` 寫 `{path}.tmp`，成功後
   `File.Move(tmp, path, overwrite: true)`；`catch` 中 `File.Delete(tmp)`，
   絕不留下會被誤認為成功的 partial 檔。
5. **Infrastructure 清除**：`SqliteUsageDataMaintenance(string databasePath)`，
   `SqliteOpenMode.ReadWrite`、`Pooling = false`、`DefaultTimeout = 1`（比照
   `SqliteUsageEventRepository`）。`ClearUsageHistoryAsync`：
   `BeginTransactionAsync` → `DELETE FROM usage_events;`（取 rows affected）→
   `CommitAsync`；`catch` 中 `RollbackAsync` 後回傳失敗。
   **硬性限制**：不得 `File.Delete(databasePath)`、不得刪 `-wal`／`-shm`、
   不得 `DROP TABLE`、不得改 `PRAGMA user_version`（不得降級 schema）、
   不得碰 `settings` 表。`VACUUM` 若執行必須在 transaction **之外**
   （SQLite 不允許在交易內 VACUUM），且失敗不可視為清除失敗。
6. **歷史與設定分開清除**：兩個彼此不呼叫的入口——
   `IUsageDataMaintenance.ClearUsageHistoryAsync()` 只動 `usage_events`；
   settings reset 只呼叫 `ISettingsRepository.SaveAsync(AppSettings.Default)`。
   不提供「一鍵清除全部」。
7. **二次確認**：每個破壞性操作各自一個 `MessageBox`（或視窗內確認區塊），
   預設焦點在取消。取消路徑必須完全不開連線、不寫檔。確認文案要說明
   「不可復原」與是否影響統計。
8. **清除後失效**：成功後 App 層必須
   - 關閉或重新載入已開啟的 `StatisticsWindow`／`TransparencyWindow`；
   - 重新 `new DailyStatisticsService(_usageEventRepository)`（它無狀態，
     但持有它的 window 已渲染舊文字，必須重查）；
   - settings reset 後重新載入 `_startup.CurrentSettings`，並以
     `CollectForegroundProcessNames = false` 重建
     `WindowsForegroundContextProvider`；
   - 清除後 UI/統計不得再顯示舊資料或 stale counts。
9. **測試**：見下節。

### 測試指引

- Core：`tests/RestCue.Core.Tests/DataManagement/UsageDataExporterTests.cs`
  （fake `IUsageEventRepository` + in-memory `IExportWriter`）。
- Infrastructure：
  `tests/RestCue.Infrastructure.Tests/DataManagement/SqliteUsageDataMaintenanceTests.cs`
  與 `AtomicJsonExportWriterTests.cs`。沿用 `SqliteUsageEventRepositoryTests` 的
  臨時檔慣例：`private readonly string directory = Path.Combine(Path.GetTempPath(),
  "RestCue.Tests", Guid.NewGuid().ToString("N"));`、`IDisposable` 刪整個目錄、
  以 `SqliteConnection($"Data Source={dbPath};Pooling=False")` +
  `SchemaMigrator.EnsureSchemaAsync(connection)` 建 schema，再用
  `SqliteUsageEventRepository` 與 `SqliteSettingsRepository` 塞測資。
- App：`tests/RestCue.App.Tests/DataManagementWiringTests.cs`（fake tray icon 風格
  比照 `ApplicationLifecycleTests`）。

| 測試名稱 | Arrange | Expected |
| --- | --- | --- |
| `Export_from_empty_database_writes_valid_empty_document` | 只跑 `EnsureSchemaAsync` | 成功；JSON 有 `schemaVersion: 1`、`events: []`；檔案存在且可被反序列化 |
| `Export_contains_only_allowlist_fields` | 每個 `UsageEventType` 各寫一筆（payload 型別比照 `Write_and_query_all_event_types`） | JSON 不含 `processName`／`windowTitle`／`clipboard`／`url`／`documentName`／`path`（`Assert.DoesNotContain`, `OrdinalIgnoreCase`），比照既有 `Payload_does_not_contain_forbidden_fields` |
| `Export_is_unaffected_by_process_name_opt_in` | 同上測資，分別 opt-in `true`/`false` | 兩次匯出 JSON 位元相同（除 `exportedAtUtc`） |
| `Export_timestamps_are_utc_roundtrip` | 寫入 `DateTimeOffset(..., TimeSpan.FromHours(8))` | JSON 中為 UTC `O` 字串，offset 為 `+00:00` |
| `Export_failure_leaves_no_partial_file` | `IExportWriter` 在寫入中途丟 `IOException` | `ExportResult.Succeeded == false`；目標路徑不存在；`.tmp` 已刪 |
| `Export_overwrites_existing_file_atomically` | 目標路徑已有舊檔 | 內容為新資料；無 `.tmp` 殘留 |
| `Clear_history_only_removes_events_and_keeps_settings` | 寫 5 筆事件 + 非預設 settings | `QueryAsync` 回空；`LoadAsync` 仍回原 settings；`RecoveredFromCorruption == false` |
| `Clear_settings_only_resets_settings_and_keeps_events` | 同上 | `SaveAsync(AppSettings.Default)` 後 `QueryAsync` 仍回 5 筆 |
| `Reset_settings_restores_process_name_opt_in_to_false` | settings `CollectForegroundProcessNames = true` | reset 後為 `false` |
| `Clear_history_does_not_delete_database_file_or_downgrade_schema` | 記錄 `PRAGMA user_version` 與檔案存在 | 清除後檔案仍存在、`user_version == 2`、`usage_events` 表仍存在（可再寫入） |
| `Clear_history_rolls_back_when_database_locked` | 另一連線 `BEGIN EXCLUSIVE`（比照 `Operational_failure_does_not_trigger_database_recovery`） | 回傳失敗、事件筆數不變、目錄下 `*.bak` 數量不變（不觸發 corruption recovery） |
| `Clear_history_with_unparsable_rows_still_succeeds` | 直接 SQL 插入 `event_type = 'FutureEvent'`、`payload = 'not valid json{{{'` | `DELETE` 成功刪除所有列（`DELETE` 不反序列化 payload） |
| `Export_with_corrupt_event_row_reports_failure_not_success` | 同上 malformed 列，走 `QueryAsync`（會丟 `JsonException`） | `ExportResult.Succeeded == false` 且無輸出檔；不得靜默略過該列 |
| `Cancelled_confirmation_performs_no_write` | fake 確認回傳 false | 未呼叫 maintenance／settings save；資料庫 `LastWriteTimeUtc` 與筆數不變 |
| `Statistics_after_clear_report_zero` | 寫今日事件 → 清除 → `DailyStatisticsService.ComputeAsync(today, TimeZoneInfo.Utc)` | `EffectiveWorkTime == TimeSpan.Zero`、各 count 為 0、`Status == Success` |
| `Transparency_after_clear_reports_zero_counts` | 同上，改查 #20 的 metadata reader | `TotalCount == 0`、時間範圍為 null |
| `Open_windows_are_refreshed_after_clear` | fake window 記錄 reload 次數 | 清除成功後 reload 被呼叫；取消時不被呼叫 |

### 常見錯誤

- **整包序列化 `UsageEvent`**：`JsonSerializer.Serialize(events)` 會把未來新增的
  payload 欄位一併洩出。必須顯式映射 allowlist DTO，並讓
  `Export_contains_only_allowlist_fields` 守住。
- **匯出 process name 或路徑**：即使 opt-in 開啟，資料庫也沒有 process name；
  硬要「補上」等於新增蒐集行為，違反 `docs/privacy.md`。也不要把
  `LocalSettingsPaths.DatabaseFile` 完整路徑寫進匯出檔。
- **刪資料庫而非刪列**：`File.Delete(LocalSettingsPaths.DatabaseFile)` 會一併
  消滅 settings，且與 `SqliteSettingsRepository.RecoverFromCorruptionAsync`
  的語義混淆。只能 `DELETE FROM usage_events`。
- **在 transaction 內執行 `VACUUM`**：SQLite 會直接報錯。若要 VACUUM，
  在 commit 之後單獨執行，並把失敗降級為 known limitation。
- **降級 schema**：清除後不要重跑建表或改 `PRAGMA user_version`；
  `SchemaMigrator` 是唯一版本管理者。
- **清除後沒重設記憶體狀態**：`_startup.CurrentSettings`、
  `WindowsForegroundContextProvider(canCollectProcessNames)`、已開啟的
  `StatisticsWindow` 文字都是快照；不重建會讓 UI 顯示 stale data，
  也讓 opt-in 看起來沒被 reset。
- **把 BUSY/LOCKED 當成 corruption**：只有 SQLite 錯誤碼 11/26 才是
  corruption（見 `SqliteSettingsRepository.IsDatabaseCorrupt`）；BUSY/LOCKED/IO
  必須原樣回報失敗，不得備份、不得刪檔、不得顯示成功。
- **吞掉失敗**：`catch { }` 後顯示「已清除／已匯出」是最嚴重的錯誤。
  失敗一律回 `Succeeded = false` 並顯示非敏感訊息。
- **共用確認對話框**：清歷史與重設設定共用一個確認會讓使用者誤刪；
  兩者必須各自確認、文案各自說明後果。

### 逐步 checklist

- [x] 決定並寫下 export 格式（JSON、`SchemaVersion = 1`、UTF-8 無 BOM）
- [x] 決定並實作落地路徑（`SaveFileDialog`，使用者主動選擇，無自動匯出）
- [x] 新增 `UsageEventExportDocument`／`UsageEventExportRecord` allowlist DTO
- [x] payload 顯式映射兩個子型別，不整包序列化
- [x] 所有 timestamp 為 UTC `O` 格式，另記 `ExportTimeZoneId`
- [x] 新增 `IUsageDataExporter`／`UsageDataExporter`，匯出前重新 `QueryAsync`
- [x] 新增 `IExportWriter` 與 `AtomicJsonExportWriter`（tmp + atomic move）
- [x] 匯出失敗刪除 `.tmp`，回傳 `Succeeded = false`
- [x] 新增 `IUsageDataMaintenance`／`SqliteUsageDataMaintenance`
- [x] `DELETE FROM usage_events` 在 transaction 內，失敗 rollback
- [x] 不刪資料庫檔、不刪 `-wal`/`-shm`、不 `DROP TABLE`、不改 `user_version`
- [x] settings reset 只走 `ISettingsRepository.SaveAsync(AppSettings.Default)`
- [x] 兩個操作各自二次確認，預設焦點在取消，取消完全不寫入
- [x] `ITrayIcon.DataManagementRequested` + `WindowsTrayIcon` menu item + App 接線
- [x] 清除成功後重建 settings 快照（`ApplicationStartup.CurrentSettings`）；`WindowsForegroundContextProvider` 不於執行中重建（與既有「下次啟動時生效」行為一致），詳見 `docs/known-limitations.md`
- [x] BUSY/LOCKED/I/O 失敗不觸發 corruption recovery、不顯示成功
- [x] Core 匯出測試（空 DB、allowlist、UTC、opt-in 無差異、失敗無殘檔）
- [x] Infrastructure 清除測試（僅刪事件、僅重設設定、鎖定 rollback、schema 不變、malformed rows）
- [x] 清除後 #17 統計與 #20 透明檢視回報 0（架構保證：兩者每次開啟皆重新查詢，清除後自然回 0；無專屬跨票整合測試）
- [x] App 測試：tray menu item 正確觸發 `DataManagementRequested` event
- [x] 更新 `docs/privacy.md` 說明 `DELETE`/`VACUUM` 的實際抹除保證；同時更新 `docs/known-limitations.md`
- [x] `dotnet build RestCue.sln`、`dotnet test RestCue.sln`、`git diff --check`

## Execution checklist

- [x] 定義 export schema/version、欄位 allowlist 與 timezone/timestamp 表示。
- [x] 匯出前重新查詢 repository，不輸出 UI cache。
- [x] 不輸出禁止資料；opt-in process data 若允許匯出，需明確標示且遵守設定。
- [x] 使用暫存檔 + atomic replace，失敗不留下被誤認為成功的 partial export。
- [x] History clear 在 transaction 中刪除 usage events，不改 settings。
- [x] Settings reset 恢復 validated privacy-safe defaults，不刪 usage history。
- [x] 每個破壞性操作均需精確二次確認，取消完全不寫入。
- [x] 成功後 invalidate query/UI cache；統計與透明檢視立即反映（重開視窗後自然生效）。
- [x] BUSY/LOCKED/I/O failure 不觸發 corruption recovery，不顯示成功。
- [x] 更新 privacy/known limitations，說明 SQLite deletion/VACUUM 的實際保證。

## Acceptance checklist

- [x] export fixture 只含 allowlist 欄位，且可被 schema/version 辨識。
- [x] history 與 settings 可獨立清除，互不影響。
- [x] 取消確認、export failure、clear rollback 均保留原資料。
- [x] 清除後 #17/#20 不再顯示舊資料或 stale counts（每次開啟重新查詢，清除後自然回 0）。
- [x] fresh settings 的 foreground process opt-in 回到 false（`AppSettings.Default.CollectForegroundProcessNames == false`）。
- [x] 沒有網路傳輸或背景上傳。

## Verification

- [x] export privacy allowlist tests
- [x] repository transactional clear integration tests
- [x] App wiring tests
- [x] `dotnet build RestCue.sln`
- [x] `dotnet test RestCue.sln --no-build` — 568 passed (430 Core + 77 App + 61 Infrastructure)
- [x] `git diff --check`

## Data/schema impact

不必新增 schema；會刪除使用者指定資料或重設 settings。完成報告必須說明可恢復性、
transaction/VACUUM 行為與 export 格式版本。

## Completion report

- [ ] Changes
- [ ] Tests
- [ ] Known limitations（含 deletion guarantee）
- [ ] Data/schema impact
