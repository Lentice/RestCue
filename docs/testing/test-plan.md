# Test plan

## Automated

- Core：狀態轉移、有效工作時間、idle/passive break 邊界、自然停頓、snooze/ignore/auto-dismiss、sleep/resume。
- Infrastructure：SQLite migration、事件查詢索引、設定持久化與 application rules。
- App smoke tests：啟動、退出、system tray lifecycle。

## Manual Windows matrix

- Windows 10 與 11。
- 單螢幕、雙螢幕、主要螢幕切換。
- 100%、125%、150%、200% DPI。
- 全螢幕影片、簡報、最大化 IDE。
- lock/unlock、sleep/resume、Remote Desktop。
- 提醒顯示時持續輸入，確認焦點不變。

每個 ticket 必須列出實際執行的測試與未覆蓋限制。

