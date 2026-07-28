# Issue #20 — 提供資料透明檢視

## Goal

讓使用者從系統列兩次點擊內查看「目前實際知道什麼」：已啟用的資料類型、
本機事件筆數與時間範圍。檢視本身必須完全唯讀。

## Dependencies and governing rules

- Blocked by #16、#19。
- 使用者從 tray menu 開始最多兩次明確 click 可開啟此頁。
- 至少列出：最後輸入「經過時間」是否用於 activity 判定、usage event 類型與筆數、
  settings 是否保存、foreground process-name collection 是否啟用。
- 明確列出永不收集：window title、input、clipboard、screen content、URL、
  document name。

## Scope

- 從目前 settings 與實際 repository metadata 動態產生內容。
- 顯示資料類型、是否啟用、筆數與最早／最新日期（若存在）。
- Tray menu 提供兩次點擊內可達入口。

## Out of scope

- 顯示原始敏感值、window title、process-name 清單、事件逐筆瀏覽。
- 開啟追蹤、寫 audit event、紅點、未讀數或主動提示。
- 匯出與清除（#21）。

## Execution checklist

- [ ] 建立 readonly transparency query，不共用會寫入 last-viewed 的 UI service。
- [ ] 資料類型由實際 schema/repository + settings 決定，不維護易漂移的硬編碼宣稱。
- [ ] opt-in 關閉時清楚區分「未收集」與「目前 0 筆」。
- [ ] repository unavailable 時顯示安全、非敏感且可診斷的局部錯誤。
- [ ] Tray → menu item → view 路徑不超過兩次 click。
- [ ] 開啟、刷新、關閉不建立 usage event、不改 settings、不更新資料庫。
- [ ] 內容與 `docs/privacy.md` 一致；差異須在同票修正文件。
- [ ] UI 不使用紅點、badge 或監控感語言。

## Acceptance checklist

- [ ] 對每種 settings/database fixture，顯示內容與實際狀態一致。
- [ ] 開啟前後資料庫 byte/logical content 與 event counts 不變。
- [ ] opt-in off/on 與空／非空資料庫有測試。
- [ ] 兩次點擊內可達且不搶走使用者目前工作焦點；只有明確點擊才開啟。
- [ ] 不暴露禁止資料或原始 process records。

## Verification

- [ ] transparency query tests
- [ ] App navigation/view-model tests
- [ ] Infrastructure readonly integration tests
- [ ] 手動兩次點擊與焦點 smoke test
- [ ] `dotnet build RestCue.sln`
- [ ] `dotnet test RestCue.sln --no-build`

## Data/schema impact

無；此功能必須唯讀。

## Completion report

- [ ] Changes
- [ ] Tests
- [ ] Known limitations
- [ ] Data/schema impact（應為 None）
