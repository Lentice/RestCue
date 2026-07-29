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

## Implementation guidance for agents

本節是給實作 Agent 的操作指引，補充而非取代下方 Execution / Acceptance
checklist。本票的產出是**文件交付物**，不是產品程式碼：除了修正 P0 時可能
另開票處理，本票本身不應新增或修改 `src/` 下的任何檔案，也不得為了收集資料
而加入新的量測或收集程式。開工前確認 #24 已關閉且已有可安裝的 v1.3 build。

### 檔案地圖

| 路徑 | 動作 | 變更內容 |
|---|---|---|
| `docs/reports/mvp-v1.3-dogfooding-review.md` | 新增 | 主交付物：Review Report。`docs/reports/` 目錄目前不存在，需一併建立 |
| `docs/reports/mvp-v1.3-dogfooding-daily-log.md` | 新增 | 每日回饋 log（版本 baseline + 每工作日一列）。可獨立成檔以免主報告在 5 天內反覆改寫 |
| `docs/reports/mvp-v1.3-dogfooding-aggregates.md` | 新增 | 非敏感 aggregate 附件，或（若數字量大）記錄重算步驟與查詢條件的說明檔 |
| `docs/reports/mvp-v1.4-review-backlog.md` | 新增 | 下一版 Review Backlog；每項含 evidence、期望 outcome、優先級、blocking edge |
| `docs/known-limitations.md` | 修改（必要時） | 只在 dogfooding 發現並接受為已知限制的項目才追加 |
| `src/**` | 不修改 | 本票不含產品程式碼；P0 修正另開 issue 與另一次變更 |

檔名可調整，但必須全部落在 `docs/reports/` 下並在主報告內互相連結。原始
資料庫、export 原檔與任何逐筆事件檔案**不得** commit（見 Data/schema impact）。

### 資料收集方式

只能使用產品已經出貨的介面取得證據，不得新增任何收集：

- **#16 usage events**：真相來源。`usage_events` 表（schema v2）位於
  `%LocalAppData%\RestCue\restcue.db`，欄位為 `id`、`occurred_utc`、
  `event_type`、`payload`，排序一律以 `(occurred_utc, id)` 為準。
- **#17 每日統計頁面**：使用者主動開啟的今日統計，用來交叉檢查你自己算出的
  aggregate。兩者不一致就是一個 finding，不要偷偷選一個好看的數字。
- **#21 匯出功能**：取得可分析副本的正當途徑。用 App 內的 export 匯出到工作
  目錄外的位置再分析；不要直接對執行中的 app 所使用的 DB 檔做寫入操作。
- **#20 資料透明檢視**：確認你分析的欄位確實是產品承認在保存的欄位。

規則：

- 不得新增 input、window title、screen、URL、process name 等收集；即使
  foreground process name opt-in 存在，也不要為了 dogfooding 打開它。
- 報告中的每一個數字都必須可由原始事件重算：寫下你用的日期區間、timezone、
  以及對應的 event type / payload 條件，讓另一個人能得到相同數字。
- 不得以「印象中好像比較常忽略」這類回憶當作數據；主觀項目只有干擾 1–5 與
  眼睛疲勞 1–5 兩欄，並且必須標明是自我觀察而非量測。
- 手動計數（例如「我記得今天自己按了兩次 Pause」）只能作為 note，不能取代
  `Paused` / `Resumed` 事件的計數。

### 執行順序

1. **Day 0（baseline）**：固定 release candidate。記錄 build commit、版本號、
   installer SHA-256、OS build、裝置與螢幕配置、以及 dogfooding 起始日期。
   確認 app 從安裝態啟動（不是 `dotnet run`），因為本票驗證的是出貨形態。
2. **Day 1–5+（每日）**：當天結束時寫一列 daily log。建議欄位：

   | Date | Weekday | 啟用時數 | 干擾 1–5 | 疲勞 1–5 | 自行 Pause/停用原因 | 觀察到的問題 | 對應 event 證據 |
   |---|---|---|---|---|---|---|---|

   當日若有 hotfix，另記日期與影響範圍，並說明它是否使前幾日資料不可比。
3. **Mid-point check（第 3 日後）**：確認事件真的有寫入、統計頁與 DB 一致、
   log 沒有空洞。若前幾日資料無效，延長天數而不是補寫。
4. **Aggregation（全部工作日結束後）**：依「分析與分類指引」的維度逐項聚合，
   同時算出全期與逐日兩層數字，並與 #17 頁面交叉核對。
5. **P0 triage**：把 findings 依下方 rubric 分級，每個 P0 開 GitHub issue，
   附最小非敏感 repro。
6. **Backlog filing**：把非 P0 的 findings 寫進 `mvp-v1.4-review-backlog.md`。
   Focus Assist／深度工作偵測即使很想做，也只能以 evidence 進 backlog 評估，
   不得在本票納入 scope。
7. **收尾**：主報告連結所有 issue，填完 Completion report 四項；不自行 commit、
   push 或關票。

### 分析與分類指引

必須分開統計、不得合併的維度（對齊 #17 的 event mapping）：

| 維度 | 來源 | 解讀邊界 |
|---|---|---|
| BreakCompleted（完整休息） | `BreakCompleted` 次數 | 唯一算「完成」的訊號 |
| Idle reset（明確離席） | 完整 `IdleStarted` → `IdleEnded` 的 `IdleEnded` 次數 | 是離席，不是休息完成 |
| Passive Pause | `PassivePauseDetected` 次數 | 既不是完成也不是 reset |
| 延後 Snooze | `ReminderDismissedPayload.Result = Snoozed` | 反映時機不合，不是拒絕 |
| 主動忽略 Ignored | `Result = Ignored` | 可能反映意願 |
| 逾時未回應 AutoDismissed | `Result = AutoDismissed` | 可能反映**能見度**不足 |
| 債務等級 | `RestDebtLevelChangedPayload(Previous, Current)` | 需分別看各 level 到達頻率與該 level 下的完成率，尤其 Level 3/4 |

- **主動 Ignored 與被動 AutoDismissed 絕對不可合併成「忽略率」**。兩者的產品
  結論完全相反：Ignored 高指向提醒節奏／內容問題，AutoDismissed 高指向提醒
  根本沒被看到。合併會直接導向錯誤的修法。
- 同理，BreakCompleted、Idle reset、Passive Pause 三者不可互相替代或加總成
  「休息次數」。
- findings **不得**表述為醫療或健康結果。可以寫「第 4 日主觀疲勞自評為 4，
  同日 Level 3 到達 3 次且完成率 33%」；不可以寫「本工具降低了眼睛疲勞」、
  「改善視力」或任何療效／診斷語句（`docs/privacy.md`：RestCue 不是醫療器材）。
- 報告必須把**觀察**（事件數字）、**推論**（可能原因）、**產品決策**
  （要不要改）分段標示，不要混在同一句話裡。

Severity rubric（用它分級，不要憑感覺）：

- **P0**：違反產品護欄或造成資料／可用性損失。例：reminder 搶焦點或封鎖輸入、
  全螢幕時仍彈出一般 popup、資料遺失或統計錯算、crash／無法啟動、
  CPU/memory 明顯超出 #22 門檻、記錄了禁止資料。→ 必須修或有明確 release
  decision，不可留白。
- **P1**：核心體驗顯著受損但有 workaround。例：AutoDismissed 遠高於
  BreakCompleted（提醒看不到）、Passive Pause 造成反覆淡出、Idle reset 過度
  寬鬆導致週期不累積、Level 3/4 頻繁但完成率低。
- **P2**：打磨、文案、低頻小瑕疵，或需要更多資料才能判斷的觀察。

若某項在 P0 與 P1 之間難以判斷，以「是否違反 `AGENTS.md` 產品護欄」為分界；
違反者一律 P0。

### 交付格式

`docs/reports/mvp-v1.3-dogfooding-review.md` 至少包含以下章節：

1. **Build and environment baseline** — commit、版本、installer checksum、
   OS build、裝置、螢幕／DPI、hotfix 紀錄。
2. **Validation period** — 實際使用的工作日清單（逐日日期），以及任何被排除
   的日期與原因。
3. **Daily feedback table** — 每工作日一列（欄位見「執行順序」第 2 步）。
4. **Event aggregates** — 全期與逐日兩張表，欄位依「分析與分類指引」的維度
   逐一分列；附上重算方式（日期區間、timezone、篩選條件）。
5. **Debt level analysis** — 各 level 到達次數、呈現結果、完成率，Level 3/4
   單獨小節。
6. **Findings** — 每項一列：`ID | 描述 | evidence | severity | frequency |
   user impact | recommendation`。描述須區分觀察與推論。
7. **P0 decisions** — 每個 P0 一列：`ID | issue link | 決策（fixed /
   accepted / block release）| owner | reason | date | 重驗結果`。
   **每個 P0 都必須以「已修正並重驗」或「已記錄的明確決策」結尾，不得留空。**
8. **Review backlog** — 指向 `mvp-v1.4-review-backlog.md`，每項含 evidence、
   期望 outcome、優先級、blocking edge。
9. **Limitations** — 樣本只有一人一機、天數有限、主觀評分不可比等，明說
   不可外推。

### 常見錯誤

- 為了湊滿五天而回填、補寫或用週末假造沒有實際使用的日期。這是最嚴重的錯誤，
  它讓整份報告失效；天數不足就延長期間並如實記錄。
- 用單日或單一情境的數字外推成趨勢結論（「Snooze 率上升」需要多日對照）。
- 把 Ignored 與 AutoDismissed 合併成「忽略率」，或把 Idle reset／Passive
  Pause 當成休息完成。
- 用醫療／療效語言描述結果（疲勞改善、護眼效果、降低乾眼），或把自評分數
  當成臨床指標。
- P0 只被列出卻沒有修正也沒有具 owner/reason/date 的 release decision。
- 把禁止資料貼進報告：截圖中的 window title／文件名稱／URL、raw
  `restcue.db`、未過濾的 export 原檔、或個人可識別內容。
- 為了拿到「更好的數據」而打開 foreground process name opt-in 或新增收集。
- 中途換 build 卻沒記錄，導致前後日資料不可比。
- 只寫主觀感受而沒有對應 event 證據，或反之只貼數字而沒有 user impact 判讀。

### 逐步 checklist

- [ ] 確認 #24 已關閉，且使用的是安裝後的 build 而非 `dotnet run`。
- [ ] 建立 `docs/reports/` 與四份文件骨架。
- [ ] 記錄 build commit／版本／installer SHA-256／OS build／裝置 baseline。
- [ ] 連續至少五個實際工作日，每日當天寫入 daily log（不事後回填）。
- [ ] 第 3 日後做 mid-point check，確認事件寫入與統計頁一致。
- [ ] 透過 #21 export 或 #20 透明檢視取得分析資料，未新增任何收集。
- [ ] 聚合 BreakCompleted、Idle reset、Passive Pause、Snooze、Ignored、
      AutoDismissed，六項分開列示。
- [ ] 分析各 debt level 到達頻率、呈現結果與完成率，Level 3/4 單獨小節。
- [ ] 記錄 focus steal、fullscreen 誤判、淡出／重現、Pause/exit 逃避、資源問題。
- [ ] 每個 finding 標註 evidence／severity／frequency／user impact／recommendation。
- [ ] 依 rubric 分級，所有 P0 開 issue 並填妥 P0 decisions 表（無空白）。
- [ ] 產出 v1.4 Review Backlog；Focus Assist／深度工作偵測僅列為待評估。
- [ ] 自我複查：無醫療宣稱、無禁止資料、所有數字可由原始事件重算。
- [ ] 執行 `git diff --check`，不自行 commit、push 或關票。

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

- [x] Changes/artifacts: Created `docs/reports/` with 4 files — `mvp-v1.3-dogfooding-review.md` (9-section report structure), `mvp-v1.3-dogfooding-daily-log.md` (baseline + Day 1 entry), `mvp-v1.3-dogfooding-aggregates.md` (aggregate templates with recalculation method), `mvp-v1.4-review-backlog.md` (backlog structure with evaluation items).
- [x] Validation period and build: Baseline recorded: commit 45fca87, version 1.3.0.0, installer SHA-256 `0C342E88...`, Windows 11 build 26200, single 1920x1080 @ 100% DPI. Day 1 (2026-07-29) partial — app verified running (~109 MB, responding), no reminders triggered yet. Full 5-workday cycle requires product owner to complete daily entries.
- [x] Findings/limitations: No P0 identified yet (insufficient usage). Limitations documented: single user/device, no multi-monitor, single session, self-reported subjective scales.
- [x] Data/schema impact: No product schema changes. Only aggregate results and voluntary feedback in `docs/reports/`. No raw user database committed.
