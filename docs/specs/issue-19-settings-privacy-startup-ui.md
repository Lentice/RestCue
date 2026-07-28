# Issue #19 — 完成設定 UI、隱私說明與開機啟動

## Goal

提供可驗證的設定 UI、清楚的隱私／非醫療說明與可診斷的 Windows 開機啟動控制，
同時維持前景程式名稱蒐集預設關閉。

## Dependencies and governing rules

- Blocked by #8、#10、#18。
- UI 必須提供工作間隔、自然停頓、最大等待、Break duration、Snooze、Idle、
  Passive Pause、retry cooldown、提醒顯示時長、四級 debt threshold、Break
  Guide mode、前景程式名稱 opt-in 與開機啟動。
- 必須明示資料預設只留本機，RestCue 不是醫療工具，也不保證治療或預防疾病。
- 開機啟動方式必須先有 ADR；若尚未核准，先停在 ADR review，不自行混用多種機制。

## Scope

- App settings UI 綁定 #18 model/validator/repository。
- 顯示 timing、debt、Break Guide 與 privacy settings 的合法控制項。
- 隱私聲明、非醫療聲明與實際收集行為保持一致。
- 一種明確、可測且可移除的 current-user startup registration。

## Out of scope

- 管理員級 system-wide startup、背景服務、scheduled telemetry。
- 資料透明明細（#20）、匯出／清除（#21）。
- 讓 UI 自行修正或默默 clamp 非法 domain values。

## Execution checklist

- [ ] 建立 view model/application service；code-behind 不持有 timing/default/validation。
- [ ] UI 控制範圍來自 #18 契約，跨欄位錯誤可定位且不丟失原有效設定。
- [ ] 保存成功後明確定義立即生效或下週期生效，不重建 tracker 丟失 Need。
- [ ] Foreground process collection opt-in 使用清楚文案且預設 off。
- [ ] Privacy Notice 列出「收集」與「絕不收集」，以及資料只留本機。
- [ ] 顯示非醫療工具聲明，不做健康效果承諾。
- [ ] ADR 比較 startup task/registry/shortcut 等方案並選一種。
- [ ] startup enable/disable/query 封裝在 Infrastructure，可用 fake 測試。
- [ ] startup registration failure 可診斷但不 modal、不搶焦點、不影響其他設定保存。

## Acceptance checklist

- [ ] UI 只能保存合法值，並顯示清楚非責備錯誤。
- [ ] 重新啟動後所有設定 round-trip，隱私 opt-in 維持使用者選擇。
- [ ] 前景程式名稱蒐集在 fresh install/recovery/migration 後均預設關閉。
- [ ] 開機啟動可啟用、停用、查詢且重複操作 idempotent。
- [ ] 權限／registration failure 可被診斷，不造成 crash 或額外 popup。
- [ ] timing/default 不存在於 UI logic。

## Verification

- [ ] Core validator + App view-model tests
- [ ] Infrastructure startup-registration tests
- [ ] Windows 手動 settings/startup smoke test
- [ ] `dotnet build RestCue.sln`
- [ ] `dotnet test RestCue.sln --no-build`
- [ ] `git diff --check`

## Data/schema impact

使用 #18 settings document；startup registration 會改使用者 Windows 設定，須在
完成報告列出精確位置、移除與失敗行為。不新增 usage data。

## Completion report

- [ ] Changes
- [ ] Tests
- [ ] Known limitations（含 startup mechanism）
- [ ] Data/schema impact
