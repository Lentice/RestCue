# Issue #18 — 擴充並驗證 v1.3 設定模型

## Goal

提供 v1.3 timing、retry cooldown、debt thresholds 與 Break Guide mode 的
預設值、持久化與跨欄位驗證；非法設定不得取代目前有效設定。

## Dependencies and governing rules

- Blocked by #12、#13、#14。
- 目前 settings 保存於 SQLite `settings` key/value table 的 `app_settings` JSON；
  非法文件依既有安全恢復策略處理，但 operational failure 不得刪庫。

## Required defaults and ranges

- Work interval 20m，10–60m。
- Natural pause 5s，2–30s。
- Max wait 3m，0–10m。
- Break duration 20s，10–60s。
- Snooze 5m，1–30m。
- Idle threshold 2m，1–10m。
- Passive Pause 20s，10–120s，且嚴格小於 Idle。
- Reminder display 30s，5–120s。
- Retry cooldown 20m，1–60m。
- Debt thresholds 20/35/45/60m，嚴格遞增，Level 1 = Work interval。
- Break Guide mode：Cue / Voice / NumberlessVisual；Spatial Audio 不在 MVP。

## Execution checklist

- [ ] 將所有可調 timing 值放入 Core settings，不留在 WPF event handler。
- [ ] 使用具名型別或清楚欄位，避免同單位數值互相傳錯。
- [ ] validator 回傳可定位欄位／跨欄位的錯誤，不依賴 UI 字串。
- [ ] 驗證每個單欄位範圍與所有跨欄位不變量。
- [ ] 更新 settings document version/serialization，舊文件安全補入新 defaults。
- [ ] 非法或未知 mode 不覆蓋目前資料；依 ADR-0001 安全恢復。
- [ ] Foreground process collection 預設仍為 false。
- [ ] round-trip、舊版文件、缺欄位、額外欄位、非法組合均有 integration test。

## Acceptance checklist

- [ ] `PassivePauseThreshold < IdleThreshold`，相等也拒絕。
- [ ] debt thresholds 嚴格遞增，Level 1 精確等於 Work interval。
- [ ] 本 spec 列出的每個 range 邊界前／等於／後都有測試。
- [ ] 非法設定不會寫入或替換已保存的有效設定。
- [ ] v1 設定升級後使用 v1.3 defaults 且隱私 opt-in 維持關閉。
- [ ] Core/App 不出現重複 hard-coded timing defaults。

## Verification

- [ ] Core validator tests
- [ ] Infrastructure settings migration/round-trip tests
- [ ] `dotnet build RestCue.sln`
- [ ] `dotnet test RestCue.sln --no-build`
- [ ] `git diff --check`

## Data/schema impact

有 settings document version/shape 變更；若 SQLite table 不變也要回報文件版本、
default migration 與 backward/forward compatibility。

## Completion report

- [ ] Changes
- [ ] Tests
- [ ] Known limitations
- [ ] Data/schema impact
