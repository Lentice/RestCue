# Issue #15 — 套用呈現強度政策與可及系統列狀態

## Goal

以 `min(DebtRecommendedIntensity, ContextCap, UserChannelCap)` 決定允許通道，
並以不只依賴顏色的靜態 tray 微狀態顯示 Level 0–4。

## Dependencies and governing rules

- Blocked by #8、#14。
- Debt 建議：Level 0 一般 tray；Level 1 靜態微狀態；Level 2 更明確的靜態
  icon/Tooltip；Level 3 可允許 edge popup；Level 4 可允許輕提示音 + popup。
- Effective intensity 永遠取 Debt 建議、Context cap、User channel cap 三者中最低者。
- Fullscreen 預設只允許靜態 tray；全螢幕聲音必須另行明確 opt-in，預設關閉。

## Scope

- Core 建立獨立 presentation-intensity policy。
- App 將有效 intensity 映射至 tray cue、邊緣 popup 與允許的輕提示音。
- Tray 使用靜態形狀／徽記或 Tooltip 文字區分 debt level。
- 最大等待只改 Timing，不提升 Intensity。

## Out of scope

- 重算 Need（#14）、設定 UI（#19）、持久化（#16）。
- 閃爍、呼吸、跳動、紅點、未讀計數或持續動畫。

## Execution checklist

- [ ] 建立 Debt 建議、ContextCap、UserChannelCap 的明確 enum/value object。
- [ ] 實作三者最低上限組合，不將 Timing 或 Need reset 混入 policy。
- [ ] Level 1/2 僅允許靜態 tray；Level 3 才可允許邊緣 popup。
- [ ] Level 4 只有在使用者允許且情境未靜音時才可加入聲音。
- [ ] 全螢幕、Silent/TrayOnly、Pause、Focus Mode 正確降低上限。
- [ ] 未知全螢幕狀態使用低干擾上限，不冒充 confirmed fullscreen。
- [ ] Tray level 以非色彩訊號區分，Tooltip 文案可由螢幕閱讀器理解。
- [ ] level/context 變更以事件更新一次；平時不持續重繪。

## Acceptance checklist

- [ ] 最大等待到期不讓 Level 1/2 越級顯示 popup。
- [ ] 全螢幕與 Silent 規則可抑制 popup，但 Need 持續存在。
- [ ] Tray 的 Level 0–4 與 Disabled 不只靠顏色辨識。
- [ ] 沒有持續動畫、閃爍、紅點或焦點竊取。
- [ ] policy unit tests 覆蓋所有 level × context cap × user cap 關鍵組合。
- [ ] App tests 覆蓋 tray mapping、suppression/release 與單次後續提醒。

## Verification

- [ ] Core/App targeted tests
- [ ] `dotnet build RestCue.sln`
- [ ] `dotnet test RestCue.sln --no-build`
- [ ] 手動鍵盤焦點與螢幕閱讀器／Tooltip smoke test
- [ ] `git diff --check`

## Data/schema impact

無。

## Completion report

- [ ] Changes
- [ ] Tests
- [ ] Known limitations（含非色彩辨識方式）
- [ ] Data/schema impact
