# RestCue MVP v1.3 design-spec reference

本專案以使用者提供的 `windows-eye-care-assistant-design-spec-v1.3.md` 作為 MVP v1.3 正式開發基準。

## Product identity

- 專案代號與 App 名稱：RestCue
- 完整英文名稱：RestCue – Eye Break Reminder
- 中文名稱：RestCue 護眼助理
- 平台：Windows 10 / 11
- 核心理念：A gentle cue to rest your eyes.

## 核心契約

- Windows 10/11、C#、.NET 10 LTS、WPF、本機 SQLite。
- 提醒不得搶焦點、封鎖輸入、使用全螢幕遮罩或 modal dialog。
- 使用者可開始休息、延後、忽略、關閉提醒；無回應須記為 `AutoDismissed`。
- 休息需求、提醒時機與呈現強度是獨立決策；短暫無輸入只能記為 `PassivePauseDetected`，不得視為完成休息或重設休息需求。
- Idle Threshold 與 Passive Pause Threshold 是獨立概念，且 Passive Pause Threshold 必須小於 Idle Threshold；只有 `BreakCompleted` 或進入 `Idle` 才能重設休息需求。
- Pause 停止有效工作時間與休息債務累積；Focus Mode 持續累積，但抑制主動提醒。
- 提醒重試冷卻不會重設休息需求時鐘；系統列債務微狀態必須有非色彩的可辨識提示。
- 前景程式名稱蒐集預設關閉；不得記錄 window title、輸入內容、剪貼簿、畫面或網站內容。
- 核心時間邏輯使用可替換 clock，並以單元測試驗證。
- MVP 不宣稱具有醫療診斷或治療效果。

## 完整原始規格

[Windows 護眼助理 Design Spec 與開發 Backlog（MVP v1.3）](windows-eye-care-assistant-design-spec-v1.3.md)

連結文件是納入版本控制的原始 MVP v1.3 開發基準。已將其技術版本對齊專案既定的 .NET 10 LTS；若其他內容與本頁「核心契約」或根目錄 `AGENTS.md` 衝突，以本頁核心契約及 `AGENTS.md` 為準。
