# Issue #21 — 匯出並安全清除本機資料

## Goal

讓使用者匯出允許的本機資料，並經明確二次確認後分別清除 usage history 或
settings。操作完成後 UI 與統計不得顯示 stale data。

## Dependencies and governing rules

- Blocked by #16、#19。
- 所有資料預設只留本機；export 必須由使用者主動觸發並選擇目的地。
- History clear 與 Settings reset 是兩個不同操作，必須分別確認。
- Settings reset 後前景程式名稱 collection 回到預設關閉。

## Scope

- 明確、版本化的 export 格式與 privacy allowlist。
- 分開的 Clear history 與 Reset settings 操作。
- 二次確認、transactional clear、failure recovery 與 UI cache invalidation。

## Out of scope

- 雲端同步、上傳、分享 API、自動匯出或 background export。
- secure erase 的物理磁區保證；若 SQLite 無法保證，文件需精確說明。
- 一鍵同時清除所有資料，除非另有 scope review。

## Execution checklist

- [ ] 定義 export schema/version、欄位 allowlist 與 timezone/timestamp 表示。
- [ ] 匯出前重新查詢 repository，不輸出 UI cache。
- [ ] 不輸出禁止資料；opt-in process data 若允許匯出，需明確標示且遵守設定。
- [ ] 使用暫存檔 + atomic replace，失敗不留下被誤認為成功的 partial export。
- [ ] History clear 在 transaction 中刪除 usage events，不改 settings。
- [ ] Settings reset 恢復 validated privacy-safe defaults，不刪 usage history。
- [ ] 每個破壞性操作均需精確二次確認，取消完全不寫入。
- [ ] 成功後 invalidate query/UI cache；統計與透明檢視立即反映。
- [ ] BUSY/LOCKED/I/O failure 不觸發 corruption recovery，不顯示成功。
- [ ] 更新 privacy/known limitations，說明 SQLite deletion/VACUUM 的實際保證。

## Acceptance checklist

- [ ] export fixture 只含 allowlist 欄位，且可被 schema/version 辨識。
- [ ] history 與 settings 可獨立清除，互不影響。
- [ ] 取消確認、export failure、clear rollback 均保留原資料。
- [ ] 清除後 #17/#20 不再顯示舊資料或 stale counts。
- [ ] fresh settings 的 foreground process opt-in 回到 false。
- [ ] 沒有網路傳輸或背景上傳。

## Verification

- [ ] export privacy allowlist tests
- [ ] repository transactional clear integration tests
- [ ] App confirmation/cancel/cache tests
- [ ] `dotnet build RestCue.sln`
- [ ] `dotnet test RestCue.sln --no-build`
- [ ] `git diff --check`

## Data/schema impact

不必新增 schema；會刪除使用者指定資料或重設 settings。完成報告必須說明可恢復性、
transaction/VACUUM 行為與 export 格式版本。

## Completion report

- [ ] Changes
- [ ] Tests
- [ ] Known limitations（含 deletion guarantee）
- [ ] Data/schema impact
