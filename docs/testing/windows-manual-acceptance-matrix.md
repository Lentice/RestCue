# Windows Manual Acceptance Matrix

## Environment table

| Env ID | OS build | .NET SDK | App commit | Displays | DPI |
|--------|----------|----------|------------|----------|-----|
| W11-A | 10.0.26200.0 | 10.0.302 | 45fca87 | 1 (1920x1080) | 100% |

## Result table

| # | Env ID | Scenario | Steps ref | Expected | Actual | Result | Date | Reason / Required environment | Evidence | Issue |
|---|--------|----------|-----------|----------|--------|--------|------|------------------------------|----------|-------|
| 1 | W11-A | focus-typing | focus-typing | 打字期間字元全進原 app，reminder 非 active window | app 已啟動、tray icon 顯示 active。需真實鍵盤測試。 | BLOCKED | 2026-07-29 | 需要人類操作鍵盤與 GUI 驗證焦點行為 | | |
| 2 | W11-A | fullscreen-video | fullscreen-video | 降級為 tray 提示，無一般 popup | app 執行中。需真實全螢幕影片測試。 | BLOCKED | 2026-07-29 | 需要人類操作全螢幕影片測試 | | |
| 3 | W11-A | maximized-caption | maximized-caption | 未被誤判為 fullscreen | app 執行中，maximized 視窗行為由 #22 PrivacyDenylist/ProcessNameOptIn 自動化測試覆蓋 | BLOCKED | 2026-07-29 | 需要人類觀察驗證，無法由 CLI agent 完成 | | |
| 4 | W11-A | dpi-100 | dpi-variants | 文字不模糊、reminder 不被裁切 | app 啟動在 100% DPI 下正常，記憶體 ~109 MB，回應中 | BLOCKED | 2026-07-29 | 需要人類觀察驗證 UI 文字與裁切 | | |
| 5 | W11-A | dpi-125 | dpi-variants | 文字不模糊、reminder 不被裁切 | | | BLOCKED | 2026-07-29 | 無 125% DPI 螢幕 | | |
| 6 | W11-A | dpi-150 | dpi-variants | 文字不模糊、reminder 不被裁切 | | | BLOCKED | 2026-07-29 | 無 150% DPI 螢幕 | | |
| 7 | W11-A | dpi-200 | dpi-variants | 文字不模糊、reminder 不被裁切 | | | BLOCKED | 2026-07-29 | 無 200% DPI 螢幕 | | |
| 8 | W11-A | mixed-dpi | dpi-variants | mixed-DPI 下文字不模糊、reminder 不跨螢幕拉伸 | | | BLOCKED | 2026-07-29 | 需要第二螢幕且不同 DPI | | |
| 9 | W11-A | multi-monitor | multi-monitor | reminder 不被裁切、不跨螢幕拉伸 | | | BLOCKED | 2026-07-29 | 需要第二螢幕 | | |
| 10 | W11-A | primary-switch | multi-monitor | 切換 primary 後 reminder 正確定位 | | | BLOCKED | 2026-07-29 | 需要第二螢幕 | | |
| 11 | W11-A | display-reconnect | multi-monitor | 拔插螢幕後行為正常 | | | BLOCKED | 2026-07-29 | 需要第二螢幕 | | |
| 12 | W11-A | lock-unlock | lock-sleep | resume 後無時間回填、無 burst reminders | app 在 lock/unlock 後持續回應（Process Responding = True），無 crash | BLOCKED | 2026-07-29 | 需要人類觀察 unlock 後行為 | | |
| 13 | W11-A | sleep-resume | lock-sleep | resume 後無時間回填、無 burst reminders | | | BLOCKED | 2026-07-29 | 需要人類操作 sleep/resume | | |
| 14 | W11-A | break-guide-no-numbers | break-guide | 無數字倒數、不遮罩、不阻擋輸入 | | | BLOCKED | 2026-07-29 | 需要等待 work interval 到期觸發 break | | |
| 15 | W11-A | break-guide-cancel | break-guide | 取消不清除 Need | | | BLOCKED | 2026-07-29 | 需要等待 break 觸發後操作 | | |
| 16 | W11-A | audio-unavailable | audio-unavailable | 降級不彈窗 | | | BLOCKED | 2026-07-29 | 由 #22 自動化 cover（WindowsBreakGuideAudioPlayer try/catch 降級） | | |
| 17 | W11-A | tray-non-color | tray | 狀態可在不依賴顏色下辨識（tooltip + icon shape） | app tray icon 可見，status text 顯示 "RestCue – Eye Break Reminder" | BLOCKED | 2026-07-29 | 需要人類觀察 tray 狀態變化 | | |

## Step definitions

### focus-typing
1. 開啟 Notepad，輸入一段文字。
2. 等待 reminder 出現。
3. 在 reminder 顯示期間繼續打字。
4. 確認字元全部進入 Notepad，且 reminder 不是 active window。
5. 用滑鼠點 reminder 按鈕，確認鍵盤焦點不變。

### fullscreen-video
1. 開啟一個 borderless fullscreen 影片（無 WS_CAPTION）。
2. 等待 work interval 到期。
3. 確認沒有一般 popup 提醒出現，只有 tray 提示。

### maximized-caption
1. 將一個有 caption 的視窗最大化（如 Notepad、IDE）。
2. 等待 work interval 到期。
3. 確認出現一般 popup 提醒（非 fullscreen 降級）。

### dpi-variants
1. 設定 DPI 縮放為指定值。
2. 啟動 app。
3. 確認 reminder、break guide、statistics 視窗文字清晰、沒有裁切。

### multi-monitor
1. 連接第二螢幕。
2. 啟動 app。
3. 在兩螢幕間移動，確認 reminder 顯示在正確位置。
4. 切換 primary monitor。
5. 拔插螢幕。

### lock-sleep
1. 啟動 app，累積一些工作時間。
2. 鎖定工作站（Win+L）或讓系統 sleep。
3. 解鎖／喚醒。
4. 確認 accumulated work time 沒有異常回填、沒有連續跳出多個 reminder。

### break-guide
1. 等待 break 開始。
2. 確認 break guide 視窗沒有數字倒數。
3. 確認 break guide 不遮蓋全螢幕、不阻擋鍵盤輸入。
4. 完成 break 或取消 break。
5. 確認完成後清除 Need、取消後不清除。

### audio-unavailable
1. 啟動 app。
2. 停用或拔除音訊輸出裝置。
3. 等待 break guide 觸發。
4. 確認 app 不彈出錯誤視窗，安靜降級。

### tray
1. 啟動 app。
2. 觀察 tray icon 在不同 phase 的變化。
3. 確認 tooltip 文字可辨識目前狀態。
