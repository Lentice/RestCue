# Known limitations

- 目前已提供 system tray 常駐、狀態頁開啟、明確退出、SQLite 設定載入，以及以 Windows 最後輸入時間顯示 Working／Idle。tray 狀態圖示、完整選單命令、提醒狀態機、usage-event persistence、session lock 與 power events 尚未實作。
- 若 Windows `GetLastInputInfo` 失敗，狀態會保守降級為 Idle，避免把未知活動誤算成有效工作；目前狀態頁不另外顯示偵測錯誤。
- 開發與 CI 環境以 .NET 10 SDK 為基準。
