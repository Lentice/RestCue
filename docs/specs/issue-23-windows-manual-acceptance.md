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

- [ ] Changes/artifacts
- [ ] Matrix summary
- [ ] Known limitations/open defects
- [ ] Data/schema impact
