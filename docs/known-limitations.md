# Known limitations

- 目前已提供 system tray 常駐、狀態頁開啟、明確退出、SQLite 設定載入、Working／Idle 活動狀態顯示，以及第一次非搶焦點休息提醒（工作週期累積、自然停頓偵測、最大等待逾時、休息完成重設週期）。每次定時器 tick 只取一次 UserActivitySample，同時驅動狀態顯示與 WorkCycleTracker。
- 休息期間按鈕已停用；休息完成完全由 WorkCycleTracker 的 fake-clock 相容邏輯控制，UI 不提前宣告完成。移除 IReminderPresenter 規格介面。
- 以下功能尚未實作：被動休息 (Passive Break)、延後 (Snooze)、忽略 (Ignore)、AutoDismissed、提醒逾時自動淡出、全螢幕降級、休息引導 (Break Guide) 語音／音效、session lock 與 power events、usage-event persistence、暫停與專注模式、停用狀態、tray 狀態圖示切換、完整選單命令。
- 若 Windows `GetLastInputInfo` 失敗，狀態會保守降級為 Idle，避免把未知活動誤算成有效工作；目前狀態頁不另外顯示偵測錯誤。
- WorkCycleTracker 累積工作時間的最小粒度為 1 秒（對應輪詢間隔），從 Idle 恢復的第一個 Tick 不累積，最多損失 1 秒。
- ReminderWindow 目前固定在主要螢幕右側邊緣；多螢幕依前景視窗螢幕定位的功能尚未實作。
- 開發與 CI 環境以 .NET 10 SDK 為基準。
