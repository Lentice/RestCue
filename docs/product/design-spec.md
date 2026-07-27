# RestCue MVP v1.1 design-spec reference

本專案以使用者提供的 `windows_eye_care_assistant_design_spec (1).md` 作為 MVP v1.1 正式開發基準。

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
- Idle Threshold 與 Passive Break Threshold 是獨立概念，且 Passive Break Threshold 必須小於等於 Idle Threshold。
- 前景程式名稱蒐集預設關閉；不得記錄 window title、輸入內容、剪貼簿、畫面或網站內容。
- 核心時間邏輯使用可替換 clock，並以單元測試驗證。
- MVP 不宣稱具有醫療診斷或治療效果。

## 完整原始規格

[Windows 護眼助理 Design Spec 與開發 Backlog（MVP v1.1）](windows-eye-care-assistant-design-spec-v1.1.md)

連結文件是納入版本控制的原始 MVP v1.1 開發基準。若其中的技術版本、產品命名、隱私規則或其他內容與本頁「核心契約」或根目錄 `AGENTS.md` 衝突，以本頁核心契約及 `AGENTS.md` 為準。
