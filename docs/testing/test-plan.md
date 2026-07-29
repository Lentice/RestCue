# Test plan

## Automated

- Core：狀態轉移、有效工作時間、idle/passive break 邊界、自然停頓、snooze/ignore/auto-dismiss、sleep/resume。
- Infrastructure：SQLite migration、事件查詢索引、設定持久化與 application rules。
- App smoke tests：啟動、退出、system tray lifecycle。
- Validation (short)：state scenarios（9 個 phase 全覆蓋）、polling cadence（每 tick 1 sample）、tray update count（穩定狀態不重複更新）、sqlite write cadence（純 poll 無寫入）、privacy denylist（0 命中）、process name opt-in（預設為 null）。
- Validation (long，`Category=LongRun`)：8 小時 soak harness 量測 CPU/memory/handle/thread/DB 成長。

## Manual Windows matrix

見 `docs/testing/windows-manual-acceptance-matrix.md`。每個 release 前必須執行一次完整矩陣，並將結果填回該檔。

每個 ticket 必須列出實際執行的測試與未覆蓋限制。

