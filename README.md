# RestCue – Eye Break Reminder

**RestCue 護眼助理**是 Windows 10/11 的非阻擋式護眼提醒工具。它會根據有效工作時間與自然輸入停頓提供提醒，但不搶焦點、不封鎖鍵盤滑鼠，也不強迫使用者中斷工作。

> A gentle cue to rest your eyes.

## 技術基線

- C# / .NET 10 LTS
- WPF
- 本機 SQLite（由後續 persistence ticket 導入）
- xUnit
- GitHub Actions（Windows runner）

## 專案結構

- `src/RestCue.App`：WPF composition root 與 UI
- `src/RestCue.Core`：不依賴 Windows/WPF 的 domain 與時間邏輯
- `src/RestCue.Infrastructure`：Windows API、儲存與外部介面實作
- `tests/RestCue.Core.Tests`：核心單元測試
- `docs`：產品、架構、測試與 agent 協作文件

## 開發

需求：Windows 10/11、.NET 10 SDK。

```powershell
dotnet restore RestCue.sln
dotnet build RestCue.sln
dotnet test RestCue.sln
dotnet run --project src/RestCue.App
```

正式需求基準為 `docs/product/design-spec.md`；若程式與規格牴觸，先提出 ADR 或需求 review，不要默默改寫產品原則。
