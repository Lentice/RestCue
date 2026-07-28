# Issue #25 — 執行 MVP v1.3 Dogfooding 與 Review Backlog

## Goal

連續至少五個工作日使用可安裝的 v1.3 build，結合本機事件與每日人工回饋，
產出不誇大健康效果的 Review Report、P0 決策與下一輪 backlog。

## Dependencies and governing rules

- Blocked by #24。
- 每日主觀干擾與眼睛疲勞各以 1–5 記錄，僅供自我觀察，不是醫療指標。
- Review trigger 包含：Ignored + AutoDismissed 明顯高於完成、頻繁 Pause/exit、
  fullscreen/app rule 誤判、CPU/memory/battery 超標、focus steal、Passive Pause
  造成反覆淡出、Idle reset 過度寬鬆、Level 3/4 頻繁且完成率低。
- Focus Assist 或深度工作偵測只能進入 Review Backlog 評估，不是本票實作內容。

## Scope

- 固定同一 release candidate/version 開始；必要 hotfix 需記錄日期與影響。
- 每工作日記錄主觀干擾感、眼睛疲勞、自行停用／Pause 原因與非敏感備註。
- 由原始事件分析 BreakCompleted、Idle reset、PassivePauseDetected、Snooze、
  Ignored、AutoDismissed、debt level/result。
- 特別檢查 Level 3/4、Passive Pause 淡出與 Idle reset 是否合理。

## Privacy and interpretation

只使用產品已允許的本機資料與自願人工回饋。不得新增 input/window title/screen/URL
等收集，也不得將主觀疲勞或完成率宣稱為醫療效果。Ignored 與 AutoDismissed 必須
分開解讀。

## Execution checklist

- [ ] 記錄 dogfood build commit/version、installer checksum、OS/device baseline。
- [ ] 建立 5 個工作日以上的每日表格，不能用週末補列假資料。
- [ ] 每日記錄啟用時數、干擾 1–5、疲勞 1–5 與明確問題。
- [ ] 匯出／查詢每日 event aggregates，確認與 App 統計一致。
- [ ] 分析 completion、Idle、Passive Pause、Snooze、Ignored、AutoDismissed。
- [ ] 分析各 debt level 的到達頻率、呈現結果與完成率，尤其 Level 3/4。
- [ ] 記錄 focus steal、fullscreen 誤判、淡出／重現、Pause/exit 逃避、資源問題。
- [ ] 每個發現標示 evidence、severity、frequency、user impact、recommendation。
- [ ] 所有 P0 建 issue 並在 report 記錄 fixed/accepted/block release 決策。
- [ ] 產出下一版 backlog；深度工作偵測/Focus Assist 只能依 evidence 評估，
      不得直接納入 scope。

## Acceptance checklist

- [ ] 至少五個實際工作日，每日回饋與版本資訊完整。
- [ ] 所有指定 event/result/debt 指標均有分開分析。
- [ ] Report 明確區分觀察、推論與產品決策。
- [ ] 所有 P0 已修正並重驗，或有具 owner/reason/date 的明確 release decision。
- [ ] Review Backlog 每項有 evidence、期望 outcome、優先級與 blocking edge。
- [ ] Report 不含禁止資料或醫療效果宣稱。

## Deliverables

- [ ] `docs/reports/` 下的 MVP v1.3 Dogfooding Review Report
- [ ] 非敏感、可重算的 aggregate 附件或產生步驟
- [ ] 每個 P0/P1 finding 的 GitHub issue link
- [ ] 下一版本 Review Backlog

## Data/schema impact

不新增產品 schema 或收集類型。Dogfooding 文件只保存聚合結果與自願人工回饋，
不得提交原始使用者資料庫。

## Completion report

- [ ] Changes/artifacts
- [ ] Validation period and build
- [ ] Findings/limitations
- [ ] Data/schema impact
