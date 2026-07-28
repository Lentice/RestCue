# Issue #9 — 完成無數字視覺 Break Guide

## Goal

提供不搶焦點、不中斷輸入的 Break Guide。只有完整走完設定時長才產生
`BreakCompleted`；任何提前結束都產生 `BreakCancelled`，且不得清除休息需求。

## Dependencies and governing rules

- Blocked by GitHub #4、#6；開工前確認兩者已關閉。
- 預設引導時間 20 秒，可設定範圍 10–60 秒；值來自 Core settings。
- 引導開始、中段與完成皆不得以數字、百分比或倒數表達。
- 若 Break Guide 完成語義仍無 ADR，新增一份 ADR，記錄去數字化、取消與降級決策。

## Scope

- Core：建立可由 `IClock` 驅動的開始、完成、取消流程與領域事件。
- App：提供極簡、無數字的視覺進度與明確取消操作。
- App 可顯示文字引導，但不得顯示剩餘秒數、百分比或環形數字刻度。
- 完成／取消接回既有 `WorkCycleTracker`，維持單一提醒嘗試。

## Out of scope

- 音效、語音與 Spatial Audio（#10）。
- 使用事件持久化（#16）、統計（#17）、設定 UI（#19）。
- 全域鍵盤 hook、滑鼠移動推論、遮罩、降黑、模糊或 modal/full-screen UI。

## Execution checklist

- [ ] 讀取 GitHub Issue #9 全文與 comments，確認 blockers 已關閉。
- [ ] 定義 Break Guide 的 `NotStarted/Running/Completed/Cancelled` 最小狀態與合法轉移。
- [ ] 所有完成判定使用注入的 `IClock`；UI 不自行擁有 timing 值。
- [ ] 完整達 `BreakDuration` 時只發出一次 `BreakCompleted` 並可信重設 Need。
- [ ] 使用者提前關閉、取消或 App 結束引導時只發出一次 `BreakCancelled`，不重設 Need。
- [ ] 建立無數字視覺呈現與明確取消按鈕；視窗不啟用、不搶焦點、不阻擋輸入。
- [ ] 不以任意鍵鼠活動推論完成或取消。
- [ ] 視窗重複開啟、重複取消與完成／取消競態均具 idempotent 行為。
- [ ] 更新必要的架構、已知限制或操作文件。

## Acceptance checklist

- [ ] 整個流程沒有數字倒數、剩餘時間或百分比。
- [ ] 精確到期才記為 `BreakCompleted` 並清除休息需求。
- [ ] 到期前任何結束均為 `BreakCancelled`，休息需求保持不變。
- [ ] 原前景視窗與鍵盤焦點不改變，滑鼠／鍵盤輸入不被封鎖。
- [ ] fake-clock 測試覆蓋到期前、精確到期、到期後、重複操作與競態。
- [ ] WPF 層測試覆蓋視窗生命週期與事件 wiring。

## Verification

- [ ] `dotnet test tests/RestCue.Core.Tests/RestCue.Core.Tests.csproj`
- [ ] `dotnet test tests/RestCue.App.Tests/RestCue.App.Tests.csproj`
- [ ] `dotnet build RestCue.sln`
- [ ] `dotnet test RestCue.sln --no-build`
- [ ] `git diff --check`

## Data/schema impact

無。本票只新增記憶體內語義與 UI；事件持久化由 #16 處理。

## Completion report

- [ ] Changes
- [ ] Tests（命令與結果）
- [ ] Known limitations
- [ ] Data/schema impact（應為 None）
