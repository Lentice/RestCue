# Windows walking skeleton smoke test

此記錄可在 Windows 10/11 與 .NET 10 SDK 重現 Issue #1 的啟動、system tray 與退出驗收。

> Repository 的 `AGENTS.md`、`global.json`、正式 product contract 與 CI
> 皆以 .NET 10 LTS 為開發與驗證基準。

## Automated verification

在 repository root 執行：

```powershell
dotnet --version
dotnet restore RestCue.sln
dotnet build RestCue.sln
dotnet test RestCue.sln
```

預期 SDK 為 `10.0.100` 或相容的 .NET 10 feature band，build 無 warning/error，所有測試通過。

## Interactive smoke test

1. 執行 `dotnet run --project src/RestCue.App`。
2. 確認沒有主視窗自動跳出，Windows system tray 出現一個 RestCue 圖示。
3. 右鍵圖示並選擇「開啟 RestCue」，確認狀態頁出現。
4. 關閉狀態頁，確認 process 與 tray 圖示仍存在。
5. 再次選擇「開啟 RestCue」，確認同一狀態頁重新出現，tray 仍只有一個圖示。
6. 選擇「結束 RestCue」，確認 process 結束且 tray 圖示消失。

此 smoke test 僅驗證 walking skeleton；提醒計時、狀態圖示變化與其他 tray 命令由後續 tickets 交付。

## Execution record

| Date | Environment | Check | Result |
|---|---|---|---|
| 2026-07-27 | Windows NT 10.0.26200.0, .NET SDK 10.0.302 | `dotnet build RestCue.sln` | PASS — 0 warnings, 0 errors |
| 2026-07-27 | Windows NT 10.0.26200.0, .NET SDK 10.0.302 | `dotnet test RestCue.sln` | PASS — 4 passed, 0 failed |
| 2026-07-27 | Windows computer-use session | Interactive step 1 | PARTIAL — App launched successfully and no main window appeared automatically |
| 2026-07-27 | Windows computer-use session | Interactive steps 2–6 | NOT RUN — the tool could not target the Windows taskbar or system tray |

The automated App lifecycle tests verify one tray visibility transition, repeated opening of the
same status-window instance, and tray disposal before shutdown. A human Windows desktop session
must still execute interactive steps 2–6 to verify Explorer's rendered tray icon and context menu;
the automation result must not be treated as that visual confirmation.
