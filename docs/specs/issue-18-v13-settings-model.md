# Issue #18 — 擴充並驗證 v1.3 設定模型

## Goal

提供 v1.3 timing、retry cooldown、debt thresholds 與 Break Guide mode 的
預設值、持久化與跨欄位驗證；非法設定不得取代目前有效設定。

## Dependencies and governing rules

- Blocked by #12、#13、#14。
- 目前 settings 保存於 SQLite `settings` key/value table 的 `app_settings` JSON；
  非法文件依既有安全恢復策略處理，但 operational failure 不得刪庫。

## Required defaults and ranges

- Work interval 20m，10–60m。
- Natural pause 5s，2–30s。
- Max wait 3m，0–10m。
- Break duration 20s，10–60s。
- Snooze 5m，1–30m。
- Idle threshold 2m，1–10m。
- Passive Pause 20s，10–120s，且嚴格小於 Idle。
- Reminder display 30s，5–120s。
- Retry cooldown 20m，1–60m。
- Debt thresholds 20/35/45/60m，嚴格遞增，Level 1 = Work interval。
- Break Guide mode：Cue / Voice / NumberlessVisual；Spatial Audio 不在 MVP。

## Implementation guidance for agents

本節針對實作 Agent，補充「在既有已出貨程式碼上如何動手」。#11–#16 已關閉，
settings/validator/migration 已經存在且被測試覆蓋，請擴充它們，不要另建平行設計。

### 檔案地圖

| 路徑 | 動作 | 變更內容 |
| --- | --- | --- |
| `src/RestCue.Core/Settings/AppSettings.cs` | 修改 | 新增 `SchemaVersion`、`DebtLevel2Threshold`、`DebtLevel3Threshold`、`DebtLevel4Threshold`、`BreakGuideMode` init 屬性與 defaults |
| `src/RestCue.Core/Settings/AppSettingsValidator.cs` | 修改 | 新增 debt 遞增／Level 1 == `WorkInterval` 跨欄位規則與 `BreakGuideMode` enum 合法性檢查 |
| `src/RestCue.Core/Settings/BreakGuideMode.cs` | 新增 | `enum BreakGuideMode { Cue, Voice, NumberlessVisual }`；Spatial Audio 不加 |
| `src/RestCue.Core/Settings/ISettingsValidator.cs` | 不改 | 契約已足夠（`Validate(AppSettings)` → `IReadOnlyList<SettingsValidationError>`） |
| `src/RestCue.Core/Settings/ISettingsRepository.cs` | 不改 | `LoadAsync`/`SaveAsync` 已足夠 |
| `src/RestCue.Core/Settings/SettingsValidationError.cs` | 不改 | `(string Field, string Message)` 已可定位欄位 |
| `src/RestCue.Core/Policies/DebtPolicy.cs` | 不改 | `ValidateThresholds` 已存在；validator 只是把它的語義以錯誤清單形式表達 |
| `src/RestCue.Infrastructure/Settings/SqliteSettingsRepository.cs` | 修改 | 反序列化後補 settings-document 升級（缺欄位補 default、`SchemaVersion` 正規化），再走既有 `EnsureValid` |
| `src/RestCue.Infrastructure/Settings/SchemaMigrator.cs` | 通常不改 | SQLite table shape 不變則 `LatestSchemaVersion` 保持 2；只有真的動 table 才 bump |
| `src/RestCue.App/App.xaml.cs` | 修改 | `StartActivityTracking` 傳入新的 debt threshold 設定值，移除 App 端硬編 default |
| `src/RestCue.App/MainWindow.xaml.cs` | 修改 | `StartActivityTracking` 建 `WorkCycleTracker` 時傳 `settings.DebtLevel2/3/4Threshold` |
| `tests/RestCue.Core.Tests/Settings/AppSettingsValidatorTests.cs` | 修改 | 補所有新 range／跨欄位邊界案例 |
| `tests/RestCue.Infrastructure.Tests/Settings/SqliteSettingsRepositoryTests.cs` | 修改 | 補 round-trip、舊文件補 default、額外欄位、非法組合不覆蓋 |

### 可重用的既有型別

- 擴充 `AppSettings` record（`init` 屬性 + `AppSettings.Default` 靜態實例），
  不要新增 `TimingSettings`／`DebtSettings` 之類平行設定型別。
  `AppSettings.Default` 就是唯一 default 來源，其他層不得再寫 default 常數。
- 擴充 `AppSettingsValidator`，沿用私有 `AddRangeError(errors, value, min, max,
  field)` helper 加新 range；不要新建第二個 validator 類別。
- 錯誤回報沿用 `SettingsValidationError` + `SettingsValidationException`，
  UI 只讀 `Field`，不解析字串。
- 持久化沿用 `SqliteSettingsRepository`（`settings` 表、`app_settings` key、
  `JsonSerializerDefaults.Web` camelCase）；不要新增 JSON 檔或第二個 repository。
- 若真的需要動 SQLite table shape，一律走 `SchemaMigrator.EnsureSchemaAsync`
  並在既有 transaction／`UnsupportedSettingsSchemaException` 機制內加分支；
  不要在 repository 裡自行下 DDL 或自行讀寫 `PRAGMA user_version`。
- Debt 遞增規則沿用 `DebtPolicy.ValidateThresholds`（已在 `WorkCycleTracker`
  建構時呼叫）；validator 不要重寫比較邏輯，只需將違規轉成錯誤清單。
- `RestDebtLevel`、`PresentationIntensity`、`ApplicationRule`／
  `DefaultApplicationRules` 皆已存在，本票不改它們。
- 注意：`WorkCycleTracker` 目前用選擇性參數 `debtLevel2/3/4 = default` 搭配
  私有 `DefaultDebtLevel2/3/4`（35/45/60m）作為 fallback。本票把 default 的
  單一來源移到 `AppSettings`，呼叫端一律顯式傳值，避免重複 hard-coded default。

### 實作順序

1. **Core 模型**：新增 `BreakGuideMode` enum；在 `AppSettings` 加
   `public int SchemaVersion { get; init; } = 2;`、
   `public TimeSpan DebtLevel2Threshold { get; init; } = TimeSpan.FromMinutes(35);`
   （同理 `DebtLevel3Threshold` 45m、`DebtLevel4Threshold` 60m）、
   `public BreakGuideMode BreakGuideMode { get; init; } = BreakGuideMode.Cue;`。
   既有欄位名不得改（`PassiveBreakThreshold` 即 spec 的 Passive Pause，
   `NaturalPauseThreshold` 即自然停頓），改名會破壞既有 JSON 文件。
2. **Core validator**：在 `AppSettingsValidator.Validate` 內依序加：
   - 單欄位 range（用 `AddRangeError`）：`DebtLevel2Threshold`、
     `DebtLevel3Threshold`、`DebtLevel4Threshold` 各自 10–240m 之類明確界線；
     其餘欄位 range 已存在，只需補齊測試。
   - 跨欄位規則一（已存在，勿重寫）：
     `PassiveBreakThreshold >= IdleThreshold` → 錯誤欄位
     `nameof(settings.PassiveBreakThreshold)`；相等也必須拒絕。
   - 跨欄位規則二（新增）：debt 嚴格遞增。
     `WorkInterval >= DebtLevel2Threshold` → 錯誤欄位 `DebtLevel2Threshold`；
     `DebtLevel2Threshold >= DebtLevel3Threshold` → `DebtLevel3Threshold`；
     `DebtLevel3Threshold >= DebtLevel4Threshold` → `DebtLevel4Threshold`。
     相等一律拒絕，不得 clamp。
   - 跨欄位規則三（新增）：Level 1 恆等於工作提醒間隔。模型上不存放
     `DebtLevel1Threshold`，而是以 `WorkInterval` 作為 Level 1，
     所以此規則實作為「不允許獨立 Level 1 欄位」＋在 App/Infrastructure
     一律以 `settings.WorkInterval` 當 `level1` 傳給 `DebtPolicy`。
     若選擇仍保留顯式欄位，則必須加
     `DebtLevel1Threshold != WorkInterval` → 錯誤欄位 `DebtLevel1Threshold`。
   - `BreakGuideMode` 非定義值（`!Enum.IsDefined`）→ 錯誤欄位
     `nameof(settings.BreakGuideMode)`，不要靜默 fallback 成 `Cue`。
   - 一次 `Validate` 要回報全部違規，不得 early return。
3. **Infrastructure 持久化**：`SqliteSettingsRepository.LoadAsync` 目前是
   `Deserialize<AppSettings>` → `EnsureValid`。在兩者之間插入一個
   `private static AppSettings UpgradeDocument(AppSettings settings)`：把
   `SchemaVersion == 0`（舊文件沒有此欄位）視為 v1，回填
   `SchemaVersion = 2`，其餘缺欄位已由 record 的 init default 自動補上。
   未知 JSON 欄位由 `System.Text.Json` 預設忽略，不需額外處理。
   `SaveAsync` 已先 `EnsureValid` 再寫入，順序不得調換——這是「非法設定不得
   寫入」的實作點。`RecoverSettingsOnlyAsync` 寫入 `AppSettings.Default`，
   自然保持 `CollectForegroundProcessNames == false`，不要改動。
4. **SQLite schema**：若 table shape 未變，`SchemaMigrator.LatestSchemaVersion`
   保持 `2`，只在 completion report 說明「文件版本升級、SQLite schema 不變」。
   若確實需要 bump 到 3，必須在既有
   `BeginTransactionAsync` 區塊內加 `else if (version == 2)` 分支，並讓
   `SetUserVersionAsync` 留在 `CommitAsync` 之前的同一 transaction 內。
5. **App 佈線**：`MainWindow.StartActivityTracking` 建構 `WorkCycleTracker`
   時，於既有 `settings.RetryCooldown` 之後補上
   `settings.DebtLevel2Threshold, settings.DebtLevel3Threshold,
   settings.DebtLevel4Threshold`。`App.OnStartup` 已透過
   `ApplicationStartup.CurrentSettings` 取得設定，不要在 App 層再寫任何
   timing 常數或 `TimeSpan.From*` 預設值。
6. **測試**：先寫 Core validator 測試，再寫 Infrastructure round-trip／升級
   測試，最後跑 `dotnet build RestCue.sln` 與 `dotnet test RestCue.sln`。

### 測試指引

Core 驗證測試放 `tests/RestCue.Core.Tests/Settings/AppSettingsValidatorTests.cs`；
持久化測試放 `tests/RestCue.Infrastructure.Tests/Settings/SqliteSettingsRepositoryTests.cs`；
schema 測試放同目錄 `SchemaMigratorTests.cs`。沿用既有風格：xunit `[Fact]`、
snake_case 方法名、`AppSettings.Default with { ... }` 建 arrange 資料、
`Assert.Contains(errors, e => e.Field == "X")` 定位欄位。

| 測試名稱 | Arrange | Expected |
| --- | --- | --- |
| `Defaults_are_valid_and_debt_thresholds_are_20_35_45_60` | `AppSettings.Default` | `Validate` 空清單；`WorkInterval == 20m`、L2/3/4 == 35/45/60m |
| `Passive_pause_equal_to_idle_threshold_is_invalid` | `PassiveBreakThreshold = 1m`、`IdleThreshold = 1m` | 錯誤含 `PassiveBreakThreshold`（此案例已存在，勿刪） |
| `Passive_pause_one_second_below_idle_threshold_is_valid` | `PassiveBreakThreshold = 59s`、`IdleThreshold = 1m` | 無 `PassiveBreakThreshold` 錯誤 |
| `Debt_level2_equal_to_work_interval_is_invalid` | `WorkInterval = 20m`、`DebtLevel2Threshold = 20m` | 錯誤含 `DebtLevel2Threshold` |
| `Debt_level3_equal_to_level2_is_invalid` | L2 = 35m、L3 = 35m | 錯誤含 `DebtLevel3Threshold` |
| `Debt_level4_below_level3_is_invalid` | L3 = 45m、L4 = 44m | 錯誤含 `DebtLevel4Threshold` |
| `Strictly_increasing_debt_thresholds_are_valid` | 20/21/22/23m | 無 debt 相關錯誤 |
| `Work_interval_below_minimum_is_invalid` | `WorkInterval = 9m` | 錯誤含 `WorkInterval` |
| `Work_interval_at_minimum_and_maximum_are_valid` | 10m 與 60m 兩案 | 無 `WorkInterval` 錯誤（60m 時需同步調高 L2/3/4 以免觸發遞增規則） |
| `Maximum_reminder_wait_zero_is_valid` | `MaximumReminderWait = TimeSpan.Zero` | 無 `MaximumReminderWait` 錯誤（下界為 0） |
| `Unknown_break_guide_mode_is_invalid` | `BreakGuideMode = (BreakGuideMode)99` | 錯誤含 `BreakGuideMode` |
| `Multiple_violations_are_all_reported` | 同時給非法 `WorkInterval` 與非法 debt 組合 | `errors.Count >= 2`，兩個欄位都出現 |
| `Debt_thresholds_and_break_guide_mode_round_trip` | `SaveAsync` 後新建 repository `LoadAsync` | 值與 `BreakGuideMode` 完全相同 |
| `Older_document_without_debt_thresholds_loads_v13_defaults` | 直接 `UPDATE settings SET value = '{"workInterval":"00:15:00"}'` | L2/3/4 == 35/45/60m、`WorkInterval == 15m`、`CollectForegroundProcessNames == false` |
| `Unknown_extra_json_field_is_ignored` | 文件多一個 `"futureField": 1` | 載入成功、`RecoveredFromCorruption == false` |
| `Invalid_debt_combination_is_rejected_without_replacing_saved_settings` | 先存合法設定，再 `SaveAsync` 非遞增 debt | 丟 `SettingsValidationException`；後續 `LoadAsync` 仍為先前合法值 |
| `Document_version_upgrade_does_not_change_sqlite_user_version` | 舊文件載入後查 `PRAGMA user_version` | 仍為 `SchemaMigrator.LatestSchemaVersion` |

Fake 慣例：

- `IClock`：本票的 validator/repository 都不需要時鐘；若測試要驗證
  tracker 端 debt 行為，沿用測試檔內私有
  `sealed class FakeClock : IClock { public DateTimeOffset UtcNow => _utcNow;
  public void Advance(TimeSpan d) => _utcNow += d; }`（見
  `tests/RestCue.App.Tests/ModeEntrySeamTests.cs`），不要新增共用測試專案。
- `ISettingsRepository`：App 層測試沿用
  `tests/RestCue.App.Tests/ApplicationStartupTests.cs` 內的私有
  `FakeSettingsRepository(AppSettings settings)`，`LoadAsync` 回傳
  `new SettingsLoadResult(settings)`、`SaveAsync` 回 `Task.CompletedTask`。
- Infrastructure 測試使用真 SQLite，路徑用
  `Path.Combine(Path.GetTempPath(), "RestCue.Tests", Guid.NewGuid().ToString("N"))`
  並在 `Dispose` 遞迴刪除，與既有測試一致。

### 常見錯誤

- 把非法值 clamp 進合法區間而不回報錯誤。本票要求「拒絕」：validator 一律
  產生 `SettingsValidationError`，`SaveAsync` 一律丟
  `SettingsValidationException`，絕不修正使用者輸入。
- 只回報第一個錯誤就 return，導致 UI 無法一次顯示所有問題。
- 在 `SaveAsync` 裡先寫入再驗證，或分成多次寫入造成 partial write。
  `settings` 表只有一列 `app_settings`，必須一次 upsert 完整文件。
- 為新增欄位自行在 App 或 Infrastructure 再寫一份 default（例如再寫一次
  35/45/60m），造成 default 兩處分歧。唯一來源是 `AppSettings.Default`。
- 改既有欄位名稱（如把 `PassiveBreakThreshold` 改成 `PassivePauseThreshold`）
  會讓舊 JSON 文件的值靜默變回 default，等於資料遺失。
- 若真要 bump SQLite schema，在 transaction commit 之前之外設定
  `PRAGMA user_version`，或在 commit 後才設定；兩者都會在中途失敗時留下
  版本與實際 table 不一致的資料庫。必須維持既有「DDL + SetUserVersion 同一
  transaction，最後 Commit」順序。
- 把 `UnsupportedSettingsSchemaException`（未來版本）誤當成 corruption 走
  recovery，導致把新版設定覆蓋成 defaults。它必須繼續往外拋。
- 讓 `BreakGuideMode` 未知值靜默 fallback，違反「非法或未知 mode 不覆蓋目前
  資料」。
- 忘了 `CollectForegroundProcessNames` 在升級與 recovery 後仍必須為 `false`。

### 逐步 checklist

- [x] 新增 `src/RestCue.Core/Settings/BreakGuideMode.cs`（Cue/Voice/NumberlessVisual）。
- [x] `AppSettings` 加 `SchemaVersion`、`DebtLevel2/3/4Threshold`、`BreakGuideMode`
      並確認 `AppSettings.Default` 為 20/35/45/60m 與 `Cue`。
- [x] `AppSettingsValidator` 加 debt threshold 單欄位 range（用 `AddRangeError`）。
- [x] `AppSettingsValidator` 加 debt 嚴格遞增規則，錯誤欄位指向較大的那一級。
- [x] `AppSettingsValidator` 以 `WorkInterval` 作為 Level 1，並確保無獨立 Level 1
      欄位可與之不一致。
- [x] `AppSettingsValidator` 加 `Enum.IsDefined` 檢查 `BreakGuideMode`。
- [x] 確認 `Validate` 收集全部錯誤，無 early return。
- [x] `SqliteSettingsRepository.LoadAsync` 加 `UpgradeDocument`，位置在
      `Deserialize` 之後、`EnsureValid` 之前。
- [x] 確認 `SaveAsync` 仍是「先 `EnsureValid` 再單次 upsert」。
- [x] 決定並記錄 `SchemaMigrator.LatestSchemaVersion` 不需要 bump（SQLite table 不變）。
- [x] `MainWindow.StartActivityTracking` 傳入 `settings.DebtLevel2/3/4Threshold`。
- [x] 移除 App／Core 中重複的 timing default 常數。
- [x] 在 `AppSettingsValidatorTests.cs` 補上表中所有 Core 案例。
- [x] 在 `SqliteSettingsRepositoryTests.cs` 補上 round-trip／舊文件／額外欄位／
      非法組合不覆蓋案例。
- [x] 無 schema bump，無需 `SchemaMigratorTests` 更新。
- [x] `dotnet build RestCue.sln`、`dotnet test RestCue.sln --no-build`、
      `git diff --check` 全綠。

## Execution checklist

- [x] 將所有可調 timing 值放入 Core settings，不留在 WPF event handler。
- [x] 使用具名型別或清楚欄位，避免同單位數值互相傳錯。
- [x] validator 回傳可定位欄位／跨欄位的錯誤，不依賴 UI 字串。
- [x] 驗證每個單欄位範圍與所有跨欄位不變量。
- [x] 更新 settings document version/serialization，舊文件安全補入新 defaults。
- [x] 非法或未知 mode 不覆蓋目前資料；依 ADR-0001 安全恢復。
- [x] Foreground process collection 預設仍為 false。
- [x] round-trip、舊版文件、缺欄位、額外欄位、非法組合均有 integration test。

## Acceptance checklist

- [x] `PassivePauseThreshold < IdleThreshold`，相等也拒絕。
- [x] debt thresholds 嚴格遞增，Level 1 精確等於 Work interval。
- [x] 本 spec 列出的每個 range 邊界前／等於／後都有測試。
- [x] 非法設定不會寫入或替換已保存的有效設定。
- [x] v1 設定升級後使用 v1.3 defaults 且隱私 opt-in 維持關閉。
- [x] Core/App 不出現重複 hard-coded timing defaults。

## Verification

- [x] Core validator tests (380 pass)
- [x] Infrastructure settings migration/round-trip tests (45 pass)
- [x] `dotnet build RestCue.sln`
- [x] `dotnet test RestCue.sln --no-build`
- [x] `git diff --check`

## Data/schema impact

Settings document version/shape changed: new `SchemaVersion`, `DebtLevel2/3/4Threshold`,
`BreakGuideMode` fields. SQLite table shape unchanged (still `settings` key/value, schema
version remains 2). Old documents without these fields load v1.3 defaults via in-memory
`UpgradeDocument` (sets `SchemaVersion = 2`). Forward compatibility: unknown JSON fields
are ignored by `System.Text.Json`. Backward compatibility: newly saved documents omit no
required fields for existing readers.

## Completion report

- [x] Changes
- [x] Tests
- [x] Known limitations
- [x] Data/schema impact
