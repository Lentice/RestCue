# MVP v1.3 Review Backlog

> 產生日期：2026-07-29
> 基準版本：v1.3.0.0 (commit 45fca87)
> 審查範圍：Design Spec v1.3 對照實作完整性 + Code Review

---

## Tier 1 — 重大問題 (P0，必須修正才能發布)

### B01 — BreakGuideMode 雙重列舉未連線，使用者選擇的音訊模式完全無效

- **檔案**：`src/RestCue.Core/Reminders/BreakGuideMode.cs` (Chime/Speech/VisualOnly) vs `src/RestCue.Core/Settings/BreakGuideMode.cs` (Cue/Voice/NumberlessVisual)
- **問題**：兩個同名列舉，值不同，沒有任何對應邏輯。`AppSettings.BreakGuideMode` 儲存使用者選擇但 `BreakGuideAudioCoordinator` 永遠以 `Chime` 初始化（`MainWindow.xaml.cs:446`），導致使用者在設定頁選擇的音訊模式完全無效。
- **影響**：FR-006 的節奏提示音／語音／無數字視覺三種模式切換功能形同虛設。
- **建議修正**：新增 `Reminders.BreakGuideMode` ← `Settings.BreakGuideMode` 的 mapping 函式（Cue→Chime, Voice→Speech, NumberlessVisual→VisualOnly），於 `SetupBreakGuideSession()` 傳入正確的 initialMode。

### B02 — ReminderWindow.StopBreakGuide() 清除動畫時使用了錯誤的 DependencyProperty

- **檔案**：`src/RestCue.App/ReminderWindow.xaml.cs:82`
- **問題**：`StartBreakGuide()` 在 `UIElement.OpacityProperty` 上啟動動畫，但 `StopBreakGuide()` 對 `SolidColorBrush.OpacityProperty` 呼叫 `BeginAnimation(null)`——兩者是不同的 DependencyProperty，動畫無法被正確停止。
- **影響**：關閉休息引導視窗後 WPF 動畫資源可能洩漏。
- **建議修正**：將第 82 行的 `System.Windows.Media.SolidColorBrush.OpacityProperty` 改為 `System.Windows.UIElement.OpacityProperty`。

### B03 — DailyStatisticsService 將 Focus Mode 期間從有效工作時間排除（與 spec 5.6 矛盾）

- **檔案**：`src/RestCue.Core/UsageEvents/DailyStatisticsService.cs:137-143`
- **問題**：`FocusModeStarted` 呼叫 `HandleAccStop` 停止累積，`FocusModeEnded` 才恢復。但 spec 5.6 明確規定 Focus Mode「持續累積」有效工作時間。WorkCycleTracker 執行時期正確累積，但統計報表將 Focus Mode 時間排除，導致每日統計的有效工作時間低報。
- **影響**：FR-010 統計數據不正確。使用者專注模式期間的工作時間不會出現在統計中。
- **建議修正**：移除 `FocusModeStarted`／`FocusModeEnded` 的 `HandleAccStop`／`HandleAccStart` 處理，改為直接 `break`（類似 `CooldownStarted`／`CooldownEnded` 不影響累積）。

### B04 — Focus Mode 無自動結束計時器（spec 預設 60 分鐘）

- **檔案**：`src/RestCue.Core/Reminders/WorkCycleTracker.cs:394-419`
- **問題**：`StartFocusMode()` 僅切換 phase 為 `FocusMode`，無任何計時器。Spec FR-008 明確規定「專注模式預設 60 分鐘」且期滿後應自動恢復。
- **影響**：Focus Mode 為手動無限期，不符合 spec。使用者若忘記手動結束，會無限延期休息提醒。
- **建議修正**：於 `StartFocusMode()` 記錄 `focusModeUntilUtc = now + focusModeDuration`，於 `Tick()` 中檢查到期自動呼叫 `EndFocusMode()`。

### B05 — Pause 無計時自動恢復（spec 要求 15/30/60 分鐘選項）

- **檔案**：`src/RestCue.App/Lifecycle/WindowsTrayIcon.cs`、`WorkCycleTracker.cs`
- **問題**：Pause 僅為手動 toggle，spec FR-008 要求「可選 15 分鐘、30 分鐘、1 小時或直到手動恢復」。
- **影響**：缺少符合 spec 的暫停時間選項。
- **建議修正**：於系統列選單增加 Pause 子選單（15/30/60 分鐘／手動恢復），`WorkCycleTracker.Pause()` 接受 `TimeSpan?`，於 `Tick()` 中檢查到期自動 `Resume()`。

---

## Tier 2 — 應該修正 (P1，影響功能完整性 / 程式品質)

### B06 — IFullscreenDetector 介面孤立，無 Infrastructure 實作

- **檔案**：`src/RestCue.Core/Activity/IFullscreenDetector.cs`
- **問題**：Core 層定義了 `IFullscreenDetector`，但沒有任何 Infrastructure 層實作，也沒有被注入到任何地方。實際全螢幕偵測嵌入在 `WindowsForegroundContextProvider.IsWindowFullscreen()` 中，無法獨立單元測試。
- **影響**：全螢幕偵測邏輯無法透過 fake 進行獨立測試，違反 spec NFR-004「Windows 狀態偵測必須有 Interface，可用 Fake 實作測試」。
- **建議修正**：從 `WindowsForegroundContextProvider` 抽取 `IsWindowFullscreen()` 到獨立的 `WindowsFullscreenDetector : IFullscreenDetector`，並透過 DI 注入。

### B07 — missing `.github/workflows/package.yml`

- **檔案**：`.github/workflows/package.yml`
- **問題**：Spec #24 的交付清單包含此檔案，但 repo 中不存在。`ci.yml` 存在且正常運作。
- **影響**：CI 無法自動建置安裝包。
- **建議修正**：建立 `package.yml`，包含 restore → build → test → publish → Inno Setup → SHA-256。

### B08 — 安裝／升級／移除全情境未測試（全部 BLOCKED）

- **檔案**：`docs/testing/windows-install-upgrade-verification.md`
- **問題**：Clean install、upgrade、repair、failed-upgrade recovery、uninstall、downgrade rejection 六個情境全部標記為 BLOCKED，需要乾淨 Windows 10/11 環境。
- **影響**：安裝套件無法驗證實際可用性。
- **建議修正**：在乾淨 Windows VM 中執行所有情境並記錄證據。

### B09 — 系統列圖示 Level 3 與 Suppressed 共用相同圖示

- **檔案**：`src/RestCue.App/Lifecycle/WindowsTrayIcon.cs:127-133`
- **問題**：`Level3Icon` 和 `SuppressedIcon` 都對應 `SystemIcons.Exclamation`。當債務達 Level 3 且同時被情境規則抑制時，使用者在視覺上無法區分。
- **影響**：Spec NFR-005 要求「不只用顏色表示狀態」；Level 3 與 suppressed 的混淆違反這個原則。
- **建議修正**：為 Level 3 選用一個不同於 `Exclamation` 的獨特圖示。

### B10 — ReminderState.BreakGuide 為未使用的舊列舉

- **檔案**：`src/RestCue.Core/Reminders/ReminderState.cs`
- **問題**：`ReminderState` 列舉包含 `BreakGuide` 成員，但從未被參考。Spec #9 明確警告「勿據此建流程」。
- **影響**：Dead code，可能誤導開發者。
- **建議修正**：移除 `BreakGuide` 成員，或加上 `[Obsolete]`。

### B11 — known-limitations.md 內容過時

- **檔案**：`docs/known-limitations.md:5`
- **問題**：第 5 行列為「尚未實作」的 session lock/power events 實際上已在 `MainWindow.xaml.cs` 中透過 `SystemEvents.SessionSwitch` / `PowerModeChanged` 完整實作。
- **影響**：文件與程式碼不一致。
- **建議修正**：將第 5 行中的「session lock 與 power events」從未實作清單移除。

### B12 — 缺少各程式累積使用時間統計

- **檔案**：`src/RestCue.Core/UsageEvents/DailyStatistics.cs`、`src/RestCue.App/StatisticsWindow.xaml`
- **問題**：FR-010 要求「各程式的累積使用時間，可選擇關閉」，但 `DailyStatistics` 無 per-app 欄位，`StatisticsWindow` 無對應 UI。
- **影響**：統計功能不完整，使用者無法看到各應用程式的使用時間分佈。
- **備註**：此功能依賴前景程式名稱蒐集開啟（預設關閉），因此只有在使用者 opt-in 後才有資料來源。

### B13 — AboutWindow 缺少連結至資料透明檢視

- **檔案**：`src/RestCue.App/AboutWindow.xaml`
- **問題**：G-003 spec 要求 About/Privacy 頁面應有「連結至資料透明檢視（FR-012）」，但目前 AboutWindow 沒有可點擊的連結或按鈕前往 TransparencyWindow。
- **影響**：使用者無法從隱私說明頁直接前往資料透明檢視，降低信任建立路徑。
- **建議修正**：於 AboutWindow 隱私區塊新增「查看目前蒐集的資料」按鈕。

---

## Tier 3 — 有時間再修正 (P2，改善體驗 / 技術債)

### B14 — 清除資料後未執行 VACUUM

- **檔案**：`src/RestCue.Infrastructure/UsageEvents/SqliteUsageDataMaintenance.cs`
- **問題**：`ClearUsageHistoryAsync()` 只做 `DELETE FROM usage_events`，不執行 `VACUUM`。-wal / -shm 檔案可能殘留。
- **影響**：資料庫檔案佔用空間不回收。（已在 known-limitations 記錄）
- **建議修正**：清除後可選執行 `VACUUM`；失敗不影響清除結果。

### B15 — ReminderWindow 英文文字含數字 "6"

- **檔案**：`src/RestCue.App/ReminderWindow.xaml.cs:38`
- **問題**：`ShowReminder()` 顯示 "Look at something\n6 meters away." 含阿拉伯數字 "6"。Spec FR-006 的核心原則是去數字化。中文版使用「六公尺外」（中文字元）則合規。
- **影響**：英文語系下的顯示違反去數字化原則（極輕微）。
- **建議修正**：改為 "six meters away"。

### B16 — Snooze 按鈕不顯示延後時間

- **檔案**：`src/RestCue.App/ReminderWindow.xaml.cs:40`
- **問題**：Snooze 按鈕固定顯示 "Snooze"，而 `SnoozeDuration` property 已設定但從未顯示。使用者無法從按鈕得知延後多久。
- **影響**：UX 可改善。
- **建議修正**：按鈕文字顯示 "延後 {n} 分鐘"。

### B17 — 設定重設後 WindowsForegroundContextProvider 未重建

- **檔案**：`src/RestCue.App/App.xaml.cs:139-143`
- **問題**：`OnSettingsReset` 只更新 `_startup.CurrentSettings`，但 `WindowsForegroundContextProvider` 仍使用舊的 `CollectForegroundProcessNames` 值，需下次啟動才生效。已在 known-limitations 記錄。
- **建議修正**：於 App 層重建 foreground context provider 並重新注入（或維持既有「下次啟動生效」設計，視產品決策）。

### B18 — ReminderWindow 多螢幕定位未實作

- **檔案**：`src/RestCue.App/ReminderWindow.xaml.cs:98-103`
- **問題**：`PositionOnPrimaryScreenRightEdge()` 永遠使用 `SystemParameters.WorkArea`（主螢幕）。Spec FR-005 要求「預設顯示於目前前景視窗所在螢幕」。
- **影響**：多螢幕使用者體驗不佳。（已在 known-limitations 記錄）
- **建議修正**：使用 `MonitorFromWindow` API 根據前景視窗所在螢幕定位，或於設定提供使用者選擇。

### B19 — 暫無 per-app 使用時間的 UI

（見 B12，歸類於 Tier 3 補充：若 B12 的資料層補上後，StatisticsWindow 需新增對應 UI 區塊。）

---

## 附錄：spec 對照檢查摘要

| Spec 需求 | 狀態 | 相關 Ticket |
|---|---|---|
| FR-001 系統列常駐 | 已實作 | — |
| FR-002 有效工作時間偵測 | 已實作 | — |
| FR-003 提醒狀態機 | 已實作 | — |
| FR-004 自然停頓提醒 | 已實作 | — |
| FR-004a 休息債務分級呈現 | 已實作 | — |
| FR-005 非阻擋式邊緣提醒 | 已實作（僅主螢幕） | B18 |
| FR-006 休息引導（去數字化） | 部分實作（音訊模式無效） | B01, B15 |
| FR-007 延後/忽略/逾時 | 已實作 | — |
| FR-008 暫停與專注模式 | 部分實作（缺計時器） | B04, B05 |
| FR-009 情境規則 | 已實作 | — |
| FR-010 每日統計 | 部分實作（缺 per-app；FocusMode bug） | B03, B12 |
| FR-011 設定 | 已實作 | — |
| FR-012 資料透明檢視 | 已實作 | — |
| NFR-001 效能 | 已驗證（soak tests） | — |
| NFR-002 穩定性 | 已實作 | — |
| NFR-003 隱私 | 已實作 | — |
| NFR-004 可測試性 | 部分（IFullscreenDetector 孤立） | B06 |
| NFR-005 無障礙 | 部分（圖示混淆） | B09 |
| G-001 效能測試 | 已實作 | — |
| G-002 安裝包 | 部分（未測試） | B07, B08 |
| G-003 Privacy Notice | 部分（缺連結） | B13 |
| G-004 Dogfooding | 進行中（Day 1 only） | — |
