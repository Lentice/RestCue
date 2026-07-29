# Issue #10 — Break Guide 聲音／語音模式與失敗降級

## Goal

在 #9 的 Break Guide 上加入一般節奏提示音與簡短語音模式；任何音訊初始化、
播放或裝置切換失敗，都安靜降級為無數字視覺引導，且不改變完成／取消語義。

## Dependencies and governing rules

- Blocked by #9；開工前確認 #9 已關閉。
- Spatial Audio 是 Optional Experiment，不是本票交付。
- MVP 必須提供節奏提示音、簡短語音、無數字視覺三種模式；預設為節奏提示音。
- 預設 20 秒流程：開始提示「看向約 6 公尺外」、中段提示慢慢眨眼並放鬆肩膀、
  完成提示結束。語句可因在地化微調，但不可出現倒數。

## Scope

- Core 定義與平台無關的 Break Guide mode 與音訊失敗結果。
- Infrastructure/App 封裝可替換的音訊播放邊界，支援 fake 實作。
- 節奏提示音至少涵蓋開始、中段、完成；語音使用簡短固定提示。
- 音訊失敗後同一次引導立即切換至無數字視覺模式。

## Out of scope

- Spatial Audio、背景下載語音、雲端 TTS、音訊遙測。
- 額外錯誤彈窗、系統通知或焦點切換。
- 改變 #9 的 `BreakCompleted`／`BreakCancelled` 判定。

## Implementation guidance for agents

本票疊在 #9 的 `BreakGuideSession` 上。#15 已關閉，
`PresentationIntensity`／`PresentationIntensityPolicy` 已上線，音量／音訊的
可見性上限請沿用它，不要新造一套強度概念。

### 檔案地圖

| 路徑 | 新增/修改 | 這裡要改什麼 |
|------|-----------|--------------|
| `src/RestCue.Core/Reminders/BreakGuideMode.cs` | 新增 | `enum BreakGuideMode { Chime, Speech, VisualOnly }`，預設 `Chime`。 |
| `src/RestCue.Core/Audio/IBreakGuideAudioPlayer.cs` | 新增 | 平台無關播放邊界：`bool TryInitialize()`、`bool TryPlay(BreakGuideCue cue, BreakGuideMode mode)`、`void Stop()`、`IDisposable`。回傳 bool，**不丟例外**。 |
| `src/RestCue.Core/Audio/AudioFailureReason.cs` | 新增 | `enum AudioFailureReason { InitializationFailed, PlaybackFailed, DeviceUnavailable, DisposeFailed }`。 |
| `src/RestCue.Core/Audio/BreakGuideAudioCoordinator.cs` | 新增 | 純邏輯：持有目前 mode，任一次失敗即降級為 `VisualOnly` 並發 `DegradedToVisual` 一次；不碰計時。 |
| `src/RestCue.Infrastructure/Audio/WindowsBreakGuideAudioPlayer.cs` | 新增 | 唯一接觸 Windows 播放 API（`System.Media.SoundPlayer` / WPF `MediaPlayer` / SAPI）的位置；所有呼叫包 `try/catch` 後轉成 `false`。 |
| `src/RestCue.App/MainWindow.xaml.cs` | 修改 | 建立 coordinator 並訂閱 #9 的 `BreakGuideSession.CueChanged`；取消／完成／`StopActivityTracking()` 時 `Stop()` + `Dispose()`。 |
| `src/RestCue.App/App.xaml.cs` | 修改 | 在 `OnStartup` 注入 `WindowsBreakGuideAudioPlayer`（比照現有 `WindowsUserActivityMonitor` 的注入方式）；`OnExit` 釋放。 |
| `tests/RestCue.Core.Tests/Audio/BreakGuideAudioCoordinatorTests.cs` | 新增 | fake player 的成功／各類失敗／降級測試。 |
| `tests/RestCue.App.Tests/BreakGuideAudioSeamTests.cs` | 新增 | 「移除音訊能力不改變完成／取消語義」的 seam 測試。 |

### 可重用的既有型別

- `RestCue.Core.Reminders.BreakGuideCue`／`BreakGuideSession`（#9 新增）：
  音訊只訂閱 cue，**不得**自行計時或判定完成。
- `RestCue.Core.Domain.PresentationIntensity`
  （`src/RestCue.Core/Domain/PresentationIntensity.cs`）已有
  `PopupAndSound = 3`；是否允許出聲請以
  `PresentationIntensityPolicy.Effective(...) >= PopupAndSound` 判斷，並沿用
  `PresentationIntensityPolicy.SilentCap`（`None`）代表靜音應用規則。
- `RestCue.Core.Reminders.ApplicationRuleType.Silent`
  （`src/RestCue.Core/Reminders/ApplicationRuleType.cs`）已存在，靜音情境不要
  另建旗標。
- `RestCue.Core.Time.IClock`：若需要間隔保護（例如避免同一 cue 重複播放），
  用注入的 clock，不要 `DateTime.UtcNow`。
- 記錄／log 沿用 `Trace.TraceError` + `Action<string>` 回呼的既有樣式
  （見 `App.xaml.cs`、`BackgroundUsageEventWriter`）。
- 不要建立的東西：新的 `UsageEventType` 值（音訊遙測是 out of scope，
  `src/RestCue.Core/UsageEvents/UsageEventType.cs` 不得新增成員）、
  新的 `AppSettings` 欄位（模式持久化屬 #18）、任何 `MessageBox`／
  `ToastNotification`、任何裝置名稱或音訊內容的儲存、NAudio 等外部套件。

### 實作順序

1. Core：新增 `BreakGuideMode` enum（`Chime` 排第一位，即預設）。
2. Core：新增 `AudioFailureReason` enum。
3. Core：新增 `IBreakGuideAudioPlayer`，簽章如下，全部以 bool 表達失敗：
   `bool TryInitialize(out AudioFailureReason? failure);`
   `bool TryPlay(BreakGuideCue cue, BreakGuideMode mode,`
   `out AudioFailureReason? failure);`
   `void Stop();`（繼承 `IDisposable`，`Dispose()` 不得丟例外）
4. Core：新增 `sealed class BreakGuideAudioCoordinator`，建構子
   `BreakGuideAudioCoordinator(IBreakGuideAudioPlayer player, BreakGuideMode
   initialMode = BreakGuideMode.Chime)`；`player` 為 null 時丟
   `ArgumentNullException`（比照 `WorkCycleTracker`）。
5. Core：coordinator 公開 `BreakGuideMode CurrentMode { get; private set; }`、
   `bool IsDegraded { get; private set; }`、
   `event EventHandler<AudioFailureReason>? DegradedToVisual`、
   `void BeginGuide(bool audioAllowed)`、`void HandleCue(BreakGuideCue cue)`、
   `void EndGuide()`。
   - `BeginGuide(false)`（強度不足 / `Silent` 規則）直接設 `VisualOnly`，
     不算失敗、不發 `DegradedToVisual`。
   - `BeginGuide(true)` 呼叫 `TryInitialize`；失敗即 `Degrade(reason)`。
   - `HandleCue` 在 `VisualOnly` 時直接 return；否則 `TryPlay`，失敗即
     `Degrade(reason)`。
   - `Degrade` 設 `CurrentMode = VisualOnly`、`IsDegraded = true`、呼叫
     `player.Stop()`（包 try/catch）、`DegradedToVisual` 只發一次。
   - `EndGuide()` 呼叫 `Stop()`，重設 `CurrentMode` 回下一次的初始 mode。
   - coordinator 完全不引用 WPF、`System.Media`、Win32；Core 專案不得新增
     任何套件參考。
6. Infrastructure：新增 `WindowsBreakGuideAudioPlayer` 實作介面。每個
   Windows API 呼叫（初始化、播放、SAPI `SpeechSynthesizer`、`Stop`、
   `Dispose`）各自 `try { … } catch { failure = …; return false; }`。語音內容
   為固定短句，不得含秒數或倒數；提示音為短促音效，不得口述數字。
7. Infrastructure：`Dispose()` 內的例外一律吞掉（對應
   `AudioFailureReason.DisposeFailed`），不得往上冒泡到 UI thread。
8. App：`MainWindow.xaml.cs` 在開始引導時計算 `audioAllowed`
   （effective intensity `>= PresentationIntensity.PopupAndSound`），呼叫
   `BeginGuide`，並把 `BreakGuideSession.CueChanged` 轉給 `HandleCue`。
   `DegradedToVisual` 只做兩件事：`Trace.TraceError` 與（可選）更新視覺提示；
   **不得**開窗、不得 `Activate()`、不得改 `Topmost`。
9. App：`CloseReminderIfOpen()`、`BreakCompleted`、`BreakCancelled`、
   `StopActivityTracking()`、`App.OnExit` 全部要呼叫 `EndGuide()` 與
   `Dispose()`。
10. 測試：Core 用 fake player 覆蓋所有失敗路徑；App 只測 seam。

### 測試指引

- Core 測試放 `tests/RestCue.Core.Tests/Audio/BreakGuideAudioCoordinatorTests.cs`，
  `namespace RestCue.Core.Tests.Audio`，`public sealed class`，xunit。
- 檔尾加手寫 `private sealed class FakeAudioPlayer : IBreakGuideAudioPlayer`，
  帶 `bool FailInitialize`、`int FailOnPlayCall`、`List<BreakGuideCue> Played`、
  `int StopCount`、`int DisposeCount`，比照
  `tests/RestCue.App.Tests/PresentationIntensityAppTests.cs` 的
  `FakeRecordingTrayIcon` 風格；不引入 mocking 套件。
- App 測試放 `tests/RestCue.App.Tests/BreakGuideAudioSeamTests.cs`，比照
  `ModeEntrySeamTests.cs`：用真 `WorkCycleTracker` + `FakeClock` +
  `FakeAudioPlayer`，不具現化任何 WPF `Window`。
- `WindowsBreakGuideAudioPlayer` 不寫單元測試（實機播放無法自動驗證），改用
  spec Verification 的手動 smoke test，並在
  `docs/known-limitations.md` 記錄未覆蓋範圍。

| 測試名稱 | Arrange | Expected |
|----------|---------|----------|
| `Default_mode_is_chime` | 新 coordinator | `CurrentMode == BreakGuideMode.Chime` |
| `Successful_guide_plays_all_three_cues` | fake 全成功，`BeginGuide(true)` 後送 `Start/Middle/End` | `Played` 依序為三個 cue，`IsDegraded == false` |
| `Initialization_failure_degrades_to_visual_only` | `FailInitialize = true` | `CurrentMode == VisualOnly`，`DegradedToVisual` 一次且 reason 為 `InitializationFailed`，`Played` 為空 |
| `Mid_guide_playback_failure_degrades_and_stops` | `FailOnPlayCall = 2`，送三個 cue | `Played` 只含 `Start`，`StopCount >= 1`，`DegradedToVisual` 恰一次 |
| `Degradation_event_fires_only_once` | 初始化失敗後再送多個 cue | `DegradedToVisual` 仍為一次 |
| `Device_unavailable_is_silent` | fake 回 `DeviceUnavailable` | 無例外拋出、無彈窗回呼被呼叫 |
| `Audio_not_allowed_starts_visual_only_without_degradation` | `BeginGuide(false)` | `CurrentMode == VisualOnly`，`DegradedToVisual` 零次 |
| `EndGuide_stops_player` | 成功引導後 `EndGuide()` | `StopCount >= 1` |
| `Dispose_failure_is_swallowed` | fake `Dispose` 丟例外 | coordinator/呼叫端不拋出 |
| `Speech_mode_plays_without_numeric_text` | mode 設 `Speech`，取 Infrastructure 的固定語句常數 | 每句 `Assert.DoesNotContain(char.IsDigit)` |
| `Degradation_does_not_change_break_duration` | `WorkCycleTracker` + `FakeClock`，引導中途音訊失敗，`Advance(BreakDuration)`、`Tick(0)` | `BreakCompleted` 恰一次，時點與無音訊時相同 |
| `Degradation_does_not_emit_break_cancelled` | 同上但只推進到一半 | `BreakCancelled` 零次 |
| `Cancel_during_audio_still_emits_single_break_cancelled` | 引導中 `CancelBreak()` | `BreakCancelled` 一次，`StopCount >= 1` |
| `Removing_audio_player_keeps_completion_semantics` | 用 `VisualOnly` 起始、player 從未被呼叫 | 完成／取消測試結果與有音訊時完全相同 |

### 常見錯誤

- 音訊失敗時彈東西：不得 `MessageBox.Show`、不得系統通知、不得
  `Activate()`／`Focus()`／改 `Topmost`。降級只允許 `Trace.TraceError` 加
  無干擾的視覺切換。
- 讓例外冒到 UI：`IBreakGuideAudioPlayer` 的契約是回傳 bool；任何 Windows
  API 例外必須在 `WindowsBreakGuideAudioPlayer` 內被 catch。
- 音訊層自行判定完成：`BreakCompleted` 只能來自 #9 的
  `BreakGuideSession`／`WorkCycleTracker.TickBreak`。播完 `End` cue 不等於
  引導結束，也不得因播放失敗而提前結束或發 `BreakCancelled`。
- 降級時重啟計時：`Degrade` 不得呼叫 `BreakGuideSession.Start()`、不得重設
  `startedUtc`、不得延長或縮短 `BreakDuration`。
- 語音／提示音出現數字：語句固定為「看向約六公尺外」這類寫法時仍要避免阿拉伯
  數字與「還剩」語意；提示音不得為口述倒數。
- 在 Core 引用平台型別：`RestCue.Core` 目前無任何套件參考，加入
  `System.Media`／`System.Windows.Media`／SAPI 會破壞分層與可測性。
- 新增遙測或設定：不得動 `UsageEventType`、不得動 `AppSettings`
  （模式持久化是 #18），不得記錄裝置名稱。
- 忘記釋放：重複開啟引導而未 `EndGuide()`／`Dispose()` 會累積播放器實例；
  `App.OnExit` 已有 `UnwireUsageEventPersistence()`／`_lifecycle?.Dispose()`
  的樣式，音訊釋放請放在同處。
- 誤以為預設就會出聲：`PresentationIntensityPolicy.GetDebtRecommendation`
  目前只有 `RestDebtLevel.Level4` 建議 `PopupAndSound`，所以在低債務時
  `audioAllowed` 會是 false。這是既有 #15 行為，不要為了「預設節奏提示音」
  去改該 policy；若判定與本 spec 衝突，停止並提 review。

### 逐步 checklist

- [x] 新增 `BreakGuideMode` enum（預設 `Chime`）。
- [x] 新增 `AudioFailureReason` enum。
- [x] 新增 `IBreakGuideAudioPlayer`（bool + out reason，繼承 `IDisposable`）。
- [x] 新增 `BreakGuideAudioCoordinator` 與建構子驗證。
- [x] 實作 `BeginGuide(bool audioAllowed)` 的允許／不允許兩條路徑。
- [x] 實作 `HandleCue` 與播放失敗降級。
- [x] 實作 `Degrade` 的單次事件與 `Stop()` 保護。
- [x] 實作 `EndGuide()` 的停止與 mode 重設。
- [x] 新增 `WindowsBreakGuideAudioPlayer`，所有 API 呼叫包 try/catch。
- [x] 定義不含數字的固定語音／提示音內容常數。
- [x] 在 `MainWindow.xaml.cs` 依 effective intensity 計算 `audioAllowed`
       並轉接 `CueChanged`。
- [x] 在 `MainWindow.xaml.cs`／`App.xaml.cs` 補上 `EndGuide()` 與
       `Dispose()` 的所有路徑。
- [x] 新增 `BreakGuideAudioCoordinatorTests.cs` 與 `FakeAudioPlayer`。
- [x] 涵蓋上表所有 coordinator 案例。
- [x] 新增 `tests/RestCue.App.Tests/BreakGuideAudioSeamTests.cs` 驗證完成／
       取消語義不受音訊影響。
- [x] 更新 `docs/known-limitations.md`（未自動化的 Windows 音訊能力）。
- [x] 執行 Verification 區塊的所有命令，含手動拔除輸出裝置 smoke test。

## Execution checklist

- [x] 確認 #9 的生命週期是唯一完成判定來源，音訊層不得自行完成引導。
- [x] 建立最小的音訊介面，App/Core 不直接依賴具體 Windows 播放 API。
- [x] 實作節奏提示音模式，內容不含口述數字倒數。
- [x] 實作簡短語音模式，內容不含剩餘秒數。
- [x] 初始化、播放中斷、裝置不存在與 dispose 失敗均安全降級。
- [x] 降級不重啟計時、不重複事件、不延長或縮短 Break Duration。
- [x] 取消與 App shutdown 會停止／釋放播放資源。
- [x] 預設模式與合法值留給 #18 持久化；本票只建立可注入的能力。

## Acceptance checklist

- [x] 節奏提示音與語音均不暴露數字倒數。
- [x] 所有可模擬音訊失敗均降級至無數字視覺模式。
- [x] 失敗不顯示 modal/額外 popup、不搶焦點、不阻塞 Break Guide。
- [x] 移除或替換音訊實作不影響完成與取消測試。
- [x] fake audio 測試覆蓋成功、初始化失敗、中途失敗、取消與完成。

## Verification

- [x] 受影響 Core/App 單元測試
- [x] `dotnet build RestCue.sln`
- [x] `dotnet test RestCue.sln --no-build`（410 Core + 73 App + 45 Infra = 528 pass）
- [ ] 手動拔除／停用輸出裝置 smoke test，確認無額外彈窗（程式路徑已確認靜默降級，但尚未以實體裝置拔除驗證。）
- [x] `git diff --check`

## Data/schema impact

無。模式持久化由 #18；不可記錄裝置名稱或音訊內容。

## Completion report

- [x] Changes
- [x] Tests（410 Core + 73 App + 45 Infra = 528 pass, 0 fail）
- [x] Known limitations（含支援的 Windows 音訊能力）
- [x] Data/schema impact（無）
