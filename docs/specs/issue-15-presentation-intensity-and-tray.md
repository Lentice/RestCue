# Issue #15 — 套用呈現強度政策與可及系統列狀態

## Goal

以 `min(DebtRecommendedIntensity, ContextCap, UserChannelCap)` 決定允許通道，
並以不只依賴顏色的靜態 tray 微狀態顯示 Level 0–4。

## Dependencies and governing rules

- Blocked by #8、#14。
- #8、#14 已關閉；本票可直接使用 `RestDebtLevel` 與既有 fullscreen/application
  context，不得重新計算 Need。
- Debt 建議：Level 0 一般 tray；Level 1 靜態微狀態；Level 2 更明確的靜態
  icon/Tooltip；Level 3 可允許 edge popup；Level 4 可允許輕提示音 + popup。
- Effective intensity 永遠取 Debt 建議、Context cap、User channel cap 三者中最低者。
- Fullscreen 預設只允許靜態 tray；全螢幕聲音必須另行明確 opt-in，預設關閉。
- 通道強度必須是明確且可排序的 allow-list；較高強度包含較低通道，但 policy
  不執行呈現、Timing transition 或 Need reset。

## Scope

- Core 建立獨立 presentation-intensity policy。
- App 將有效 intensity 映射至 tray cue、邊緣 popup 與允許的輕提示音。
- Tray 使用靜態形狀／徽記或 Tooltip 文字區分 debt level。
- 最大等待只改 Timing，不提升 Intensity。
- 本票提供 user/context cap 的可測 seam 與安全預設，不新增設定畫面或持久化。

## Out of scope

- 重算 Need（#14）、設定 UI（#19）、持久化（#16）。
- 閃爍、呼吸、跳動、紅點、未讀計數或持續動畫。

## Execution checklist

- [x] 建立 Debt 建議、ContextCap、UserChannelCap 的明確 enum/value object。
- [x] 實作三者最低上限組合，不將 Timing 或 Need reset 混入 policy。
- [x] policy 對所有 enum 輸入做明確驗證；未知值不得意外提升呈現強度。
- [x] Level 1/2 僅允許靜態 tray；Level 3 才可允許邊緣 popup。
- [x] Level 4 只有在使用者允許且情境未靜音時 policy 才允許
  `PopupAndSound`；本票未新增實際音效資產，預設仍不播放聲音。
- [x] 全螢幕與 `TrayOnly` 將上限降為靜態 tray；`Silent` 不建立 popup/sound attempt
  （Pause/Focus Mode 由 phase 層級抑制，不屬於 intensity gate 的責任）。
- [x] 未知全螢幕狀態使用低干擾上限，不冒充 confirmed fullscreen。
- [x] Tray 對 Level 0–4、Paused、Focus Mode、Idle、Disabled 建立明確 view
  state；Level 以靜態形狀／徽記加可讀 Tooltip 文字區分，而非只換色。
- [x] Tooltip 包含狀態名稱與債務等級，不顯示倒數、未讀數字或敏感內容。
- [x] popup/sound 執行前使用同一 effective intensity 結果做最後 gate，避免
   最大等待、suppression release 或既有 `ReminderShown` wiring 繞過 cap。
- [x] level/context 變更以事件更新一次；平時不持續重繪。
- [x] tray 更新不得建立 modal/full-screen UI、呼叫 Activate 或改變鍵盤焦點。

## Acceptance checklist

- [x] 最大等待到期不讓 Level 1/2 越級顯示 popup（intensity gate 在 `EnterReminderVisible` 封鎖 EdgePopup 以下的通道）。
- [x] 全螢幕與 Silent 規則可抑制 popup，但 Need 持續存在。
- [x] Tray 的 Level 0–4 與 Disabled 不只靠顏色辨識（各 level 使用不同 SystemIcons 形狀 + Tooltip 文字含等級資訊）。
- [x] 沒有持續動畫、閃爍、紅點或焦點竊取（現有 tray 更新機制未改變）。
- [x] policy unit tests 覆蓋所有 level × context cap × user cap 關鍵組合。
- [x] App tests 覆蓋 production tray mapping、Tooltip、事件去重（GetStatusTextForDebtLevel、GetStatusTextForPhase 單元測試已加入）。
- [x] 測試證明 intensity 降級不改變 `RestDebtLevel`／`AccumulatedWorkTime`，
  且 Level 1/2 不會經 tracker 的 production presentation gate 顯示 popup
  或允許 sound。

## Verification

- [x] Core/App targeted tests（PresentationIntensityPolicyTests: 27 tests; PresentationIntensityAppTests: 17 tests）
- [x] `dotnet build RestCue.sln`（0 warnings, 0 errors）
- [x] `dotnet test RestCue.sln --no-build` — 415/415：333 Core、62 App、
  20 Infrastructure
- [ ] 手動鍵盤焦點與螢幕閱讀器／Tooltip smoke test（真實 Windows tray 行為無法自動化）
- [x] `git diff --check`

## Data/schema impact

無。本票未新增持久化、資料庫或 schema 變更。

## Completion report

- [x] Changes（src/: 7 files; tests/: 4 files; docs/: 1 file）
- [x] Tests（PresentationIntensityPolicyTests: 27 focused tests；
  PresentationIntensityAppTests: 17 focused tests；完整 solution 415/415）
- [x] Known limitations：非色彩辨識採用各 debt level 使用不同 SystemIcons
  （Information/Shield/Warning/Exclamation/Error）搭配 Tooltip 的 Level 文字。
  未自動化真實 NotifyIcon、鍵盤焦點與螢幕閱讀器 smoke test；實際輕提示音資產
  與 user-facing opt-in 尚未加入，因此目前只提供安全的 channel policy。
- [x] Data/schema impact（無）
