# Open issue specifications

本目錄中的 issue spec 是交給實作 Agent 的自包含執行契約。每份 spec 已納入
完成該票所需的產品規則、預設值、隱私界線與驗收條件；Agent 不需要另外閱讀
`docs/product/windows-eye-care-assistant-design-spec-v1.3.md` 才能實作。
GitHub Issue 只用來確認狀態、blockers 與較新的 comments。若 comments 與 spec
衝突，停止實作並提出 review，不得自行混合兩種語義。

只有下方索引列出的檔案是 open-ticket specs。檔名包含 `handoff` 或 `followup`
的文件是特定執行階段交接紀錄，不是獨立 ticket spec。

## 使用方式

1. 只在 GitHub blocking issues 全部關閉後開始該票。
2. 先讀 `AGENTS.md`、本 spec、GitHub Issue 全文與 comments；只有本 spec 明確
   要求新增或修改 ADR 時才需處理該 ADR。
3. 嚴格依 Execution checklist 實作，不自行擴大 scope。
4. 完成後執行該票指定測試，以及 solution build/test。
5. 回報變更、測試、已知限制、資料/schema 影響；不要自行 commit、push 或關票。

## Open tickets

- [#9 無數字視覺 Break Guide](issue-9-numberless-visual-break-guide.md)
- [#10 Break Guide 聲音／語音與降級](issue-10-break-guide-audio-fallback.md)
- [#11 Passive Pause 與可信重設](issue-11-passive-pause-and-trusted-reset-semantics.md)
- [#12 提醒重試冷卻](issue-12-reminder-retry-cooldown.md)
- [#13 Pause 與 Focus Mode 時間語義](issue-13-pause-focus-time-semantics.md)
- [#14 四級休息債務](issue-14-rest-debt-levels.md)
- [#15 呈現強度與可及系統列狀態](issue-15-presentation-intensity-and-tray.md)
- [#16 v1.3 使用事件持久化](issue-16-v13-usage-event-persistence.md)
- [#17 v1.3 每日統計](issue-17-v13-daily-statistics.md)
- [#18 v1.3 設定模型](issue-18-v13-settings-model.md)
- [#19 設定 UI、隱私與開機啟動](issue-19-settings-privacy-startup-ui.md)
- [#20 資料透明檢視](issue-20-data-transparency-view.md)
- [#21 資料匯出與安全清除](issue-21-data-export-and-safe-clear.md)
- [#22 效能與隱私自動化驗證](issue-22-performance-and-privacy-validation.md)
- [#23 Windows 手動驗收矩陣](issue-23-windows-manual-acceptance.md)
- [#24 Windows 安裝與升級](issue-24-windows-install-and-upgrade.md)
- [#25 Dogfooding 與 Review Backlog](issue-25-dogfooding-review.md)
