# Test plan

## Automated

- Core：狀態轉移、有效工作時間、idle/passive break 邊界、自然停頓、snooze/ignore/auto-dismiss、sleep/resume。
- Infrastructure：SQLite migration、事件查詢索引、設定持久化與 application rules。
- App smoke tests：啟動、退出、system tray lifecycle。
- Validation (short)：state scenarios（9 個 phase 全覆蓋）、polling cadence（每 tick 1 sample）、tray update count（穩定狀態不重複更新）、sqlite write cadence（純 poll 無寫入）、privacy denylist（0 命中）、process name opt-in（預設為 null）。
- Validation (long，`Category=LongRun`)：8 小時 soak harness 量測 CPU/memory/handle/thread/DB 成長。

## Manual Windows matrix

- Windows 10 與 11。
- 單螢幕、雙螢幕、主要螢幕切換。
- 100%、125%、150%、200% DPI。
- 全螢幕影片、簡報、最大化 IDE。
- lock/unlock、sleep/resume、Remote Desktop。
- 提醒顯示時持續輸入，確認焦點不變。

每個 ticket 必須列出實際執行的測試與未覆蓋限制。

