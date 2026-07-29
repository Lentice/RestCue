# Issue #20 — 提供資料透明檢視

## Goal

讓使用者從系統列兩次點擊內查看「目前實際知道什麼」：已啟用的資料類型、
本機事件筆數與時間範圍。檢視本身必須完全唯讀。

## Dependencies and governing rules

- Blocked by #16、#19。
- 使用者從 tray menu 開始最多兩次明確 click 可開啟此頁。
- 至少列出：最後輸入「經過時間」是否用於 activity 判定、usage event 類型與筆數、
  settings 是否保存、foreground process-name collection 是否啟用。
- 明確列出永不收集：window title、input、clipboard、screen content、URL、
  document name。

## Scope

- 從目前 settings 與實際 repository metadata 動態產生內容。
- 顯示資料類型、是否啟用、筆數與最早／最新日期（若存在）。
- Tray menu 提供兩次點擊內可達入口。

## Out of scope

- 顯示原始敏感值、window title、process-name 清單、事件逐筆瀏覽。
- 開啟追蹤、寫 audit event、紅點、未讀數或主動提示。
- 匯出與清除（#21）。

## Implementation guidance for agents

本節是給實作 Agent 的具體施工說明。不要新增本節未列出的檔案或型別。

### 檔案地圖

| 路徑 | 動作 | 內容 |
| --- | --- | --- |
| `src/RestCue.Core/Transparency/DataTransparencySnapshot.cs` | 新增 | `record` 們：`DataTransparencySnapshot`、`DataCategoryStatus`、`UsageEventTypeCount`、`enum CollectionState { NeverCollected, DisabledByUser, EnabledEmpty, EnabledWithData, Unavailable }` |
| `src/RestCue.Core/Transparency/IDataTransparencyService.cs` | 新增 | `Task<DataTransparencySnapshot> GetSnapshotAsync(CancellationToken ct = default)` |
| `src/RestCue.Core/Transparency/DataTransparencyService.cs` | 新增 | 唯讀組裝：吃 `ISettingsRepository`＋`IUsageEventMetadataReader`，列舉 `Enum.GetValues<UsageEventType>()` 產生類型清單 |
| `src/RestCue.Core/UsageEvents/IUsageEventMetadataReader.cs` | 新增 | 唯讀 metadata 介面：`Task<UsageEventMetadata> ReadMetadataAsync(CancellationToken ct = default)`，回傳 `TotalCount`、`EarliestUtc`、`LatestUtc`、`PerTypeCounts`、`UnparsableRowCount` |
| `src/RestCue.Infrastructure/UsageEvents/SqliteUsageEventMetadataReader.cs` | 新增 | 以 `SqliteOpenMode.ReadOnly` 開連線，`COUNT/MIN/MAX` 與 `GROUP BY event_type` 聚合；不呼叫 `SchemaMigrator.EnsureSchemaAsync` |
| `src/RestCue.App/TransparencyWindow.xaml` / `.xaml.cs` | 新增 | 唯讀視窗，樣式沿用 `StatisticsWindow.xaml`；`ShowInTaskbar` 與焦點行為必須不搶焦點 |
| `src/RestCue.App/Lifecycle/ITrayIcon.cs` | 修改 | 新增 `event EventHandler? DataTransparencyRequested;` |
| `src/RestCue.App/Lifecycle/WindowsTrayIcon.cs` | 修改 | 在 `ContextMenuStrip` 加一個「資料透明檢視」`MenuItem`（與「今日統計」同層，不放子選單） |
| `src/RestCue.App/App.xaml.cs` | 修改 | 新增 `internal void WireDataTransparencyCommand(ITrayIcon trayIcon)`，沿用 `WireStatisticsCommand` 的寫法 |
| `docs/privacy.md` | 條件修改 | 只有當檢視文案與此文件語義不一致時才修，且同票修 |

### 可重用的既有型別

- `UsageEventType`（`src/RestCue.Core/UsageEvents/UsageEventType.cs`）是資料類型
  的唯一真實來源，共 17 個值；一律以 `Enum.GetValues<UsageEventType>()` 列舉。
- `AppSettings.CollectForegroundProcessNames`（`src/RestCue.Core/Settings/AppSettings.cs`）
  是 foreground process-name opt-in 的唯一來源；透過 `ISettingsRepository.LoadAsync`
  取得，不要自己讀 `settings` 表。
- `SchemaMigrator.LatestSchemaVersion`（值為 2）與 `PRAGMA user_version` 用來顯示
  schema 版本；`SchemaMigrator.GetUserVersionAsync` 是 `internal`，不要改其可見度，
  在 metadata reader 內自行 `PRAGMA user_version;` 查詢。
- `LocalSettingsPaths.DatabaseFile` 是資料庫位置，UI 顯示路徑時用它。
- `DailyStatisticsService`（in-flight #17）已負責一切事件聚合語義；#20 只顯示
  「筆數／時間範圍／是否啟用」，**不得**引入第二條聚合路徑，也不得呼叫
  `IDailyStatisticsService`。

不要建立：`IUsageEventRepository.QueryAsync` 的替代查詢層、任何 derived-truth 表
（例如 `transparency_state`、`last_viewed_at`）、任何 `WriteAsync` 呼叫。

### 實作順序

1. **Core 抽象**：新增 `IUsageEventMetadataReader` 與 `UsageEventMetadata`。
   欄位：`long TotalCount`、`DateTimeOffset? EarliestUtc`、`DateTimeOffset? LatestUtc`、
   `IReadOnlyDictionary<UsageEventType, long> PerTypeCounts`、`long UnparsableRowCount`、
   `long SchemaVersion`。
2. **Core 服務**：`DataTransparencyService(ISettingsRepository, IUsageEventMetadataReader)`
   實作 `GetSnapshotAsync`。內容必須由程式列舉產生，不得硬編字串清單：
   - usage event 類型逐項來自 `Enum.GetValues<UsageEventType>()`，缺筆數者填 0。
   - 「最後輸入經過時間用於 activity 判定」為 `CollectionState.EnabledWithData`
     的固定事實（`WindowsUserActivityMonitor` 只讀 idle 時間，不記錄內容）。
   - foreground process name：`CollectForegroundProcessNames == false` →
     `DisabledByUser`；為 `true` 時目前仍為 `NeverCollected`（見下方「常見錯誤」）。
   - 永不收集清單以一個 `static readonly string[] NeverCollected` 常數表達
     （window title、input、clipboard、screen content、URL、document name），
     文案必須與 `docs/privacy.md` 一字對應。
   - metadata reader 丟例外時回 `CollectionState.Unavailable` 加上非敏感訊息，
     不要往上拋（比照 `DailyStatisticsService.Failure` 的作法）。
3. **Infrastructure 實作**：`SqliteUsageEventMetadataReader(string databasePath)`，
   connection string 用 `SqliteConnectionStringBuilder { Mode = SqliteOpenMode.ReadOnly,
   Pooling = false, DefaultTimeout = 1 }`。**ReadOnly 是硬需求**：它在型別層面保證
   「開啟檢視不得寫入任何 usage event 或設定」。`occurred_utc` 是 ISO-8601 `O` 字串，
   `MIN`/`MAX` 的字典序即時間序（ADR-0005），讀回後 `DateTimeOffset.Parse(...,
   DateTimeStyles.RoundtripKind).ToUniversalTime()`。無法解析的列計入
   `UnparsableRowCount` 而不是丟例外。
4. **App wiring**：`ITrayIcon.DataTransparencyRequested` →
   `App.WireDataTransparencyCommand` → `new TransparencyWindow(service).Show()`。
   路徑必須是 tray 右鍵（第一次 click）→ menu item（第二次 click）→ 視窗，
   **兩次點擊內可達**；不得再加子選單或中繼確認頁。
5. **UI 呈現**：`TransparencyWindow` 在 `OnSourceInitialized` 中 `await
   GetSnapshotAsync()` 並填 `TextBlock`。清楚區分 `DisabledByUser`（顯示「未收集」）
   與 `EnabledEmpty`（顯示「已啟用，目前 0 筆」）。**不得使用紅點、badge、未讀數、
   閃爍或任何主動提示**；不得呼叫 `Activate()`／`Focus()`／`Topmost = true`。
6. **測試**：見下節。

### 測試指引

- Core：新增 `tests/RestCue.Core.Tests/Transparency/DataTransparencyServiceTests.cs`，
  用手寫 fake `IUsageEventMetadataReader` 與 fake `ISettingsRepository`（比照
  `tests/RestCue.Core.Tests/UsageEvents/DailyStatisticsServiceTests.cs` 的 fake 風格）。
- Infrastructure：新增
  `tests/RestCue.Infrastructure.Tests/UsageEvents/SqliteUsageEventMetadataReaderTests.cs`。
  沿用 `SqliteUsageEventRepositoryTests` 的臨時檔慣例：class 內
  `private readonly string directory = Path.Combine(Path.GetTempPath(),
  "RestCue.Tests", Guid.NewGuid().ToString("N"));`，實作 `IDisposable` 刪掉整個目錄，
  以 `SqliteConnection($"Data Source={dbPath};Pooling=False")` +
  `SchemaMigrator.EnsureSchemaAsync` 建 schema，再用
  `SqliteUsageEventRepository` 塞測資。
- App：新增 `tests/RestCue.App.Tests/DataTransparencyWiringTests.cs`，用既有
  `ApplicationLifecycleTests` 裡的 fake tray icon 風格驗證事件接線。

| 測試名稱 | Arrange | Expected |
| --- | --- | --- |
| `Empty_database_reports_zero_counts_and_no_range` | 只跑 `EnsureSchemaAsync`，不寫事件 | `TotalCount == 0`、`Earliest/LatestUtc` 為 null、每個 `UsageEventType` 都出現且為 0 |
| `All_event_types_are_listed_from_enum` | 空 DB | 快照類型數 == `Enum.GetValues<UsageEventType>().Length` |
| `Counts_and_range_match_written_events` | 寫 3 筆不同時間事件 | `TotalCount == 3`、`EarliestUtc`/`LatestUtc` 等於最早／最新 UTC |
| `Unparsable_row_is_counted_not_thrown` | 直接 SQL 插入 `occurred_utc = 'not-a-date'` 一列 | 不丟例外；`UnparsableRowCount == 1`；其餘筆數仍正確 |
| `Malformed_payload_row_does_not_break_metadata` | 直接 SQL 插入 `payload = 'not valid json{{{'` | metadata 成功（metadata 不反序列化 payload）；筆數含該列 |
| `Unknown_event_type_string_is_counted_as_unparsable` | 直接 SQL 插入 `event_type = 'FutureEvent'` | 不丟例外，計入 `UnparsableRowCount` |
| `Opt_in_off_reports_disabled_not_zero` | settings `CollectForegroundProcessNames = false` | process-name 類別為 `DisabledByUser`，不是 `EnabledEmpty` |
| `Opt_in_on_with_empty_data_is_distinguishable` | opt-in `true`、無資料 | 狀態不等於 `DisabledByUser` |
| `Repository_unavailable_yields_Unavailable_state` | fake reader 丟 `SqliteException` | 快照 `Unavailable` + 非敏感訊息；不丟例外 |
| `Opening_snapshot_writes_nothing` | 建 DB、寫 2 筆事件、記錄 `PRAGMA user_version`、`COUNT(*)`、`settings` 列的 `updated_at_utc` 與檔案 `Length`／`LastWriteTimeUtc`；連續呼叫 `GetSnapshotAsync` 三次 | 上述值全部不變；目錄下沒有新增 `-wal`／`-shm`／`.bak` 檔 |
| `ReadOnly_mode_rejects_insert` | 用 metadata reader 的 connection string 直接執行 `INSERT` | 丟 `SqliteException`（證明連線真的是唯讀） |
| `Tray_menu_item_raises_request_once_per_click` | fake tray icon | 事件恰好觸發一次，且不呼叫任何 `IUsageEventRepository.WriteAsync` |

### 常見錯誤

- **硬編資料類型清單**：在 XAML 或字串陣列寫死 17 種事件名稱，之後
  `UsageEventType` 新增值就漂移。一定要 `Enum.GetValues<UsageEventType>()`。
- **重用 `SqliteUsageEventRepository`**：它是 `SqliteOpenMode.ReadWrite`，
  且 `QueryAsync` 會反序列化 payload，一遇 malformed 列就整批丟 `JsonException`
  （見 `Single_malformed_event_does_not_corrupt_database`）。透明檢視必須用
  獨立的 ReadOnly metadata reader。
- **順手呼叫 `SchemaMigrator.EnsureSchemaAsync`**：那會建表並寫
  `PRAGMA user_version`，直接違反「開啟不得寫入」。schema 由啟動流程
  （`SqliteSettingsRepository.LoadAsync`）保證存在。
- **透過 `ISettingsRepository.LoadAsync` 觸發寫入**：settings 文件損毀時它會
  upsert 預設值（`RecoverSettingsOnlyAsync`）。測試必須用乾淨 fixture，並在
  `RecoveredFromCorruption == true` 時把該類別標為 `Unavailable`，不要當成正常值。
- **顯示原始值**：不要顯示 process name 清單、逐筆事件、payload 內容或
  `.bak` 備份檔名。只顯示彙總數字與布林狀態。
- **紅點／未讀提示**：不得加 badge、`Balloon tip`、`SetSuppressedState` 之類的
  注意力機制；本檢視是被動的。
- **重算統計**：不要在此頁重算工作時間或休息次數；那是 #17
  `DailyStatisticsService` 的責任，重算會產生兩套可能不一致的數字。
- **吞掉失敗**：`catch { }` 之後顯示 0 筆，會讓使用者以為沒有資料。失敗必須
  以 `Unavailable` 明確呈現。

### 逐步 checklist

- [x] 新增 `UsageEventMetadata` 與 `IUsageEventMetadataReader`（Core）
- [x] 新增 `DataTransparencySnapshot`、`DataCategoryStatus`、`CollectionState`
- [x] 新增 `IDataTransparencyService` 與 `DataTransparencyService`
- [x] 類型清單由 `Enum.GetValues<UsageEventType>()` 產生，無硬編字串
- [x] 永不收集清單文案與 `docs/privacy.md` 逐項對應
- [x] 新增 `SqliteUsageEventMetadataReader`，`SqliteOpenMode.ReadOnly`
- [x] metadata reader 不呼叫 `EnsureSchemaAsync`、不執行任何 `INSERT`/`PRAGMA` 寫入
- [x] 無法解析的列計入 `UnparsableRowCount` 而非丟例外
- [x] `ITrayIcon.DataTransparencyRequested` 與 `WindowsTrayIcon` menu item
- [x] `App.WireDataTransparencyCommand` 接線並在 `WireTrayCommands` 呼叫
- [x] `TransparencyWindow` 唯讀顯示，不搶焦點、無紅點／badge
- [x] 區分 `DisabledByUser` 與 `EnabledEmpty` 兩種文案
- [x] `Unavailable` 時顯示非敏感、可診斷的局部錯誤
- [x] Core 測試（空 DB、enum 完整性、opt-in on/off、unavailable）
- [x] Infrastructure 測試（筆數／範圍、unparsable 列、ReadOnly 拒絕 INSERT）
- [x] `Opening_snapshot_writes_nothing` 斷言檔案與資料庫內容不變
- [x] App 測試驗證兩次點擊路徑且不寫入 usage event
- [x] `dotnet build RestCue.sln` 與 `dotnet test RestCue.sln` 通過（420 Core + 76 App + 53 Infrastructure = 549 total, 0 failed）

## Execution checklist

- [x] 建立 readonly transparency query，不共用會寫入 last-viewed 的 UI service。
- [x] 資料類型由實際 schema/repository + settings 決定，不維護易漂移的硬編碼宣稱。
- [x] opt-in 關閉時清楚區分「未收集」與「目前 0 筆」。
- [x] repository unavailable 時顯示安全、非敏感且可診斷的局部錯誤。
- [x] Tray → menu item → view 路徑不超過兩次 click。
- [x] 開啟、刷新、關閉不建立 usage event、不改 settings、不更新資料庫。
- [x] 內容與 `docs/privacy.md` 一致；差異須在同票修正文件。
- [x] UI 不使用紅點、badge 或監控感語言。

## Acceptance checklist

- [x] 對每種 settings/database fixture，顯示內容與實際狀態一致。
- [x] 開啟前後資料庫 byte/logical content 與 event counts 不變。
- [x] opt-in off/on 與空／非空資料庫有測試。
- [x] 兩次點擊內可達且不搶走使用者目前工作焦點；只有明確點擊才開啟。
- [x] 不暴露禁止資料或原始 process records。

## Verification

- [x] Core transparency service tests (9 tests, all pass)
- [x] App navigation/view-model tests (1 test, pass)
- [x] Infrastructure readonly integration tests (8 tests, all pass)
- [x] 手動兩次點擊與焦點 smoke test
- [x] `dotnet build RestCue.sln`
- [x] `dotnet test RestCue.sln --no-build` (549 pass)

## Data/schema impact

無；此功能必須唯讀。

## Completion report

- [x] Changes
- [x] Tests（9 Core + 8 Infrastructure + 1 App = 18 new, 0 failed）
- [x] Known limitations
- [x] Data/schema impact（無）
