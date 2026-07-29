# Issue #22 — 自動化驗證核心效能與隱私界線

## Goal

建立可重現、自動化的長時間／資源、狀態轉移、資料與日誌隱私驗證，產出清楚
pass/fail 與量測 artifacts，而不是只留下人工觀察。

## Dependencies and governing rules

- Blocked by #10、#15、#17、#20、#21。
- 背景閒置 CPU 平均目標低於 0.5%，記憶體目標低於 150 MB。
- activity polling 建議每秒一次；不得每秒寫入 SQLite。
- tray 平時只能事件驅動切換靜態 icon；必要淡入淡出不得超過 10 FPS，完成後停止渲染。
- release candidate 必須可在 Windows 10/11 穩定常駐 8 小時，且無 Critical/High
  未決缺陷。

## Scope

- 對 one-second activity polling、event-driven tray updates、SQLite writes、
  CPU/memory/handle growth 建立 harness。
- 對完整 v1.3 state transitions 建立 fake-clock scenario tests。
- 對 logs、database、export artifacts 建立禁止資料掃描。
- 可在 CI 執行的短版與 Windows 長時間本機版。

## Success thresholds

CPU 與 memory 使用上列門檻。Handle/thread/DB growth 沒有固定數值，須先量
baseline，在 test plan 記錄 hardware/OS/build/configuration，並以 8 小時內無
持續單調且無界成長作為最低判定；若觀察到成長，必須建立 issue，不可自行放寬。

## Implementation guidance for agents

本節是給實作 Agent 的補充執行指引，不取代下方 Execution/Acceptance checklist。
所有路徑以 repository root 為基準。標記「新增」的檔案目前不存在，必須建立。

### 檔案地圖

新增測試專案（第 4 個測試專案，與現有三個並列）：

- `tests/RestCue.Validation.Tests/RestCue.Validation.Tests.csproj` — 新增。
  `TargetFramework` 用 `net10.0-windows`（需引用 `RestCue.Infrastructure`）；
  套件版本沿用現有測試專案：`Microsoft.NET.Test.Sdk` 17.10.0、`xunit` 2.8.1、
  `xunit.runner.visualstudio` 2.8.1；`ProjectReference` 指向
  `src/RestCue.Core/RestCue.Core.csproj` 與
  `src/RestCue.Infrastructure/RestCue.Infrastructure.csproj`。
- `RestCue.sln` — 修改。註冊上述專案，格式照既有
  `RestCue.Infrastructure.Tests` 區塊（含 Debug/Release、Any CPU 兩組設定）。

新增測試檔（放在新專案）：

- `tests/RestCue.Validation.Tests/StateScenarios/StateTransitionScenarioTests.cs`
  — 新增。以 `FakeClock : IClock` 驅動 `WorkCycleTracker`，覆蓋九個
  `WorkCyclePhase`（`Working`、`PendingReminder`、`ReminderVisible`、
  `BreakInProgress`、`Snoozed`、`Idle`、`Paused`、`FocusMode`、`Disabled`）
  與 `Tick`、`TickActivityUnavailable`、`Snooze`、`Ignore`、`StartBreak`、
  `ManualStartBreak`、`Pause`/`Resume`、`StartFocusMode`/`EndFocusMode`、
  `Disable`/`Enable`、`HandleLock`/`HandleUnlock`、`HandleSleep`/`HandleResume`。
- `tests/RestCue.Validation.Tests/StateScenarios/DebtAndIntensityScenarioTests.cs`
  — 新增。用 `DebtPolicy.Evaluate` 與 `PresentationIntensityPolicy`
  （`GetDebtRecommendation`、`FromFullscreenState`、
  `FromApplicationRuleType`、`Effective`）覆蓋 `RestDebtLevel.Level0`–`Level4`
  與 fullscreen／`ApplicationRuleType.Silent` 降級組合；不重複
  `tests/RestCue.Core.Tests/Policies` 已有的單元案例。
- `tests/RestCue.Validation.Tests/Privacy/PrivacyDenylist.cs` — 新增。
  allowlist 只允許 `usage_events` 的 `id`／`occurred_utc`／`event_type`／
  `payload` 欄位、`UsageEventType`／`ReminderResult`／`RestDebtLevel` 的 enum
  名稱與 ISO-8601 時間字串；denylist 覆蓋 window title、輸入內容、clipboard、
  screen content、URL（`http://`、`https://`）、document name（副檔名樣式）
  與 process name。
- `tests/RestCue.Validation.Tests/Privacy/PrivacyDenylistTests.cs` — 新增。
  用 `SchemaMigrator.EnsureSchemaAsync` 在 `Path.GetTempPath()` 下建臨時 DB，
  以 `SqliteUsageEventRepository.WriteAsync` 寫入所有 `UsageEventType`，
  再用 `Microsoft.Data.Sqlite` 讀 `sqlite_master` 的 schema 文字與
  `usage_events` 每一列，逐字串比對 denylist；並斷言 `payload` JSON 只出現
  `result`／`previous`／`current` 三個 key。同一測試也掃描收集到的 log 字串
  （`Action<string>` 收集器，對應 `BackgroundUsageEventWriter` 的 `onError`）。
- `tests/RestCue.Validation.Tests/Privacy/ProcessNameOptInTests.cs` — 新增。
  用 `WindowsForegroundContextProvider(canCollectProcessNames: false, ...)`
  搭配 fake `IFullscreenWin32Api`（照
  `tests/RestCue.Infrastructure.Tests/Activity/WindowsFullscreenDetectionTests.cs`
  的 `FakeFullscreenWin32Api`）斷言 `ForegroundContext.ProcessName` 為 `null`；
  只有明確傳 `true` 時才允許非 null，且 process name 不得進入 `usage_events`。
- `tests/RestCue.Validation.Tests/Soak/ResourceSampler.cs` — 新增。
  每個取樣點記 `Process.GetCurrentProcess()` 的 `TotalProcessorTime`、
  `WorkingSet64`、`PrivateMemorySize64`、`HandleCount`、`Threads.Count`，
  加上 DB 檔案 bytes 與累計 write 次數，輸出 CSV。
- `tests/RestCue.Validation.Tests/Soak/SoakHarness.cs` — 新增。長時間 soak，
  以 `[Trait("Category", "LongRun")]` 標記；時長讀環境變數
  `RESTCUE_SOAK_MINUTES`（未設定時用短預設值，例如 5 分鐘）。

新增／修改既有測試專案的檔案（需要 `internal` 成員者只能放在
`RestCue.App.Tests`，因為 `src/RestCue.App/RestCue.App.csproj` 的
`InternalsVisibleTo` 只授權該專案）：

- `tests/RestCue.App.Tests/PollingCadenceTests.cs` — 新增。用計數版
  `IUserActivityMonitor` 斷言「每個 tick 只取一次 `UserActivitySample`」，
  並斷言 activity timer interval 契約為 1 秒（`MainWindow` 的
  `DispatcherTimer.Interval = TimeSpan.FromSeconds(1)`）。
- `tests/RestCue.App.Tests/TrayUpdateCountTests.cs` — 新增。以計數版
  `ITrayIcon` fake（照 `WindowsTrayIconPhaseMappingTests` 的 `FakeTrayIcon`
  寫法）呼叫 `App.ApplyPhaseToTray`，斷言同一 phase＋`RestDebtLevel` 重複
  輸入不產生額外 `SetDebtLevel`／`SetSuppressedState` 的 icon 變更。
- `tests/RestCue.App.Tests/UsageEvents/SqliteWriteCadenceTests.cs` — 新增。
  用計數版 `IUsageEventRepository` 搭配 `BackgroundUsageEventWriter`，
  斷言純 polling tick（無狀態轉移）不產生任何 `WriteAsync`。

文件：

- `docs/testing/performance-privacy-validation.md` — 新增。短版／長版 harness
  的單一文件化命令、threshold、環境欄位與結果表（見「證據與紀錄格式」）。
- `docs/testing/test-plan.md` — 修改。Automated 段落補上 validation 專案與
  `Category=LongRun` 分類說明。
- `docs/known-limitations.md` — 修改。記錄未量測到的成長型指標與 soak 的
  環境依賴。

排除機制：預設 fast gate 一律加 `--filter "Category!=LongRun"`。
`RestCue.Validation.Tests` 內除 soak 以外的測試不加 trait，仍會在 fast gate
執行；只有 soak 帶 `LongRun`，因此不會拖慢預設測試。

### 執行方式

以下兩行已在本 repo 實跑驗證可用：

```powershell
dotnet build RestCue.sln
dotnet test RestCue.sln --filter "Category!=LongRun"
```

只跑 validation 專案的短版：

```powershell
dotnet test tests/RestCue.Validation.Tests/RestCue.Validation.Tests.csproj `
  --filter "Category!=LongRun"
```

只跑長版 soak（Windows 本機，release candidate 用）：

```powershell
$env:RESTCUE_SOAK_MINUTES = "480"
dotnet test tests/RestCue.Validation.Tests/RestCue.Validation.Tests.csproj `
  --filter "Category=LongRun" `
  --logger "trx;LogFileName=soak.trx" `
  --results-directory artifacts/validation
```

量測輸出一律寫到 `artifacts/validation/`。`.gitignore` 已忽略
`artifacts/`、`TestResults/`、`*.db`、`*.log`，所以原始輸出不會被誤 commit；
需要保留的摘要手動整理後貼進
`docs/testing/performance-privacy-validation.md`，不要 commit 原始 CSV/TRX。

### 實作順序

1. 建立 `tests/RestCue.Validation.Tests` 並註冊到 `RestCue.sln`，先確認
   `dotnet test RestCue.sln --filter "Category!=LongRun"` 仍全綠再往下做。
2. 寫 `FakeClock : IClock`，照
   `tests/RestCue.Core.Tests/Reminders/WorkCycleTrackerTests.cs` 內既有樣式
   （固定起始時間 + `Advance(TimeSpan)`）。所有時間推進都用
   `clock.Advance(...)` 搭配 `tracker.Tick(idleDuration)`，
   **不得用 `Thread.Sleep`／`Task.Delay` 模擬時間**。
3. 補 state scenario 測試。斷言 `tracker.CurrentPhase`、
   `tracker.AccumulatedWorkTime`、`tracker.RestDebtLevel` 與觸發的 event，
   不要斷言任何 UI 字串。
4. 補 debt／intensity 組合測試，特別注意 `FullscreenState.Uncertain` 也必須
   降到 `PresentationIntensity.TrayOnly`（`FromFullscreenState` 現行契約）。
5. 在 `RestCue.App.Tests` 補 cadence 與 tray 計數測試：
   - cadence 用計數 `IUserActivityMonitor`：N 個模擬 tick 恰好 N 次
     `GetCurrentActivity()`，不得有第二個 poller 重複取樣。
   - tray 用計數 fake `ITrayIcon` 走 `App.ApplyPhaseToTray`；
     `WindowsTrayIcon` 本身（`NotifyIcon`）不進測試。
   - 不要在新專案呼叫 `App.ApplyPhaseToTray`，那是 `internal`。
6. 補 SQLite write cadence 測試，計數 `IUsageEventRepository.WriteAsync`，
   斷言只有真實狀態轉移才寫入。
7. 寫 privacy 掃描：建臨時 DB → 寫入所有事件型別 → 讀回 schema 與所有資料列
   → 每個字串跑 denylist；再對 log 收集器內容跑同一組 denylist。
   斷言 log 與 DB 都沒有 window title、輸入內容、clipboard、screen content、
   URL、document name。
8. 寫 process name opt-in 測試：預設（`false`）必須是 `null`，只有 opt-in 打開
   才允許出現 process name，且仍不得寫入 `usage_events`。
9. 最後才寫 soak，且必須保持有界與可重現：
   - 時長讀 `RESTCUE_SOAK_MINUTES`，未設就用短預設值，CI 不會被卡住。
   - 取樣間隔固定（例如 60 秒），取樣次數 = 時長 / 間隔，輸出列數可預期。
   - 用 `CancellationTokenSource` 設硬上限，超時判定 fail 而非無限等待。
   - 只有「必須觀察真實資源成長」的部分用真實時間；狀態語義一律用
     `FakeClock`。
   - 判定條件是「8 小時內無持續單調且無界成長」，不是單點峰值；量到成長就開
     issue，不自行放寬門檻。
10. 更新 `docs/testing/performance-privacy-validation.md`、
    `docs/testing/test-plan.md` 與 `docs/known-limitations.md`。

### 證據與紀錄格式

`docs/testing/performance-privacy-validation.md` 至少含三張表。

環境表：

| Field | Value |
|---|---|
| Date (UTC) | |
| OS build | |
| .NET SDK | |
| App commit | |
| Configuration | Debug / Release |
| Hardware (CPU/RAM) | |

短版結果表：

| Check | Command | Threshold | Result |
|---|---|---|---|
| state scenarios | | 九個 phase 全覆蓋 | PASS/FAIL |
| polling cadence | | 每 tick 1 sample | PASS/FAIL |
| tray update count | | 穩定狀態 0 次重繪 | PASS/FAIL |
| sqlite write count | | 純 poll 0 writes | PASS/FAIL |
| privacy denylist | | 0 命中 | PASS/FAIL |
| process name opt-in | | 預設為 null | PASS/FAIL |

soak 結果表（每取樣點一列，來源 `artifacts/validation/soak.csv`）：

| Sample | Elapsed | CPU % avg | WorkingSet MB | PrivateBytes MB |
|---|---|---|---|---|

| Sample | Handles | Threads | DB KB | Writes |
|---|---|---|---|---|

引用證據時只寫檔名、行號與數值；CSV/TRX 留在 `artifacts/`（gitignored）。
若需截圖，只截 RestCue 自己的視窗或 tray tooltip，畫面不得出現其他 app 的
window title、URL、文件名稱或輸入內容；貼 log 前先讓該行通過 denylist。

### 常見錯誤

- 用 `Thread.Sleep`／`Task.Delay` 代替 `FakeClock.Advance`：測試同時變慢與
  flaky，也無法覆蓋小時級時間語義。
- 忘記給 soak 加 `[Trait("Category", "LongRun")]`，導致
  `dotnet test RestCue.sln` 從秒級變成分鐘／小時級。
- 斷言 tray 的中文字串（例如 `"RestCue – 已暫停"`）當作行為驗證：文案一改就
  壞。請斷言 `SetPauseEnabled`／`SetDebtLevel`／`SetSuppressedState` 的呼叫
  次數與參數。
- 在 `RestCue.Validation.Tests` 直接用 `App.ApplyPhaseToTray` 等 `internal`
  成員而編譯失敗（`InternalsVisibleTo` 只給 `RestCue.App.Tests`）。
- 用真實的 `LocalSettingsPaths.DatabaseFile`
  （`%LOCALAPPDATA%\RestCue\restcue.db`）做測試，污染使用者資料；必須用
  `Path.GetTempPath()` 下的臨時目錄，照 `SqliteUsageEventRepositoryTests`
  建立與清除。
- 忘記 `SqliteUsageEventRepository` 以 `SqliteOpenMode.ReadWrite` 開檔，
  沒先跑 `SchemaMigrator.EnsureSchemaAsync` 就 `WriteAsync` 而失敗。
- 產出的 artifacts 自己洩漏禁止資料（完整命令列、含使用者姓名的路徑、
  其他 app 的 window title 被寫進 CSV 或截圖）。
- 只看單點峰值就宣告 growth 通過，或量到成長卻不開 issue。

### 逐步 checklist

- [ ] 建立 `tests/RestCue.Validation.Tests` 並註冊到 `RestCue.sln`
- [ ] 加入 `FakeClock : IClock`，全專案 grep 確認無 `Thread.Sleep`
- [ ] state scenario 覆蓋九個 `WorkCyclePhase` 與 lock/sleep/resume 入口
- [ ] debt × intensity 組合（含 `FullscreenState.Uncertain`）覆蓋
- [ ] `PollingCadenceTests` 斷言每 tick 一次 sample、無重複 poller
- [ ] `TrayUpdateCountTests` 斷言穩定狀態不重複更新 icon
- [ ] `SqliteWriteCadenceTests` 斷言純 poll 無寫入
- [ ] `PrivacyDenylistTests` 掃 schema、資料列與 log 皆 0 命中
- [ ] `ProcessNameOptInTests` 斷言預設關閉時為 `null`
- [ ] soak 以 `[Trait("Category", "LongRun")]` 標記且有硬上限
- [ ] artifacts 全部落在 `artifacts/validation/`
- [ ] `dotnet test RestCue.sln --filter "Category!=LongRun"` 全綠
- [ ] 執行一次 `RESTCUE_SOAK_MINUTES=480` 長版並填三張表
- [ ] 人工複查 artifacts 無禁止資料
- [ ] 更新 `docs/testing/performance-privacy-validation.md`、
      `docs/testing/test-plan.md`、`docs/known-limitations.md`

## Execution checklist

- [ ] 建立 deterministic state-scenario harness，涵蓋 Working、Passive Pause、
      Idle、Snooze、Ignore、AutoDismissed、Break completed/cancelled、Pause、
      Focus Mode、debt levels、fullscreen/silent downgrade。
- [ ] 以短版測試確認活動來源平均約每秒一次，不因 UI 重複建立 poller。
- [ ] 計數 tray render/update，穩定狀態不得每秒重建 icon 或持續動畫。
- [ ] 計數 SQLite writes；單純 poll 不得每秒寫入無意義 snapshot。
- [ ] 建立 8 小時 soak harness，定期記 CPU、working set、private bytes、handles、
      threads、DB size 與 write counts。
- [ ] 對 logs/DB/export 的 schema 與內容跑 privacy denylist/allowlist 檢查。
- [ ] 故障注入包含 activity unavailable、audio failure、DB locked、fullscreen
      unknown、sleep/large clock gap。
- [ ] test artifacts 不包含禁止資料，並可由另一 agent 重跑。
- [ ] 更新 `docs/testing/test-plan.md` 與 known limitations。

## Acceptance checklist

- [ ] one-second polling 沒有 duplicate loop；穩定 tray 沒有持續更新／動畫。
- [ ] 核准門檻下的 CPU、memory、handle、thread 與 DB growth 測試通過。
- [ ] logs、DB、export 不含 window title、input、clipboard、screen、URL、
      document name；process name 遵守 opt-in。
- [ ] 核心 v1.3 所有主要轉移有自動化 fake-clock 覆蓋。
- [ ] 短版 CI 與長版 Windows soak 均有單一文件化命令與 machine-readable 結果。
- [ ] failure injection 不導致 focus steal、input block、資料刪除或 crash loop。

## Verification

- [ ] `dotnet build RestCue.sln`
- [ ] `dotnet test RestCue.sln --no-build`
- [ ] 執行短版 performance/privacy harness
- [ ] 執行並保存一次完整 8 小時 soak 結果
- [ ] `git diff --check`

## Data/schema impact

無產品 schema 變更。測試 artifacts 只能放明確測試輸出位置且不得含使用者資料。

## Completion report

- [x] Changes: New `tests/RestCue.Validation.Tests/` project (csproj + 7 test files), new `tests/RestCue.App.Tests/PollingCadenceTests.cs`, `TrayUpdateCountTests.cs`, `UsageEvents/SqliteWriteCadenceTests.cs`, new `docs/testing/performance-privacy-validation.md`, modified `RestCue.sln`, `docs/testing/test-plan.md`, `docs/known-limitations.md`.
- [x] Tests/measurements: 624 total (431 Core + 46 Validation + 84 App + 63 Infrastructure; +53 vs baseline). State scenarios cover 9 WorkCyclePhase values + lock/sleep/resume. Debt×intensity covers Level0–4 + fullscreen states + application rules. Privacy denylist scans schema, data rows, and log messages. Process name opt-in tests default=false→null, true→non-null, and not in usage_events. Soak harness has `[Trait("Category", "LongRun")]`, env var `RESTCUE_SOAK_MINUTES`, CTS timeout, CSV output to `artifacts/validation/`.
- [x] Known limitations: Soak harness environment-dependency and 60-second sampling granularity documented in `docs/known-limitations.md`.
- [x] Data/schema impact: No product schema changes. Test artifacts only.
