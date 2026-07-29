# Issue #9 — 完成無數字視覺 Break Guide

## Goal

提供不搶焦點、不中斷輸入的 Break Guide。只有完整走完設定時長才產生
`BreakCompleted`；任何提前結束都產生 `BreakCancelled`，且不得清除休息需求。

## Dependencies and governing rules

- Blocked by GitHub #4、#6；開工前確認兩者已關閉。
- 預設引導時間 20 秒，可設定範圍 10–60 秒；值來自 Core settings。
- 引導開始、中段與完成皆不得以數字、百分比或倒數表達。
- 若 Break Guide 完成語義仍無 ADR，新增一份 ADR，記錄去數字化、取消與降級決策。

## Scope

- Core：建立可由 `IClock` 驅動的開始、完成、取消流程與領域事件。
- App：提供極簡、無數字的視覺進度與明確取消操作。
- App 可顯示文字引導，但不得顯示剩餘秒數、百分比或環形數字刻度。
- 完成／取消接回既有 `WorkCycleTracker`，維持單一提醒嘗試。

## Out of scope

- 音效、語音與 Spatial Audio（#10）。
- 使用事件持久化（#16）、統計（#17）、設定 UI（#19）。
- 全域鍵盤 hook、滑鼠移動推論、遮罩、降黑、模糊或 modal/full-screen UI。

## Implementation guidance for agents

#11–#16 已關閉，`WorkCycleTracker`、`PresentationIntensity` 與 usage event
持久化皆已上線。請在既有程式上修改，不要另建平行的計時或事件流程。

### 檔案地圖

| 路徑 | 新增/修改 | 這裡要改什麼 |
|------|-----------|--------------|
| `src/RestCue.Core/Reminders/BreakGuidePhase.cs` | 新增 | `enum BreakGuidePhase { NotStarted, Running, Completed, Cancelled }`。 |
| `src/RestCue.Core/Reminders/BreakGuideCue.cs` | 新增 | `enum BreakGuideCue { Start, Middle, End }`，代表無數字文字提示的三個節點。 |
| `src/RestCue.Core/Reminders/BreakGuideSession.cs` | 新增 | 由 `IClock` 驅動的引導狀態機；`Start()`／`Cancel()`／`Tick()`；`Completed`／`Cancelled` 各只發一次。 |
| `src/RestCue.Core/Reminders/WorkCycleTracker.cs` | 修改 | 新增 `public void CancelBreak()`，只在 `WorkCyclePhase.BreakInProgress` 生效，發一次 `BreakCancelled`，**不呼叫** `ResetCycle()`。 |
| `src/RestCue.App/ReminderWindow.xaml` | 修改 | 移除 `CountdownText` TextBlock；改放無刻度視覺指示（例如無 `Text` 的色帶／`Ellipse` 淡入淡出）與「結束休息」按鈕。 |
| `src/RestCue.App/ReminderWindow.xaml.cs` | 修改 | 刪除 `countdownSeconds`、`OnCountdownTick` 的 `$"{n}s"` 與 `"Done!"`；`ShowReminder()` 的 `ActionButton.Content` 不得含秒數；新增 `event EventHandler? CancelRequested`。 |
| `src/RestCue.App/MainWindow.xaml.cs` | 修改 | 在 `OnReminderShown`／`StartBreakNow` 內接上 `CancelRequested` → `WorkCycleTracker.CancelBreak()`；`CloseReminderIfOpen()` 於 `BreakInProgress` 時先呼叫 `CancelBreak()`。 |
| `docs/adr/0006-break-guide-completion-semantics.md` | 新增 | spec 要求的 ADR：去數字化、取消語義、降級決策。沿用 `docs/adr/README.md` 的四段格式與四位數編號。 |
| `tests/RestCue.Core.Tests/Reminders/BreakGuideSessionTests.cs` | 新增 | fake clock 生命週期／競態測試。 |
| `tests/RestCue.App.Tests/BreakGuideSeamTests.cs` | 新增 | 取消／完成 wiring 與「文案不含數字」測試。 |

### 可重用的既有型別

- `RestCue.Core.Time.IClock`（`src/RestCue.Core/Time/IClock.cs`）：唯一時間
  來源，只有 `UtcNow`。生產環境用
  `src/RestCue.Infrastructure/Time/SystemClock.cs`。
- `RestCue.Core.Reminders.WorkCyclePhase`：已有 `BreakInProgress`，不要再
  新增休息用的 phase 值。
- `WorkCycleTracker` 既有成員直接沿用：`StartBreak()`、`ManualStartBreak()`、
  `BreakDuration`、`BreakStarted`／`BreakCompleted`／`BreakCancelled` 事件、
  `TickBreak` 的到期判定。
- `RestCue.Core.Settings.AppSettings.BreakDuration`（預設 20 秒）與
  `AppSettingsValidator` 已有 10–60 秒範圍檢查；**不要**新增設定欄位或
  重複驗證。
- `UsageEventType.BreakCompleted`／`BreakCancelled` 與 `App.xaml.cs` 的
  `OnBreakCompletedEvent`／`OnBreakCancelledEvent` 已把事件寫入
  `BackgroundUsageEventWriter`；本票不需碰持久化。
- 不要建立的東西：新的 clock 抽象、新的 `ReminderState` 值（
  `src/RestCue.Core/Reminders/ReminderState.cs` 的 `BreakGuide` 是未使用的
  舊列舉，勿據此建流程）、`DispatcherTimer` 以外的第二套 UI 計時器、
  任何 modal／全螢幕遮罩視窗、任何鍵鼠 hook。

### 實作順序

1. Core：新增 `BreakGuidePhase` 與 `BreakGuideCue` 兩個 enum，各自一個檔案。
2. Core：新增 `sealed class BreakGuideSession`，建構子
   `BreakGuideSession(IClock clock, TimeSpan duration)`，`duration <=
   TimeSpan.Zero` 時丟 `ArgumentOutOfRangeException`（比照
   `WorkCycleTracker.ValidateThreshold`）。
3. Core：`BreakGuideSession` 公開 `BreakGuidePhase Phase { get; private
   set; }`、`void Start()`、`void Cancel()`、`void Tick()`、
   `event EventHandler? Completed`、`event EventHandler? Cancelled`、
   `event EventHandler<BreakGuideCue>? CueChanged`。
   - `Start()` 在非 `NotStarted` 時直接 return（idempotent），記錄
     `startedUtc = clock.UtcNow`，發 `CueChanged(Start)`。
   - `Tick()` 在 `Running` 時計算 `clock.UtcNow - startedUtc`；跨過
     `duration / 2` 發一次 `CueChanged(Middle)`；`>= duration` 時設
     `Completed`、發 `CueChanged(End)` 與 `Completed`。
   - `Cancel()` 只在 `Running` 生效，設 `Cancelled` 並發一次 `Cancelled`。
   - `Completed` 之後的 `Cancel()`／`Cancelled` 之後的 `Tick()` 皆為 no-op。
   - 內部不得回傳或格式化任何剩餘時間。
4. Core：在 `WorkCycleTracker` 新增 `CancelBreak()`。實作：非
   `BreakInProgress` 直接 return；否則 `CurrentPhase =
   WorkCyclePhase.ReminderVisible` 之外的處理請保持最小 —— 設
   `CurrentPhase = WorkCyclePhase.Working`、`breakStartUtc = null`，發一次
   `BreakCancelled`，且**不觸碰** `AccumulatedWorkTime`、`restDebtLevel`、
   `cooldownUntil`。
5. Core：不要改 `TickBreak`／`TickActivityUnavailable` 的完成判定；完成仍
   走既有 `ResetCycle()` + `BreakCompleted`（這是「可信重設 Need」的路徑）。
6. App（抽象縫）：本票沒有 Win32/媒體 API 需要包裝，唯一需要的縫是把
   「文案產生」從 WPF 拉到可測位置 —— 在
   `src/RestCue.Core/Reminders/BreakGuideCue.cs` 同 namespace 加
   `static class BreakGuideText { public static string ForCue(BreakGuideCue
   cue); }`，回傳固定中文提示（開始：看向約 6 公尺外；中段：慢慢眨眼、
   放鬆肩膀；完成：休息結束）。WPF 只負責顯示。
   `ReminderWindow.OnSourceInitialized` 既有的 `WS_EX_NOACTIVATE |
   WS_EX_TOOLWINDOW` 就是不搶焦點的保證，維持原樣不要改。
7. App：改 `ReminderWindow.xaml` 移除 `CountdownText`，加入無刻度視覺元素與
   `x:Name="CancelButton"`（Content 固定「結束休息」，不含秒數）。
8. App：改 `ReminderWindow.xaml.cs`。`StartBreakCountdown()` 更名為
   `StartBreakGuide()`；內部只設定 `PhaseText.Text =
   BreakGuideText.ForCue(BreakGuideCue.Start)` 並啟動既有
   `DispatcherTimer`，Tick 內僅呼叫外部注入的 `Action` 或更新視覺，不寫任何
   數字。新增 `OnCancelButtonClick` → `CancelRequested`。
9. App：改 `MainWindow.xaml.cs`。`OnBreakRequested` 改叫
   `StartBreakGuide()`；新增 `OnCancelRequested` handler 呼叫
   `workCycleTracker?.CancelBreak()` 後 `CloseReminderIfOpen()`；
   `CloseReminderIfOpen()` 內若 `workCycleTracker?.CurrentPhase ==
   WorkCyclePhase.BreakInProgress` 先 `CancelBreak()`，避免關窗漏發事件。
10. 文件：新增 ADR-0006，並在 `docs/known-limitations.md` 記錄「視覺引導的
    進度呈現不精確，因為刻意不顯示剩餘時間」。
11. 測試：先寫 Core 測試，再寫 App seam 測試（見下）。

### 測試指引

- Core 測試放 `tests/RestCue.Core.Tests/Reminders/BreakGuideSessionTests.cs`，
  `namespace RestCue.Core.Tests.Reminders`，`public sealed class`，xunit
  `[Fact]`／`[Theory]`。照現有慣例在檔案尾端加
  `private sealed class FakeClock : IClock`（帶 `Advance(TimeSpan)`），
  比照 `WorkCycleTrackerTests.cs`；不要引入 mocking 套件。
- `CancelBreak()` 的測試加在既有
  `tests/RestCue.Core.Tests/Reminders/WorkCycleTrackerTests.cs`，重用該檔的
  `CreateTracker` helper 與 `Default*` 常數。
- App 測試放 `tests/RestCue.App.Tests/BreakGuideSeamTests.cs`，比照
  `ModeEntrySeamTests.cs`：測試 `static`／可獨立呼叫的邏輯，**不要**具現化
  `ReminderWindow`（WPF 視窗在測試 host 中無法可靠建立）。

| 測試名稱 | Arrange | Expected |
|----------|---------|----------|
| `Start_enters_Running_and_emits_start_cue` | `FakeClock`、20 秒 session | `Phase == Running`，`CueChanged` 收到一次 `Start` |
| `Tick_before_duration_does_not_complete` | Start 後 `Advance(19s)`、`Tick()` | 無 `Completed`，`Phase == Running` |
| `Tick_at_exact_duration_completes_once` | Start 後 `Advance(20s)`、`Tick()` 兩次 | `Completed` 恰一次，`Phase == Completed` |
| `Tick_after_duration_does_not_recomplete` | 完成後 `Advance(10s)`、`Tick()` | `Completed` 仍為一次 |
| `Middle_cue_emitted_once_at_half_duration` | `Advance(10s)`、`Tick()` 三次 | `Middle` 恰一次 |
| `Cancel_before_duration_emits_cancelled_once` | Start 後 `Advance(5s)`、`Cancel()` 兩次 | `Cancelled` 恰一次，無 `Completed` |
| `Cancel_after_completion_is_noop` | 完成後 `Cancel()` | 無 `Cancelled`，`Phase == Completed` |
| `Start_is_idempotent` | `Start()` 兩次 | `Start` cue 恰一次，`startedUtc` 不被重設 |
| `Text_for_all_cues_contains_no_digits` | 對三個 `BreakGuideCue` 呼叫 `BreakGuideText.ForCue` | 每個結果 `Assert.DoesNotContain(char.IsDigit)`（含全形數字亦不可） |
| `CancelBreak_preserves_accumulated_work_time` | `WorkCycleTracker` 累積工作→`ManualStartBreak()`→`CancelBreak()` | `BreakCancelled` 一次，`AccumulatedWorkTime` 與取消前相同 |
| `CancelBreak_outside_break_is_noop` | `Working` phase 下 `CancelBreak()` | 無事件、無例外、phase 不變 |
| `Break_completion_still_resets_need` | `ManualStartBreak()`→`Advance(BreakDuration)`→`Tick(0)` | `BreakCompleted` 一次，`AccumulatedWorkTime == TimeSpan.Zero` |

- 假造依賴一律用手寫 `private sealed class Fake…`（`FakeClock`、
  `FakeRecordingTrayIcon` 已是既有範例），並用計數器 `int count` +
  `Assert.Equal(1, count)` 驗證「只發一次」。

### 常見錯誤

- 在任何字串中留下數字：`ReminderWindow.ShowReminder()` 現況會產生
  `"Start Break (20s)"`、`"Snooze 5min"`，`StartBreakCountdown()` 會產生
  `"20s"`／`"Done!"`。引導期間（`BreakInProgress`）的 UI 一律不得含數字、
  百分比或「剩餘」字樣；`ProgressBar` 也不得顯示數值。
- 用 modal 或遮罩：不要 `ShowDialog()`、不要全螢幕視窗、不要降黑／模糊。
  維持 `ReminderWindow.xaml` 既有的 `ShowInTaskbar="False"`、
  `WindowStyle="None"`、右緣定位。
- 用輸入行為推論完成：不要因 `passiveBreakThreshold`／idle 判定就宣告
  `BreakCompleted`。完成只能來自 `IClock` 走完 `BreakDuration`。
- 提前結束沒發 `BreakCancelled`：目前 `CloseReminderIfOpen()` 只是關窗，
  `Pause()`／`StartFocusMode()`／`Disable()`／`StartBreakNow()` 都會呼叫它。
  補上取消後要確認每條路徑只發一次事件、不重複。
- 取消時重設休息需求：`ResetCycle()` 會把 `AccumulatedWorkTime` 歸零並清掉
  `restDebtLevel`，`CancelBreak()` 絕不可呼叫它。
- 誤改 `HandleUnlock()`／`HandleResume()`／`EnterIdle()`：這三條路徑目前是
  「先發 `BreakCancelled` 再 `ResetCycle()`」，屬於 lock/sleep/idle 的既有
  可信重設語義，本票不要為了「取消不重設」而改動它們；若認定衝突，停止
  並提 review。
- 在 UI 層自行持有 `BreakDuration` 數值或用 `DateTime.UtcNow`：時間值只能
  由 `AppSettings` → `WorkCycleTracker.BreakDuration` → `BreakGuideSession`
  傳遞，時間只能由 `IClock` 讀取。
- 忘記 dispose／stop `DispatcherTimer`：`CloseReminderIfOpen()` 與 App 結束
  時都要停掉，否則重複開窗會有兩個 timer 同時推進。

### 逐步 checklist

- [x] 新增 `BreakGuidePhase` enum。
- [x] 新增 `BreakGuideCue` enum 與 `BreakGuideText.ForCue`。
- [x] 新增 `BreakGuideSession`（建構子驗證 + `Phase` 屬性）。
- [x] 實作 `Start()` idempotent 與 `CueChanged(Start)`。
- [x] 實作 `Tick()` 的中段 cue 與精確到期單次 `Completed`。
- [x] 實作 `Cancel()` 單次 `Cancelled` 與完成後 no-op。
- [x] 在 `WorkCycleTracker` 新增 `CancelBreak()`，確認不動
      `AccumulatedWorkTime`。
- [x] 移除 `ReminderWindow.xaml` 的 `CountdownText`，加入無刻度視覺與
       取消按鈕。
- [x] 清除 `ReminderWindow.xaml.cs` 內所有秒數字串，改用
      `BreakGuideText`。
- [x] 在 `ReminderWindow` 新增 `CancelRequested` 事件與 click handler。
- [x] 在 `MainWindow.xaml.cs` wire `CancelRequested` 與
      `CloseReminderIfOpen()` 的取消路徑。
- [x] 新增 `docs/adr/0006-break-guide-completion-semantics.md`。
- [x] 更新 `docs/known-limitations.md`。
- [x] 新增 `BreakGuideSessionTests.cs` 並涵蓋上表所有 session 案例。
- [x] 在 `WorkCycleTrackerTests.cs` 補 `CancelBreak` 三個案例。
- [x] 新增 `tests/RestCue.App.Tests/BreakGuideSeamTests.cs` 涵蓋文案無數字
       與 wiring。
- [x] 執行 Verification 區塊的所有命令並確認全綠。

## Execution checklist

- [x] 讀取 GitHub Issue #9 全文與 comments，確認 blockers 已關閉。
- [x] 定義 Break Guide 的 `NotStarted/Running/Completed/Cancelled` 最小狀態與合法轉移。
- [x] 所有完成判定使用注入的 `IClock`；UI 不自行擁有 timing 值。
- [x] 完整達 `BreakDuration` 時只發出一次 `BreakCompleted` 並可信重設 Need。
- [x] 使用者提前關閉、取消或 App 結束引導時只發出一次 `BreakCancelled`，不重設 Need。
- [x] 建立無數字視覺呈現與明確取消按鈕；視窗不啟用、不搶焦點、不阻擋輸入。
- [x] 不以任意鍵鼠活動推論完成或取消。
- [x] 視窗重複開啟、重複取消與完成／取消競態均具 idempotent 行為。
- [x] 更新必要的架構、已知限制或操作文件。

## Acceptance checklist

- [x] 整個流程沒有數字倒數、剩餘時間或百分比。
- [x] 精確到期才記為 `BreakCompleted` 並清除休息需求。
- [x] 到期前任何結束均為 `BreakCancelled`，休息需求保持不變。
- [x] 原前景視窗與鍵盤焦點不改變，滑鼠／鍵盤輸入不被封鎖。
- [x] fake-clock 測試覆蓋到期前、精確到期、到期後、重複操作與競態。
- [x] WPF 層測試覆蓋視窗生命週期與事件 wiring。

## Verification

- [x] `dotnet test tests/RestCue.Core.Tests/RestCue.Core.Tests.csproj` (395 pass)
- [x] `dotnet test tests/RestCue.App.Tests/RestCue.App.Tests.csproj` (70 pass)
- [x] `dotnet build RestCue.sln`
- [x] `dotnet test RestCue.sln --no-build` (510 pass)
- [x] `git diff --check`

## Data/schema impact

無。本票只新增記憶體內語義與 UI；事件持久化由 #16 處理。

## Completion report

- [x] Changes
- [x] Tests（395 Core + 70 App = 510 total, 0 failed）
- [x] Known limitations
- [x] Data/schema impact（無）
