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

- [ ] Changes/artifacts
- [ ] Tests/matrix
- [ ] Known limitations（含 signing/update）
- [ ] Data/schema impact
