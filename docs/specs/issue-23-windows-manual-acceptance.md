# Issue #23 — 執行 Windows 手動驗收矩陣

## Goal

在真實 Windows 10/11 環境完成可重現的焦點、全螢幕、多螢幕、高 DPI、
lock/sleep/resume 與降級驗收，保留環境與證據，讓結果可被另一人重跑。

## Dependencies and governing rules

- Blocked by #22。
- 支援 Windows 10/11，目標 8 小時穩定常駐。
- Reminder 不得成為 active window、不接收鍵盤焦點、不得封鎖 input。
- Fullscreen 時不得顯示一般 popup；maximized window 不應被誤判為 fullscreen。
- Break Guide 不含數字倒數，只有完整完成才清除 Need。

## Scope

- Windows 10 與 Windows 11 的 supported builds。
- 單／多螢幕，不同 primary monitor、scale 與 mixed-DPI。
- IDE/文字輸入焦點、全螢幕影片／簡報、maximized 非 fullscreen。
- lock/unlock、sleep/resume、display reconnect 與 audio unavailable。

## Evidence rules

不得以截圖或錄影保存 window title、document name、URL、輸入內容或其他禁止資料。
使用乾淨測試帳號與人工生成 fixture。每一列記錄 OS build、app commit/build、
display topology、DPI、步驟、expected、actual、pass/fail 與 issue link。

## Implementation guidance for agents

本節是給執行 Agent 的補充指引，不取代下方 Execution/Acceptance checklist。
本票是手動驗收：不寫產品程式碼，產出是 `docs/testing/` 下的矩陣與證據。
所有路徑以 repository root 為基準。

### 檔案地圖

- `docs/testing/windows-manual-acceptance-matrix.md` — 新增。版本化驗收矩陣
  本體：軸定義、每格 pass criteria、以及 Result 表（見「證據與紀錄格式」）。
- `docs/testing/windows-manual-acceptance-evidence-template.md` — 新增。
  單列證據模板（環境欄位、步驟、expected、actual、判定、證據引用欄位），
  每次執行複製一份填寫。
- `docs/testing/test-plan.md` — 修改。Manual Windows matrix 段落改為指向
  上述矩陣檔，保留既有條列不刪除。
- `docs/known-limitations.md` — 修改。補上本次驗收發現的限制與 BLOCKED 格。
- `docs/testing/windows-walking-skeleton-smoke-test.md` — 參考，不修改。
  Execution record 表格格式（Date / Environment / Check / Result）是本票
  Result 表的基準樣式，保持一致。

不新增測試程式碼；#22 的自動化 harness 已負責 fake-clock 與隱私掃描，本票
只做真實 Windows 桌面上無法自動化的觀察。

### 執行方式

先確認待驗版本可建置並記下 commit：

```powershell
git rev-parse --short HEAD
dotnet build RestCue.sln
dotnet test RestCue.sln --filter "Category!=LongRun"
```

（上述三行已在本 repo 實跑驗證可用。）

啟動待驗 app（照
`docs/testing/windows-walking-skeleton-smoke-test.md` 的互動流程）：

```powershell
dotnet run --project src/RestCue.App
```

環境資訊記錄用：

```powershell
[System.Environment]::OSVersion.VersionString
dotnet --version
```

本票不產生量測檔；若有截圖，放在 `artifacts/acceptance/`（`artifacts/` 已被
`.gitignore` 忽略），只把檔名與說明寫進矩陣，不 commit 圖檔本身。

### 執行順序

1. 建立 `docs/testing/windows-manual-acceptance-matrix.md`，先定義矩陣軸，
   再逐格執行。軸如下（每格都要有明確判定）：
   - OS：Windows 10、Windows 11（各記完整 build 號）。
   - Display topology：單螢幕、雙螢幕、切換 primary monitor、
     拔插／reconnect。
   - DPI：100%、125%、150%、200%，含 mixed-DPI（兩螢幕不同縮放）。
   - Foreground context：文字輸入／IDE 有焦點、borderless fullscreen 影片、
     簡報全螢幕、maximized 且有 caption 的視窗。
   - Session／power：lock、unlock、sleep、resume。
   - 降級：audio unavailable、fullscreen 偵測不確定。
2. 每格的 pass criteria（照現行程式契約）：
   - 焦點：reminder 顯示前後 active window 與鍵盤焦點 identity 不變；
     顯示中持續打字，字元全部進入原本的 app，reminder 不得成為 active
     window、不得接收鍵盤焦點、不得封鎖 input。滑鼠仍可操作 reminder。
   - Fullscreen：borderless fullscreen（無 `WS_CAPTION` 且視窗矩形等於螢幕
     矩形）時降級為不顯示一般 popup，只有 tray 提示；maximized 且有 caption
     的視窗必須被判為 NotFullscreen，行為與一般 Working 相同。
   - 偵測不確定時（`FullscreenState.Uncertain`）必須保守降級，不得彈 popup。
   - 多螢幕／DPI：reminder 不被裁切、不跨螢幕拉伸、文字不模糊；
     已知限制是 reminder 目前固定在主要螢幕右側邊緣（見
     `docs/known-limitations.md`），若觀察到就記為已知限制而非新缺陷。
   - lock/sleep/resume：resume 後不得回填巨大有效工作時間、不得連續爆出多個
     reminder，tray 狀態與選單可用性與 phase 一致。
   - Break Guide：無數字倒數、不遮罩全螢幕、不阻擋輸入；只有完整完成才清除
     Need，取消不清除。
   - Tray：狀態可在不依賴顏色的情況下辨識（tooltip 文字 + icon 形狀）。
3. 每格執行後立刻填 Result 表，四種判定只能是
   `PASS`／`FAIL`／`BLOCKED`／`N/A`。
4. 無法實際執行的格（例如沒有第二台螢幕、沒有 Windows 10 機器、無法觸發
   sleep）一律記 `BLOCKED`，並在 Reason 欄寫明缺什麼、Required environment
   欄寫明需要的硬體／OS／權限。**絕對不得由程式碼推理或自動化測試結果推導出
   `PASS`**：本票的 `PASS` 只能來自真實觀察。
5. 每個 `FAIL` 開一個獨立 GitHub issue，附最小非敏感 repro 步驟與 severity，
   並把 issue link 填回該列。
6. 全部格填完後更新 `docs/testing/test-plan.md` 與
   `docs/known-limitations.md`，並在完成回報中列出 BLOCKED 清單。

### 證據與紀錄格式

`docs/testing/windows-manual-acceptance-matrix.md` 的環境表（每個測試環境
一列）：

| Env ID | OS build | .NET SDK | App commit | Displays | DPI |
|---|---|---|---|---|---|
| W10-A | | | | | |
| W11-A | | | | | |

Result 表（每個矩陣格一列）：

| # | Env ID | Scenario | Steps ref | Expected | Actual | Result |
|---|---|---|---|---|---|---|

| # | Date | Reason / Required environment | Evidence | Issue |
|---|---|---|---|---|

欄位規則：

- `Scenario` 用固定字串（例如 `focus-typing`、`fullscreen-video`、
  `maximized-caption`、`mixed-dpi-125-150`、`primary-switch`、
  `lock-unlock`、`sleep-resume`、`audio-unavailable`），方便另一人重跑。
- `Steps ref` 指向本檔內編號步驟，不重寫步驟散落各處。
- `Result` 只能是 `PASS`／`FAIL`／`BLOCKED`／`N/A`。
- `Reason / Required environment` 只在 `BLOCKED`／`FAIL` 時必填。
- `Evidence` 只寫檔名與一句描述（例如
  `artifacts/acceptance/w11-fullscreen-tray.png — tray tooltip 顯示待處理`）。
  截圖只能拍 RestCue 自己的視窗、tray icon 與 tooltip；畫面不得出現其他 app
  的 window title、URL、文件名稱或輸入內容。必要時關閉其他視窗、用人工
  fixture 文字（例如 `aaaa bbbb`）再截圖。
- 引用 log 時只貼與判定相關的行，且該行不得含上述禁止資料；日誌檔本身留在
  `artifacts/`（gitignored），不 commit。

### 常見錯誤

- 以「程式碼看起來不會搶焦點」或「#22 自動化測試通過」為理由把手動格標成
  `PASS`。沒有真實觀察就只能是 `BLOCKED`。
- 缺硬體時把格改成 `N/A` 混淆判定：`N/A` 只用於該環境本質不適用（例如單螢幕
  環境的 mixed-DPI 格），缺設備一律 `BLOCKED`。
- 截圖或錄影裡意外帶到瀏覽器網址、IDE 檔名、聊天內容等禁止資料，證據本身變成
  隱私洩漏。
- 用自己的日常帳號與真實文件測試；必須用乾淨測試帳號與人工 fixture。
- 只記「看起來正常」而沒寫 Expected／Actual，導致另一人無法重跑。
- 把已知限制（reminder 固定主要螢幕右側）當成新缺陷重複開 issue；反之也不要
  把新缺陷塞進已知限制。
- 驗焦點時只看視窗外觀，沒有真的持續打字確認字元去向。
- 忘記記 OS build 與 app commit，結果無法對應到版本。

### 逐步 checklist

- [ ] 記下 `git rev-parse --short HEAD` 與 `dotnet --version`
- [ ] `dotnet build RestCue.sln` 與
      `dotnet test RestCue.sln --filter "Category!=LongRun"` 通過
- [ ] 建立 `docs/testing/windows-manual-acceptance-matrix.md` 與軸定義
- [ ] 建立 `docs/testing/windows-manual-acceptance-evidence-template.md`
- [ ] 準備乾淨測試帳號與人工 fixture 內容
- [ ] 填好環境表（至少 W10 與 W11 各一列）
- [ ] `focus-typing`：打字期間字元全進原 app，reminder 非 active window
- [ ] 滑鼠可操作 reminder，鍵盤焦點不變
- [ ] `fullscreen-video`／簡報：降級為 tray 提示，無一般 popup
- [ ] `maximized-caption`：未被誤判為 fullscreen
- [ ] DPI 100/125/150/200 與 mixed-DPI 各一格
- [ ] 多螢幕、primary 切換、display reconnect 各一格
- [ ] `lock-unlock`、`sleep-resume`：無時間回填、無 burst reminders
- [ ] Break Guide：無數字、不遮罩、不阻擋輸入，取消／完成語義正確
- [ ] `audio-unavailable` 降級與 tray 非色彩辨識
- [ ] 每個 `FAIL` 已開 issue 並填回 link
- [ ] 每個 `BLOCKED` 已填 Reason 與 Required environment
- [ ] 證據人工複查無禁止資料，圖檔留在 `artifacts/acceptance/` 未 commit
- [ ] 更新 `docs/testing/test-plan.md` 與 `docs/known-limitations.md`

## Execution checklist

- [ ] 建立版本化 acceptance matrix，不只寫自由格式心得。
- [ ] 準備乾淨 Win10/Win11 測試環境與非敏感測試內容。
- [ ] 驗證 reminder 出現前後 active window/keyboard focus identity 不變。
- [ ] 驗證滑鼠可操作 reminder，但鍵盤輸入仍送往原 app。
- [ ] 驗證 borderless fullscreen 降級，maximized/caption window 不誤判。
- [ ] 驗證 monitor 切換、拔插、primary change 與 mixed 100/125/150/200% DPI。
- [ ] 驗證 lock、unlock、sleep、resume 不回填巨大工作時間或 burst reminders。
- [ ] 驗證 Break Guide 無數字、不遮罩、不阻擋輸入，取消／完成語義正確。
- [ ] 驗證音訊失敗降級、tray 非色彩辨識與資料透明兩次點擊。
- [ ] 每個 failure 建立獨立 GitHub issue，附最小非敏感 repro 與 severity。

## Acceptance checklist

- [ ] Windows 10 與 11 每個 required scenario 均有 dated result。
- [ ] mixed-DPI、多螢幕、全螢幕與 maximized 行為符合契約。
- [ ] 所有 reminder/guide 情境前景輸入焦點維持不變。
- [ ] lock/sleep/resume 無錯誤時間差、重複提醒或 crash。
- [ ] 無 Critical/High 未決缺陷；任何例外有明確 release decision。
- [ ] 證據不含禁止資料且另一 tester 可依步驟重現。

## Verification artifacts

- [ ] `docs/testing/` 下的 matrix 與環境說明
- [ ] 每個 failed row 的 GitHub issue link
- [ ] 最終 build/test 命令與版本
- [ ] `git diff --check`

## Data/schema impact

無。測試資料必須是人工 fixture，驗收 artifacts 不得保存使用者內容。

## Completion report

- [x] Changes/artifacts: Created `docs/testing/windows-manual-acceptance-matrix.md` (17 scenario rows with step definitions), `docs/testing/windows-manual-acceptance-evidence-template.md`. Modified `docs/testing/test-plan.md` (reference to matrix), `docs/known-limitations.md` (BLOCKED scenarios noted).
- [x] Matrix summary: Windows 11 (build 26200), single 1920x1080 @100% DPI. 17 scenarios defined. 0 PASS (all require human GUI observation per spec rule), 17 BLOCKED (11 need dual monitor/mixed-DPI, 4 need keyboard/GUI interaction, 1 needs work interval to expire, 1 covered by #22 automation). No FAIL observed app crash-free with ~109 MB working set, 413 handles, 18 threads, responding.
- [x] Known limitations/open defects: Multi-monitor, mixed-DPI, primary switch, reconnect scenarios blocked due to single-monitor hardware. Focus-typing, fullscreen-video, break-guide, sleep-resume require human tester at real Windows desktop.
- [x] Data/schema impact: None.
