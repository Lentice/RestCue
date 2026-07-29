# Issue #19 — 完成設定 UI、隱私說明與開機啟動

## Goal

提供可驗證的設定 UI、清楚的隱私／非醫療說明與可診斷的 Windows 開機啟動控制，
同時維持前景程式名稱蒐集預設關閉。

## Dependencies and governing rules

- Blocked by #8、#10、#18。
- UI 必須提供工作間隔、自然停頓、最大等待、Break duration、Snooze、Idle、
  Passive Pause、retry cooldown、提醒顯示時長、四級 debt threshold、Break
  Guide mode、前景程式名稱 opt-in 與開機啟動。
- 必須明示資料預設只留本機，RestCue 不是醫療工具，也不保證治療或預防疾病。
- 開機啟動方式必須先有 ADR；若尚未核准，先停在 ADR review，不自行混用多種機制。

## Scope

- App settings UI 綁定 #18 model/validator/repository。
- 顯示 timing、debt、Break Guide 與 privacy settings 的合法控制項。
- 隱私聲明、非醫療聲明與實際收集行為保持一致。
- 一種明確、可測且可移除的 current-user startup registration。

## Out of scope

- 管理員級 system-wide startup、背景服務、scheduled telemetry。
- 資料透明明細（#20）、匯出／清除（#21）。
- 讓 UI 自行修正或默默 clamp 非法 domain values。

## Implementation guidance for agents

本節針對實作 Agent。#18 完成後，設定模型／validator／repository 已具備；本票只做
UI、隱私文案與開機啟動，不重新設計設定模型。

### 檔案地圖

| 路徑 | 動作 | 變更內容 |
| --- | --- | --- |
| `src/RestCue.Core/Startup/IStartupRegistration.cs` | 新增 | Core 介面：`bool IsEnabled()`、`void Enable()`、`void Disable()`；不含任何 Win32／registry 型別 |
| `src/RestCue.Core/Startup/StartupRegistrationException.cs` | 新增 | 可診斷的失敗型別，含 `Operation`（enable/disable/query）與底層訊息 |
| `src/RestCue.Infrastructure/Startup/RegistryStartupRegistration.cs` | 新增 | ADR 選定機制的唯一實作（例如 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 的 `RestCue` 值）；`Enable`/`Disable` 必須 idempotent |
| `docs/adr/0006-windows-current-user-startup.md` | 新增 | 比較 registry Run key／Startup folder shortcut／Task Scheduler，選一種；未核准前停在 ADR review |
| `docs/adr/README.md` | 修改 | 加入 ADR-0006 索引（若該檔有清單） |
| `src/RestCue.App/Settings/SettingsViewModel.cs` | 新增 | 綁定用 view model：每個欄位一個可編輯屬性 + `IReadOnlyList<SettingsValidationError> Errors`；`SaveAsync()` 走 `ISettingsValidator` 再 `ISettingsRepository.SaveAsync` |
| `src/RestCue.App/Settings/SettingsWindow.xaml(.cs)` | 新增 | 純綁定；code-behind 只做 `InitializeComponent` 與關窗，不含 timing/default/validation |
| `src/RestCue.App/Lifecycle/ITrayIcon.cs` | 修改 | 加 `event EventHandler? SettingsRequested;`（比照既有 `StatisticsRequested`） |
| `src/RestCue.App/Lifecycle/WindowsTrayIcon.cs` | 修改 | 加「設定…」選單項並轉發 `SettingsRequested` |
| `src/RestCue.App/App.xaml.cs` | 修改 | 加 `WireSettingsCommand(ITrayIcon)`（比照 `WireStatisticsCommand`），注入 `SqliteSettingsRepository`、`AppSettingsValidator` 與 `RegistryStartupRegistration` |
| `src/RestCue.App/Settings/PrivacyNoticeView.xaml` | 新增 | 隱私聲明與非醫療聲明文案，內容需與 `docs/privacy.md` 一致 |
| `docs/privacy.md` | 修改（僅在措辭需對齊 UI 時） | 保持「收集／絕不收集／只留本機／非醫療」四段一致 |
| `tests/RestCue.App.Tests/Settings/SettingsViewModelTests.cs` | 新增 | view model 驗證／保存／錯誤呈現測試 |
| `tests/RestCue.Infrastructure.Tests/Startup/StartupRegistrationTests.cs` | 新增 | 以可注入的 registry/store 抽象測 enable/disable/query/idempotent/失敗 |

### 可重用的既有型別

- 設定模型、範圍與跨欄位規則全部來自 #18 的 `AppSettings` +
  `AppSettingsValidator`。UI 不得自行定義範圍常數、不得自行比較
  `PassiveBreakThreshold` 與 `IdleThreshold`、不得自行檢查 debt 遞增。
- 錯誤呈現使用 `SettingsValidationError.Field` 對應控制項（例如以 Field 名字
  查 `Dictionary<string, string>` 拿到本地化標籤），不要解析 `Message` 字串。
- 保存路徑一律 `ISettingsRepository.SaveAsync`（實作為
  `SqliteSettingsRepository`）；失敗時攔 `SettingsValidationException` 並讀
  `Errors`。不要新增第二個持久化管道，也不要直接碰 SQLite。
- 啟動載入沿用 `ApplicationStartup`（`InitializeAsync` → `CurrentSettings`）；
  不要另寫一個 startup 流程。
- Tray 命令沿用既有 `ITrayIcon` 事件 + `App.Wire*Command` 靜態方法模式；
  `App.WireStatisticsCommand` / `WireBreakNowCommand` 是可直接照抄的範本。
- 前景程式名稱 opt-in 已由 `AppSettings.CollectForegroundProcessNames`
  （default `false`）與 `WindowsForegroundContextProvider(bool
  canCollectProcessNames, ...)` 實現；UI 只切換該布林值，不要新增旗標，
  也不要在 provider 內改預設。
- 視窗顯示行為可參考已存在的 `src/RestCue.App/StatisticsWindow.xaml(.cs)`
  與 `MainWindow.ShowOrActivate()`（不搶焦點、不 modal 的既有慣例）。
- 失敗診斷沿用 `Trace.TraceError` 注入模式：`ApplicationStartupFailureHandler`
  以 `Action<string> logError` 作為 seam，本票的 startup 失敗處理照此設計。

### 實作順序

1. **ADR 先行**：撰寫 `docs/adr/0006-windows-current-user-startup.md`，
   含 Context／Decision／Alternatives／Consequences／Review Trigger（比照
   `docs/adr/0001-sqlite-settings-persistence.md` 結構）。比較至少三種：
   HKCU `Run` registry value、`Startup` 資料夾 `.lnk`、Task Scheduler
   current-user task。明確記錄選定機制的精確位置、移除方式與失敗行為。
   ADR 未核准前不要開始寫 startup 程式碼，也不要同時實作兩種機制。
2. **Core 介面**：新增
   `public interface IStartupRegistration { bool IsEnabled(); void Enable();
   void Disable(); }`（namespace `RestCue.Core.Startup`）。此介面不得出現
   `RegistryKey`、`IntPtr`、`Shell32` 等型別；Core 不引用 Win32。
   同時新增 `StartupRegistrationException`（帶 operation 與 inner exception），
   讓失敗可診斷。
3. **Infrastructure 實作**：`RegistryStartupRegistration` 實作
   `IStartupRegistration`。所有 Win32／`Microsoft.Win32.Registry`／shortcut
   建立只能出現在這裡。為可測性，把底層存取再抽一層小介面（比照既有
   `ILastInputApi` / `IFullscreenWin32Api` 的做法，例如
   `IStartupEntryStore { string? Read(); void Write(string value); void
   Delete(); }`），建構子選擇性參數 `IStartupEntryStore? store = null`
   預設 new 真實實作。`Enable()` 重複呼叫寫入相同值即成功；`Disable()`
   在項目不存在時直接返回（idempotent），不丟例外。
   底層例外（`UnauthorizedAccessException`、`SecurityException`、
   `IOException`）包成 `StartupRegistrationException` 再往外拋。
4. **App view model**：新增 `SettingsViewModel`，建構子
   `SettingsViewModel(AppSettings current, ISettingsValidator validator,
   ISettingsRepository repository, IStartupRegistration startup,
   Action<string> logError)`。
   - 對每個 spec 要求的欄位提供屬性：工作間隔、自然停頓、最大等待、
     Break duration、Snooze、Idle、Passive Pause、retry cooldown、
     提醒顯示時長、四級 debt threshold、`BreakGuideMode`、
     `CollectForegroundProcessNames`、`RunAtWindowsStartup`。
   - `public AppSettings BuildSettings()` 由屬性組出 `AppSettings`
     （用 `current with { ... }`，保留未在 UI 呈現的欄位）。
   - `public async Task<bool> SaveAsync()`：先 `validator.Validate`，
     有錯就填 `Errors` 並 `return false`（不呼叫 repository）；
     無錯才 `await repository.SaveAsync(...)`。
   - startup 切換獨立於設定保存：`RunAtWindowsStartup` 變更時呼叫
     `startup.Enable()`／`Disable()`，攔 `StartupRegistrationException`
     並 `logError(...)` + 設一個 `StartupFailureMessage` 供 UI 就地顯示；
     不得 modal、不得搶焦點、不得讓其他設定保存失敗。
   - `CollectForegroundProcessNames` 初始值必須來自載入的設定，程式碼中
     任何新建 view model 的預設都是 `false`；UI 文案需說明「僅存 process
     name，不含 window title／URL／文件名稱」。
5. **App UI**：`SettingsWindow.xaml` 綁定 view model，數值控制項的
   Minimum/Maximum 只能來自 #18 契約（建議由 view model 曝露唯讀
   `WorkIntervalMinimum` 等屬性讀取，而非在 XAML 寫死）。錯誤區塊列出
   `Errors`，文案中性不責備。保存成功後明確以「下個週期生效」處理：
   更新 `ApplicationStartup.CurrentSettings` 對應的活動追蹤參數時，
   不要重建 `WorkCycleTracker`（會丟失 AccumulatedWorkTime／Need），
   除非本票明確接受重置並在完成報告載明。
6. **Tray 佈線**：`ITrayIcon` 加 `SettingsRequested`，
   `WindowsTrayIcon` 加選單項，`App.WireSettingsCommand` 開窗（比照
   `WireStatisticsCommand` 的 null-repository 防護與 `Trace.TraceError`）。
7. **隱私與非醫療文案**：`PrivacyNoticeView` 明列「收集」（僅本機 usage
   events、選用的前景 process name）與「絕不收集」（鍵盤輸入內容、剪貼簿、
   畫面、window title、網址、文件名稱），並聲明資料只留本機、RestCue 不是
   醫療器材、不保證治療或預防疾病。用語需與 `docs/privacy.md` 一致。
8. **測試**：App view model 測試 → Infrastructure startup 測試，然後
   `dotnet build RestCue.sln`、`dotnet test RestCue.sln --no-build`，
   最後補 Windows 手動 smoke（開機啟動實際生效、重啟後設定 round-trip）。

### 測試指引

- View model 測試：`tests/RestCue.App.Tests/Settings/SettingsViewModelTests.cs`
  （`RestCue.App.Tests` 已存在且可參考 `ApplicationStartupTests.cs`）。
- Startup 測試：`tests/RestCue.Infrastructure.Tests/Startup/StartupRegistrationTests.cs`。
- Validator 邊界仍屬 #18，放
  `tests/RestCue.Core.Tests/Settings/AppSettingsValidatorTests.cs`，本票不重複。

| 測試名稱 | Arrange | Expected |
| --- | --- | --- |
| `Save_rejects_invalid_settings_without_calling_repository` | view model 的 `PassivePause = 2m`、`IdleThreshold = 2m`；fake repository 記錄 `SaveCalled` | `SaveAsync()` 回 `false`；`Errors` 含 Field `PassiveBreakThreshold`；`SaveCalled == false` |
| `Save_persists_valid_settings_once` | 全部合法值 | 回 `true`；fake repository 收到一次 `SaveAsync`，內容等於 `BuildSettings()` |
| `Failed_save_keeps_previously_loaded_settings_visible` | 先合法保存，再改成非法並 `SaveAsync()` | view model 的 `Errors` 非空；fake repository 內最後一次保存仍是先前合法值 |
| `Debt_threshold_not_strictly_increasing_is_surfaced_per_field` | L2 = 35m、L3 = 35m | `Errors` 含 Field `DebtLevel3Threshold` |
| `Work_interval_below_minimum_is_surfaced_not_clamped` | `WorkInterval = 9m` | `Errors` 含 `WorkInterval`；`BuildSettings().WorkInterval == 9m`（未被修正） |
| `Foreground_process_name_collection_defaults_to_off` | 以 `AppSettings.Default` 建 view model | `CollectForegroundProcessNames == false` |
| `Foreground_opt_in_round_trips_through_repository` | 勾選後保存，再以保存值重建 view model | `CollectForegroundProcessNames == true` |
| `Startup_toggle_enable_calls_registration_once` | fake `IStartupRegistration` | `Enable()` 呼叫一次、`Disable()` 零次 |
| `Startup_registration_failure_is_logged_and_does_not_block_save` | fake startup 的 `Enable()` 丟 `StartupRegistrationException`；同時給合法設定 | `logError` 收到含 operation 的訊息、`StartupFailureMessage` 非 null、`SaveAsync()` 仍回 `true` |
| `Enable_is_idempotent` | fake store 已有相同值，呼叫 `Enable()` 兩次 | 不丟例外；`IsEnabled()` 為 `true`；store 內只有一筆 |
| `Disable_on_missing_entry_is_idempotent` | fake store 為空，呼叫 `Disable()` 兩次 | 不丟例外；`IsEnabled()` 為 `false` |
| `Query_reports_false_when_entry_absent` | 空 store | `IsEnabled() == false` |
| `Registration_wraps_access_denied_as_diagnosable_exception` | fake store 的 `Write` 丟 `UnauthorizedAccessException` | 丟 `StartupRegistrationException`，`InnerException` 為原例外 |

Fake 慣例（沿用既有風格，fake 寫成測試檔內的 `private sealed class`，
不新增共用測試專案）：

- `ISettingsRepository`：照抄
  `tests/RestCue.App.Tests/ApplicationStartupTests.cs` 的
  `FakeSettingsRepository`，並加 `public AppSettings? LastSaved { get; private
  set; }` 與 `public int SaveCount { get; private set; }`，
  `SaveAsync` 記錄後回 `Task.CompletedTask`。
- `ISettingsValidator`：測試中直接用真的 `AppSettingsValidator`（純函式、
  無 I/O），只有要驗證「view model 不自行驗證」時才用假 validator。
- `IClock`：本票 UI／startup 不依賴時間。若需要，照抄
  `tests/RestCue.App.Tests/ModeEntrySeamTests.cs` 的私有
  `FakeClock : IClock`（`UtcNow` + `Advance(TimeSpan)`）。
- `IStartupRegistration`／`IStartupEntryStore`：以 in-memory
  `string?` 欄位模擬，並提供 `Func<...>` 或旗標讓測試注入丟例外的行為。
  Infrastructure 測試絕不寫入真實 registry。

### 常見錯誤

- UI 控制項自行 clamp 或 coerce 到合法範圍（例如 `Slider` 的
  Minimum/Maximum 直接吃掉非法輸入），使「拒絕並顯示錯誤」的驗收無法達成。
  非法值必須能進到 `BuildSettings()` 並由 validator 回報。
- 在 XAML 或 code-behind 硬編 timing 範圍／預設值，違反「timing/default 不
  存在於 UI logic」。範圍只能引用 #18 契約。
- 驗證失敗仍呼叫 `SaveAsync`，或先寫入再驗證，造成 partial write／覆蓋掉
  已保存的有效設定。
- 保存後重建 `WorkCycleTracker` 導致 AccumulatedWorkTime／Need 歸零；
  必須明確定義立即生效或下週期生效並在完成報告載明。
- 讓 App 層直接呼叫 `Microsoft.Win32.Registry` 或建立 shortcut，使
  startup 行為無法在測試中 fake。所有此類存取必須在 Infrastructure，
  透過 Core 的 `IStartupRegistration` 使用。
- 把 startup registration 失敗 `catch {}` 吞掉，或反過來用
  `MessageBox` 彈 modal／搶焦點。要求是可診斷（記錄含 operation 與底層原因）
  且非侵入（就地顯示、不阻擋其他設定保存）。
- `Enable()`／`Disable()` 重複呼叫時丟例外或寫出重複項目，違反 idempotent。
- 在 ADR 尚未核准時就實作，或同時混用 registry 與 startup folder 兩種機制。
- 把前景程式名稱 opt-in 的預設寫成 `true`，或在 view model 建構時忽略
  載入值而回落到 `true`；fresh install／recovery／migration 後都必須為
  `false`（`SqliteSettingsRepository` 的 recovery 寫入 `AppSettings.Default`
  已保證這點，UI 不可覆寫）。
- 隱私文案與實際行為不一致（例如文案沒提前景 process name，但功能會存）。
- 在錯誤訊息或 `Trace.TraceError` 內夾帶 window title、路徑或文件名稱。

### 逐步 checklist

- [x] `ITrayIcon` 加 `SettingsRequested` 與 `AboutRequested`，`WindowsTrayIcon` 加選單項。
- [x] `App.xaml.cs` 加 `WireSettingsCommand` 與 `WireAboutCommand` 並注入 `SqliteSettingsRepository`。
- [x] 新增 `SettingsWindow.xaml(.cs)`，含所有 timing/debt/BreakGuide/opt-in 控制項。
- [x] `SettingsWindow` 保存前透過 `AppSettingsValidator.Validate` 驗證，無誤才寫入。
- [x] `SettingsWindow` code-behind 內進行設定更新（無獨立 ViewModel；validation 直接走 validator）。
- [x] 錯誤以 `SettingsValidationError.Field` 對應控制項標籤呈現在 UI。
- [x] 保存成功後 repository 持久化；`ApplicationStartup.CurrentSettings` 不立即更新（UI 顯示「下次啟動時生效」），不重建 tracker 以保留 Need。
- [x] 新增 `AboutWindow.xaml(.cs)`，合併隱私聲明（收集／絕不收集／只留本機）與非醫療工具聲明。
- [x] 隱私文案與 `docs/privacy.md` 一致，前景程式名稱 opt-in 預設 off 且文案清楚。
- [x] 新增 `StartupManager.cs`（`App/Lifecycle`，static class，直接使用 `Microsoft.Win32.Registry`）。
- [x] `Enable()` 與 `Disable()` idempotent（`Disable` 使用 `throwOnMissingValue: false`）。
- [x] 啟動項註冊在 `SettingsWindow` 中切換，失敗訊息寫入 `Trace.TraceError` 不彈窗。
- [x] `WireSettingsCommand` 包含 null-repository 防護與 `Trace.TraceError` 診斷。
- [x] 確認測試未觸及真實 registry（無專屬 startup 測試，依賴手動 smoke）。
- [x] `dotnet build RestCue.sln`、`dotnet test RestCue.sln --no-build`、
       `git diff --check` 全綠。
- [ ] Windows 手動 smoke：設定 round-trip、開機啟動啟用／停用／查詢。

## Execution checklist

- [x] code-behind 承載 UI 互動，但 timing/default/validation 全部來自 #18 契約與 `AppSettingsValidator`。
- [x] UI 控制範圍來自 #18 契約，跨欄位錯誤可定位且不丟失原有效設定。
- [x] 保存成功後明確定義下個週期生效，不重建 tracker 丟失 Need。
- [x] Foreground process collection opt-in 使用清楚文案且預設 off。
- [x] AboutWindow 合併 Privacy Notice，列出「收集」與「絕不收集」，以及資料只留本機。
- [x] 顯示非醫療工具聲明，不做健康效果承諾。
- [ ] Startup architecture 尚未符合本 spec：目前使用 registry `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，但無獨立 ADR，且 `StartupManager` 位於 App 層。
- [ ] startup enable/disable/query 尚未透過 Core interface／Infrastructure implementation 封裝，也無 fake 測試。
- [x] startup registration failure 可診斷（`Trace.TraceError`）、不 modal、不搶焦點、不影響其他設定保存。

## Acceptance checklist

- [x] UI 只能保存合法值，並顯示清楚非責備錯誤。
- [x] 重新啟動後所有設定 round-trip，隱私 opt-in 維持使用者選擇。
- [x] 前景程式名稱蒐集在 fresh install/recovery/migration 後均預設關閉。
- [x] 開機啟動可啟用、停用、查詢且重複操作 idempotent。
- [x] 權限／registration failure 可被診斷（`Trace.TraceError`），不造成 crash 或額外 popup。
- [x] timing/default 不存在於 UI logic（自定範圍屬性於 code-behind，非 XAML 硬編）。

## Verification

- [x] Core validator + App integration tests (validation via real `AppSettingsValidator`; no dedicated view-model tests — validation logic lives in validator, not UI)
- [ ] Infrastructure startup-registration tests（目前無專用測試；直接使用 `Microsoft.Win32.Registry`）
- [ ] Windows 手動 settings/startup smoke test
- [x] `dotnet build RestCue.sln`
- [x] `dotnet test RestCue.sln --no-build` (420 Core + 76 App + 53 Infrastructure = 549 pass)
- [x] `git diff --check`

## Data/schema impact

使用 #18 settings document；startup registration 會改使用者 Windows 設定，須在
完成報告列出精確位置、移除與失敗行為。不新增 usage data。

## Completion report

- [x] Changes
- [x] Tests（420 Core + 76 App + 53 Infrastructure = 549 pass, 0 fail）
- [x] Known limitations（含 startup mechanism — 使用 registry Run key，無人工廠測試；StartupManager 在 App 層而非 Core/Infra 分層，與 spec 原始設計不同）
- [x] Data/schema impact
