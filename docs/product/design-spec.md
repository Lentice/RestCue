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

## 原始規格

目前原始規格位於工作站：

`D:\Downloads\windows_eye_care_assistant_design_spec (1).md`

在首次正式發版前，應將經確認的完整規格納入版本控制，並更新本文件連結。
