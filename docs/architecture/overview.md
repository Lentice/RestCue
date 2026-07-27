# Architecture overview

RestCue 採三層單一應用程式結構：

- **Core**：提醒狀態、工作週期、設定規則與統計語意。不得依賴 WPF、Win32 或 SQLite。
- **Infrastructure**：Win32 活動偵測、session/power events、SQLite、檔案設定與系統啟動。
- **App**：WPF UI、system tray 與 dependency composition。

跨層依賴只朝向 Core。時間、使用者活動、前景視窗與 persistence 均以介面注入，使狀態機可在不等待真實時間的情況下測試。

重要技術決策依設計規格逐步記錄於 `docs/adr/`；在實作相應 slice 前完成 ADR，避免過早固定尚未驗證的細節。

