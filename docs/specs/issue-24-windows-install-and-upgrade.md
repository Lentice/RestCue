# Issue #24 — 建立 Windows 安裝與升級流程

## Goal

提供 Windows 10/11 current-user 可安裝、啟動、升級與移除的 RestCue 套件，
並明確保留／移除設定與 usage data 的行為。

## Dependencies and governing rules

- Blocked by #23。
- 交付物必須包含可安裝 Windows App、原始碼、build instructions、測試、
  privacy notice、test plan、known limitations 與 checksum。
- 套件必須支援 Windows 10/11；不得要求與功能無關的管理員權限。
- 開工前必須新增「安裝包與更新策略」ADR；未核准前不得同時導入多個 installer。

## Required ADR decisions

- installer technology 與 Windows 10/11 支援範圍。
- per-user/per-machine、簽章與 publisher identity。
- .NET runtime self-contained/framework-dependent。
- upgrade identity/versioning、downgrade policy、rollback。
- startup registration ownership與 uninstall 行為。
- app data 在 uninstall 預設保留或刪除，以及使用者如何先使用 #21 清除。

## Scope

- reproducible packaging command 與 CI artifact。
- clean install、same-version repair/reinstall、upgrade、failed-upgrade rollback、uninstall。
- 版本資訊、安裝 log 與非敏感診斷指引。
- 與 #19 startup registration、#16 schema migration、#21 data clear 一致。

## Out of scope

- 未經 review 的自動背景更新服務、管理員服務或 telemetry。
- 在 uninstall 時默默刪除使用者資料。
- code signing secret 進 repo；CI secret provisioning 需另行安全設定。

## Implementation guidance for agents

本節是給實作 Agent 的操作指引，補充而非取代下方 Execution / Acceptance
checklist。所有標記「新增」的檔案目前不存在，需自行建立；標記「修改」的檔案
已存在於 repository。開工前請確認 #22、#23 已關閉（本票 Blocked by #23），
並注意 #17（DailyStatistics / StatisticsWindow）可能仍在進行中：不要把 #17
未完成的檔案當成缺陷，也不要在本票內修改它們。

### 檔案地圖

| 路徑 | 動作 | 變更內容 |
|---|---|---|
| `docs/adr/0006-windows-install-and-upgrade.md` | 新增 | 「安裝包與更新策略」ADR，格式依 `docs/adr/README.md`（Context、Decision、Alternatives、Consequences、Review Trigger）。若 #18–#23 已佔用 0006，改用下一個未使用的四位數編號 |
| `src/RestCue.App/RestCue.App.csproj` | 修改 | 加入 `Version` / `AssemblyVersion` / `FileVersion` / `Company` / `ApplicationIcon` 等 version metadata；若採 self-contained 亦加入 `RuntimeIdentifier`。目前僅有 `OutputType=WinExe`、`TargetFramework=net10.0-windows`、`UseWPF`、`UseWindowsForms`、`AssemblyName=RestCue`、`Product`、`Description`，**沒有**任何版本或 RID 屬性 |
| `Directory.Build.props` | 修改（可選） | 若要單一版本來源，把 `Version` 提到此處，讓 csproj 與 installer script 共用；已有 `Deterministic=true`，reproducible build 不必另設 |
| `packaging/windows/RestCue.iss` | 新增 | installer script 本體（若採 Inno Setup；改用其他技術則檔名與副檔名隨之調整，但仍放在 `packaging/windows/`） |
| `packaging/windows/build-package.ps1` | 新增 | 單一 reproducible 打包命令：publish → 產生 installer → 計算 SHA-256，輸出到 `artifacts/`（`.gitignore` 已忽略 `artifacts/`，不要把產物加入版控） |
| `src/RestCue.App/Properties/PublishProfiles/win-x64.pubxml` | 新增（可選） | 把 publish 參數固定在 profile，避免 CI 與本機命令漂移 |
| `.github/workflows/package.yml` | 新增 | 在 `windows-latest` 上跑打包並上傳 artifact + checksum。不要把打包塞進現有 `ci.yml`（該 workflow 目前只做 restore/build/test，維持它快速） |
| `.github/workflows/ci.yml` | 修改（僅必要時） | 只有在需要共用 setup 步驟時才動；勿改變既有 `dotnet-version: 10.0.x` 與 Release build/test 步驟語義 |
| `docs/testing/windows-install-upgrade-verification.md` | 新增 | install / upgrade / uninstall 驗收矩陣與 Execution record 表，格式參考 `docs/testing/windows-walking-skeleton-smoke-test.md` |
| `docs/testing/test-plan.md` | 修改 | 在 Manual Windows matrix 增列 install/upgrade/uninstall 情境 |
| `docs/privacy.md` | 修改 | 明述安裝位置、user data 位置，以及 uninstall 是否刪除資料 |
| `docs/known-limitations.md` | 修改 | 記錄簽章狀態、SmartScreen 影響、downgrade 不支援等限制 |
| `README.md` | 修改 | 加入 build / package / install / uninstall 命令 |

### 打包決策

本 spec 的 governing rules 已明確要求「開工前必須新增安裝包與更新策略 ADR」，
且未核准前不得同時導入多個 installer。以下每一項都必須在該 ADR 內以
Decision 形式寫下並附理由；不要在 script 裡隱含決定而不記錄。

受既有 ADR 約束的部分：`docs/adr/0001-sqlite-settings-persistence.md` 決定
settings 存放於本機 SQLite `restcue.db`，並定義 corruption recovery 只針對
`CORRUPT`/`NOTADB`，且**較新的 schema version 會被拒絕而不 downgrade**；
`docs/adr/0005-usage-event-persistence.md` 決定 schema v2、v1→v2 交易式
migration 與 append-only `usage_events`。這兩份 ADR 直接限制了 upgrade 與
downgrade policy，新 ADR 不得與其衝突，只能引用或提出明確的 amendment。

| 決策項 | 建議預設 | 理由 |
|---|---|---|
| installer technology | Inno Setup（單一 `.exe`） | 支援 per-user 安裝、script 可進版控、無需 MSI 授權模型與 admin；只選一種，符合「不得同時導入多個 installer」 |
| per-user vs per-machine | per-user（`%LocalAppData%\Programs\RestCue`） | governing rule 明禁不必要的 admin 權限；app 資料本來就在 `LocalApplicationData`，per-user 安裝與其一致 |
| publish 模式 | framework-dependent（依賴已安裝的 .NET 10 Desktop Runtime） | 套件小、修補由 runtime 更新負責。若目標機器不保證有 runtime，則改 self-contained 並在 ADR 記錄體積與修補責任的取捨 |
| RID | `win-x64` | csproj 目前**未指定** RID；`win-x64` 覆蓋主流 Windows 10/11 桌機。若要 ARM64 需另出一份 artifact 並在矩陣加列 |
| versioning | SemVer `MAJOR.MINOR.PATCH`，單一來源在 csproj/props，由 CI 注入 | 讓 installer 顯示的版本、`RestCue.exe` file version 與 release artifact 名稱不可能不一致 |
| upgrade detection | 固定 AppId/upgrade GUID + 版本比較，同版視為 repair/reinstall，較舊版拒絕（no downgrade） | 與 ADR-0001/0005「不 downgrade schema」一致；downgrade 會讓 v2 DB 面對只懂 v1 的程式 |
| code signing | 預設**未簽章**，並在 ADR 與 known limitations 明列為 release blocker | 憑證與 secret 不得進 repo；未簽章會觸發 SmartScreen，必須誠實記錄而非假裝已解決 |
| user data 位置 | 不變：`%LocalAppData%\RestCue\restcue.db`（見 `LocalSettingsPaths.DatabaseFile`），installer 不得搬移 | 資料目錄與程式目錄分離，才可能做到 upgrade 保留、uninstall 可選保留 |
| uninstall 資料行為 | 預設**保留** user data，並在 uninstall 畫面／文件指向 #21 的 export/clear 功能 | Out of scope 已明禁「在 uninstall 時默默刪除使用者資料」。若改為提供刪除選項，必須是明確 opt-in 且與 `docs/privacy.md` 文案一字對齊 |
| startup registration | 由 #19 的 app 內設定擁有；installer 不自行寫入，uninstall 需移除殘留項 | 避免 installer 與 app 兩處爭奪同一 registry 值 |

### 實作順序

1. 先確認乾淨 publish 可行，再碰任何 installer 檔案：

   ```powershell
   dotnet restore RestCue.sln
   dotnet build RestCue.sln --configuration Release --no-restore
   dotnet test RestCue.sln --configuration Release --no-build
   dotnet publish src/RestCue.App/RestCue.App.csproj `
     --configuration Release `
     --framework net10.0-windows `
     --runtime win-x64 `
     --self-contained false `
     --output artifacts/publish/win-x64
   ```

   `--framework net10.0-windows` 與 `--runtime win-x64` 分別對應 csproj 的
   `TargetFramework` 與本節選定的 RID。確認輸出含 `RestCue.exe`（
   `AssemblyName=RestCue`），且直接執行可進 tray。若改為 self-contained，把
   `--self-contained` 改成 `true` 並在 ADR 記錄。
2. 寫入 version metadata，重新 publish，確認 `RestCue.exe` 的 file version
   與 csproj 的 `Version` 相同（`(Get-Item .\RestCue.exe).VersionInfo`）。
3. 撰寫並提交 ADR。ADR 未成形前不要寫 installer script。
4. 建立 `packaging/windows/RestCue.iss` 與 `build-package.ps1`，讓單一命令從
   乾淨 clone 產生 installer 與 SHA-256（`Get-FileHash -Algorithm SHA256`）。
5. 在本機做 clean install → 啟動 → 退出 → uninstall 一輪，確認流程不需 admin。
6. 以上一個版本號打一包舊 installer，安裝並產生真實資料，再安裝新版，驗證
   upgrade 保留資料（見下節）。
7. 模擬 upgrade failure（例如 app 仍在執行、目標檔案被鎖定），確認舊版本與
   資料仍可用且有可定位的錯誤訊息。
8. 加入 `.github/workflows/package.yml`，確認 CI 產出的 artifact 與本機一致。
9. 最後才更新 `docs/privacy.md`、`docs/known-limitations.md`、
   `docs/testing/*` 與 `README.md`，讓文件描述已驗證過的實際行為。

### 資料與升級語義

- 使用者資料位置由 `src/RestCue.Infrastructure/Settings/LocalSettingsPaths.cs`
  單一決定：`Environment.SpecialFolder.LocalApplicationData` +
  `RestCue\restcue.db`。這個路徑與安裝目錄無關，所以 upgrade 只要換掉
  binaries 就能自然保留資料。**不要**在 installer 內複寫、搬移或備份此路徑。
- schema 相容性由 `SchemaMigrator.EnsureSchemaAsync` 負責：`LatestSchemaVersion`
  目前為 2，`PRAGMA user_version` 為 0 或 1 時在單一 transaction 內升級，等於
  2 時 no-op，大於 2 時丟 `UnsupportedSettingsSchemaException` 且不寫入。
  因此「安裝新版 → 首次啟動 → migration」是 upgrade 的真正資料路徑，
  installer 不執行任何 SQL。
- 一次 upgrade 必須存活的東西：`settings` 表的 `app_settings` 文件（含
  foreground process opt-in 的關閉狀態）、`usage_events` 全部列與其
  `(occurred_utc, id)` 排序、以及 #19 的 startup registration 狀態。
- uninstall 的資料行為必須與 `docs/privacy.md` 的文字完全一致。若文件說資料
  留在本機由使用者自行清除，installer 就不得刪；若要提供刪除選項，就必須先
  改文件並在 ADR 記錄。兩邊不一致本身就是驗收失敗。
- upgrade 失敗必須可診斷且不得刪除有效資料：失敗時保留舊 binaries 可執行
  狀態，log 只寫 package 版本、目標路徑與 error code，不得寫入 window title、
  輸入內容、URL 或文件名稱（`AGENTS.md` 產品護欄）。因為
  `SchemaMigrator` 已是交易式，migration 失敗會 rollback，DB 應維持原
  version；驗證時要實際確認 `PRAGMA user_version` 未被改壞。
- downgrade 不支援：安裝舊版後啟動會因 `user_version > LatestSchemaVersion`
  而拒絕啟動。這是既有設計，不要為此加入靜默刪 DB 的「修復」邏輯，只需在
  known limitations 誠實記錄。

### 驗證指引

需要覆蓋的三條主線，Windows 10 與 Windows 11 各一輪：

1. **Clean install**：從未安裝過 RestCue 的乾淨帳號／機器，安裝、啟動、
   確認 tray 出現、確認全程未出現 UAC 提示、確認資料庫在首次啟動後才建立。
2. **Upgrade over previous version**：先安裝上一個版本並產生真實 usage
   events 與非預設 settings，記下事件筆數與 `PRAGMA user_version`，再安裝新
   版，重新確認兩個數字與設定值。額外跑一次同版本 repair/reinstall。
3. **Uninstall**：確認 app 進程結束、tray 圖示消失、binaries 與 shortcut 移除、
   startup registration 移除，並確認 user data 的去留符合 privacy 文件。

證據表格請填在 `docs/testing/windows-install-upgrade-verification.md`：

| Date | OS build | App version / commit | Installer SHA-256 | Scenario | Steps | Expected | Actual | Result |
|---|---|---|---|---|---|---|---|---|
|  |  |  |  | Clean install (Win10) |  |  |  | PASS / FAIL / BLOCKED |
|  |  |  |  | Clean install (Win11) |  |  |  | PASS / FAIL / BLOCKED |
|  |  |  |  | Upgrade from previous |  |  |  | PASS / FAIL / BLOCKED |
|  |  |  |  | Same-version repair |  |  |  | PASS / FAIL / BLOCKED |
|  |  |  |  | Failed-upgrade recovery |  |  |  | PASS / FAIL / BLOCKED |
|  |  |  |  | Uninstall |  |  |  | PASS / FAIL / BLOCKED |

無法取得乾淨機器或第二個 OS 版本時，該列必須填 `BLOCKED`，並在同列或註腳
寫出 (a) 阻擋原因、(b) 需要的環境（例如「Windows 10 22H2 乾淨 VM 或未安裝過
RestCue 的本機帳號」）、(c) 已改用什麼替代驗證（例如新建本機使用者帳號）。
絕對不要把未執行的情境寫成 PASS，也不要用「應該可以」推論結果。完成報告的
Known limitations 必須複述所有 BLOCKED 列。

### 常見錯誤

- installer 要求 admin 權限（per-machine 目錄、寫 HKLM、`PrivilegesRequired=admin`）
  卻沒有功能上的需要，直接違反 governing rule。
- upgrade 過程刪除或覆蓋 `%LocalAppData%\RestCue\restcue.db`，把 usage events
  一起清掉；或在 uninstall 的清理步驟誤刪整個 `%LocalAppData%\RestCue`。
- uninstall 保留資料但 `docs/privacy.md` 寫「移除時一併刪除」（或反之）。
  文件與行為不一致等同驗收失敗，不是文件小疏漏。
- 未簽章 binary 觸發 SmartScreen／Defender 警告卻沒記錄，讓使用者以為套件損毀。
  必須寫進 known limitations 並列為 release blocker。
- 版本號在 installer script 或 workflow 裡 hardcode，與 csproj 的 `Version`
  漂移，導致 installer 顯示 1.3.0 而 exe 是 1.2.x。
- 把 `artifacts/` 下的 installer 或 publish 產物 commit 進 repo（`.gitignore`
  已忽略，不要用 `-f` 強加）。
- 打包時忘記 `--configuration Release`，或用 `dotnet build` 輸出目錄取代
  `dotnet publish` 輸出，導致缺少必要的 runtime 相依檔案。
- 在本票內順手改動 #17 未提交的 `DailyStatistics*` / `StatisticsWindow` 檔案。

### 逐步 checklist

- [ ] 確認 #23 已關閉，#22/#23 的驗收證據可查。
- [ ] 跑通 `dotnet restore/build/test`（Release）與上節的 `dotnet publish` 命令。
- [ ] 決定並在新 ADR 記錄「打包決策」表中的每一項，含理由與被拒選項。
- [ ] 在 csproj（或 `Directory.Build.props`）建立單一版本來源並驗證 exe 版本。
- [ ] 建立 `packaging/windows/` 下的 installer script 與 `build-package.ps1`。
- [ ] 確認單一命令可從乾淨 clone 重建 installer 並輸出 SHA-256。
- [ ] 新增 `.github/workflows/package.yml` 並確認 artifact 可下載。
- [ ] 執行 clean install / upgrade / repair / failed-upgrade / uninstall 六列驗證。
- [ ] 驗證 upgrade 前後 `PRAGMA user_version`、事件筆數與 settings 一致。
- [ ] 驗證安裝與升級 log 不含禁止資料且可定位 package/version/error。
- [ ] 確認 uninstall 行為與 `docs/privacy.md` 文案逐字一致。
- [ ] 更新 `docs/privacy.md`、`docs/known-limitations.md`、`docs/testing/*`、
      `README.md`。
- [ ] 把所有 BLOCKED 列與所需環境寫入完成報告的 Known limitations。
- [ ] 執行 `git diff --check`，不自行 commit、push 或關票。

## Execution checklist

- [ ] 撰寫並核准 installer ADR，選定單一技術與 identity。
- [ ] package 使用 Release build，版本可由 source/CI deterministic 注入。
- [ ] 支援 clean per-user install、launch、upgrade、uninstall。
- [ ] upgrade 前後 settings/events 通過 #16/#18 migration，失敗不破壞原 DB。
- [ ] 安裝／升級失敗 log 不含禁止資料，且可定位 package/version/error。
- [ ] uninstall 停止 app、移除 binaries/startup registration/shortcuts。
- [ ] user data 行為符合 privacy notice，UI/installer 文案不誤導。
- [ ] 產出 SHA-256/checksum；若簽章不在此環境完成，明列 release blocker。
- [ ] 文件化本機 build、CI build、install、upgrade、uninstall 與驗證命令。

## Acceptance checklist

- [ ] 乾淨 Windows 10/11 可安裝、啟動、退出與移除。
- [ ] 從上一支援版本升級保留相容 settings/events，並可安全 migration。
- [ ] 模擬 upgrade failure 後舊版本／資料仍可診斷且無靜默資料損失。
- [ ] uninstall 資料行為與 privacy notice/#21 一致。
- [ ] package 不需要不必要的 admin 權限，不加入 input/screen capture 能力。
- [ ] artifact 可由 documented command 重建，版本與 checksum 可追蹤。

## Verification

- [ ] Release `dotnet build` / `dotnet test`
- [ ] Win10 clean install/upgrade/uninstall matrix
- [ ] Win11 clean install/upgrade/uninstall matrix
- [ ] migration/rollback fixture verification
- [ ] package signature/checksum verification
- [ ] `git diff --check`

## Data/schema impact

Installer 本身不定義新 schema；upgrade 會觸發 #16/#18 migrations。完成報告必須
說明 install paths、startup registration、uninstall data retention 與 rollback。

## Completion report

- [x] Changes/artifacts: ADR-0007 (install/upgrade strategy), version metadata in `Directory.Build.props` + `RestCue.App.csproj`, `packaging/windows/RestCue.iss` (Inno Setup 7), `packaging/windows/build-package.ps1`, `.github/workflows/package.yml`, `docs/testing/windows-install-upgrade-verification.md`. Updated `docs/testing/test-plan.md`, `docs/privacy.md`, `docs/known-limitations.md`, `README.md`. Installer compiled: `RestCue-1.3.0-win-x64.exe` (3.27 MB, SHA-256 `0C342E88...`).
- [x] Tests/matrix: Release build/test passes (624/624). Publish verified (framework-dependent `win-x64`, version 1.3.0.0). Installer built from single command `.\packaging\windows\build-package.ps1`. Install/upgrade/uninstall scenarios BLOCKED (require human Windows GUI testing).
- [x] Known limitations: No code signing (SmartScreen warning), framework-dependent (requires .NET 10 Desktop Runtime), no downgrade support, all install/upgrade verification scenarios BLOCKED.
- [x] Data/schema impact: No new schema. Install path: `%LocalAppData%\Programs\RestCue`. User data: `%LocalAppData%\RestCue\restcue.db` (separate from binaries, preserved on uninstall). Startup registration owned by #19 app settings, not installer.
