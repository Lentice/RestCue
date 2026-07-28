# Issue #10 — Break Guide 聲音／語音模式與失敗降級

## Goal

在 #9 的 Break Guide 上加入一般節奏提示音與簡短語音模式；任何音訊初始化、
播放或裝置切換失敗，都安靜降級為無數字視覺引導，且不改變完成／取消語義。

## Dependencies and governing rules

- Blocked by #9；開工前確認 #9 已關閉。
- Spatial Audio 是 Optional Experiment，不是本票交付。
- MVP 必須提供節奏提示音、簡短語音、無數字視覺三種模式；預設為節奏提示音。
- 預設 20 秒流程：開始提示「看向約 6 公尺外」、中段提示慢慢眨眼並放鬆肩膀、
  完成提示結束。語句可因在地化微調，但不可出現倒數。

## Scope

- Core 定義與平台無關的 Break Guide mode 與音訊失敗結果。
- Infrastructure/App 封裝可替換的音訊播放邊界，支援 fake 實作。
- 節奏提示音至少涵蓋開始、中段、完成；語音使用簡短固定提示。
- 音訊失敗後同一次引導立即切換至無數字視覺模式。

## Out of scope

- Spatial Audio、背景下載語音、雲端 TTS、音訊遙測。
- 額外錯誤彈窗、系統通知或焦點切換。
- 改變 #9 的 `BreakCompleted`／`BreakCancelled` 判定。

## Execution checklist

- [ ] 確認 #9 的生命週期是唯一完成判定來源，音訊層不得自行完成引導。
- [ ] 建立最小的音訊介面，App/Core 不直接依賴具體 Windows 播放 API。
- [ ] 實作節奏提示音模式，內容不含口述數字倒數。
- [ ] 實作簡短語音模式，內容不含剩餘秒數。
- [ ] 初始化、播放中斷、裝置不存在與 dispose 失敗均安全降級。
- [ ] 降級不重啟計時、不重複事件、不延長或縮短 Break Duration。
- [ ] 取消與 App shutdown 會停止／釋放播放資源。
- [ ] 預設模式與合法值留給 #18 持久化；本票只建立可注入的能力。

## Acceptance checklist

- [ ] 節奏提示音與語音均不暴露數字倒數。
- [ ] 所有可模擬音訊失敗均降級至無數字視覺模式。
- [ ] 失敗不顯示 modal/額外 popup、不搶焦點、不阻塞 Break Guide。
- [ ] 移除或替換音訊實作不影響完成與取消測試。
- [ ] fake audio 測試覆蓋成功、初始化失敗、中途失敗、取消與完成。

## Verification

- [ ] 受影響 Core/App 單元測試
- [ ] `dotnet build RestCue.sln`
- [ ] `dotnet test RestCue.sln --no-build`
- [ ] 手動拔除／停用輸出裝置 smoke test，確認無額外彈窗
- [ ] `git diff --check`

## Data/schema impact

無。模式持久化由 #18；不可記錄裝置名稱或音訊內容。

## Completion report

- [ ] Changes
- [ ] Tests
- [ ] Known limitations（含支援的 Windows 音訊能力）
- [ ] Data/schema impact
