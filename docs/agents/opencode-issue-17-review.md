# Issue #17 Review — Daily Statistics v1.3

**Reviewer**: opencode (deepseek-v4-flash)
**Date**: 2026-07-29
**Scope**: Spec correctness, domain semantics, privacy/focus guardrails, UI wiring, test coverage, build risk, DST/cross-midnight/repository-failure behavior

---

## Summary

**NOT OK** — 10 actionable findings (2 block build, 3 critical, 2 major, 3 test quality).

---

## Actionable Findings

### F-01 [BLOCKER] `DateTimeOffset.AddHours` called with 2 arguments (CS1501)

| Field | Value |
|---|---|
| File | `tests/RestCue.Core.Tests/UsageEvents/DailyStatisticsServiceTests.cs` |
| Lines | 168, 169, 357 |
| Evidence | `startOfDay.AddHours(10, 10)` — `DateTimeOffset.AddHours` takes exactly 1 argument (`double`). |
| Required fix | Replace with `.AddHours(10).AddMinutes(10)` (lines 168, 357) and `.AddHours(10).AddMinutes(30)` (line 169). |

### F-02 [BLOCKER] Fake tray icons missing `StatisticsRequested` event (CS0535)

| Field | Value |
|---|---|
| Files | `tests/RestCue.App.Tests/PresentationIntensityAppTests.cs:59`<br>
`tests/RestCue.App.Tests/TrayCueSuppressionTests.cs:53`<br>
`tests/RestCue.App.Tests/ApplicationLifecycleTests.cs:69`<br>
`tests/RestCue.App.Tests/WindowsTrayIconPhaseMappingTests.cs:168` |
| Evidence | Adding `StatisticsRequested` to `ITrayIcon` broke all existing fake implementations. The build output shows CS0535 for each. |
| Required fix | Add `public event EventHandler? StatisticsRequested;` (field, no invocation) to each fake tray icon class. |

### F-03 [CRITICAL] Default initial accumulator state is `true` instead of `false`

| Field | Value |
|---|---|
| File | `src/RestCue.Core/UsageEvents/DailyStatisticsService.cs:207` |
| Evidence | `DetermineInitialState` returns `true` when `lastStateChange is null` (no pre-day events). This causes work time accumulation from midnight even when the app has no record of the user being active. The empty-day test (`ComputeAsync_empty_day_returns_all_zeros`) would compute 24 h effective work time instead of `TimeSpan.Zero`. At least 6 tests depend on the accumulator starting `false`. |
| Required fix | Change `return true;` to `return false;` on line 208. The initial state should only be `true` when a pre-day event actually establishes a working state. |

### F-04 [CRITICAL] Pre-day events add segments to the current day's statistics

| Field | Value |
|---|---|
| File | `src/RestCue.Core/UsageEvents/DailyStatisticsService.cs:73,86,94,102` |
| Evidence | `HandleAccStop` is called unconditionally for pre-day events (e.g., `IdleStarted` before midnight). If `DetermineInitialState` returned `true`, `segmentStart` is `startOfDayUtc`, so `duration = preDayEventTime - startOfDayUtc` — time entirely in the previous day — is appended to `completedSegments`. This corrupts `EffectiveWorkTime`, `LongestContinuousWork`, and `AverageWorkCycleDuration`. |
| Required fix | Guard segment-accumulating calls with `if (inDay)`, or process only `dayEvents` in the main loop and rely on `DetermineInitialState` for initial state. |

### F-05 [CRITICAL] Unpaired reminders at end of day are silently dropped

| Field | Value |
|---|---|
| File | `src/RestCue.Core/UsageEvents/DailyStatisticsService.cs` — after the `foreach` loop (~line 156) |
| Evidence | Remaining entries in `pendingReminders` are never added to `reminderOutcomes`. The spec requires "未配對保留為未完成" (unpaired remain as incomplete). |
| Required fix | After the loop, drain `pendingReminders` and add each as `ReminderOutcomeEntry(shownAt, shownEventId, outcome: null, outcomeAt: null)`. |

### F-06 [MAJOR] FocusMode counted as work time, contradicts spec

| Field | Value |
|---|---|
| File | `src/RestCue.Core/UsageEvents/DailyStatisticsService.cs:149-153` |
| Evidence | Spec event mapping: "有效工作時間 | 只累計 Enabled 且不在 **FocusMode** … 區段的事件間隔". The code treats `FocusModeStarted`/`FocusModeEnded` as no-ops, so FocusMode time is accumulated as work. The test `ComputeAsync_work_includes_focus_mode` verifies this wrong behaviour. |
| Required fix | Call `HandleAccStop` on `FocusModeStarted` and `HandleAccStart` on `FocusModeEnded`. Correct the test expectation. |

### F-07 [MAJOR] Cross-midnight dismissal silently drops the reminder instead of recording null outcome

| Field | Value |
|---|---|
| File | `src/RestCue.Core/UsageEvents/DailyStatisticsService.cs:270` |
| Evidence | `TryPairReminder` dequeues the reminder before checking `inDay`. If the dismissal event is on the next day (`inDay == false`), the method returns early without recording the outcome. The reminder is dequeued but lost from both `pendingReminders` and `reminderOutcomes`. |
| Required fix | Move the `if (!inDay) return;` check before `pendingReminders.Dequeue()`, or record the entry with null outcome when `inDay` is false. |

### F-08 [TEST QUALITY] `ComputeAsync_longest_continuous_work` expects wrong value (20 min vs 1 h)

| Field | Value |
|---|---|
| File | `tests/RestCue.Core.Tests/UsageEvents/DailyStatisticsServiceTests.cs:177` |
| Evidence | With segments [Enabled→IdleStarted] = 1 h and [IdleEnded→Disabled] = 20 min, `LongestContinuousWork` should be 1 h, not 20 min. A trusted idle reset still counts the preceding work segment. |
| Required fix | Change expected value to `TimeSpan.FromHours(1)`. |

### F-09 [TEST QUALITY] `ComputeAsync_average_work_cycle` expects wrong value (30 min vs 1 h)

| Field | Value |
|---|---|
| File | `tests/RestCue.Core.Tests/UsageEvents/DailyStatisticsServiceTests.cs:195` |
| Evidence | Two 1-hour ended work segments give an arithmetic mean of 1 hour, not 30 minutes. |
| Required fix | Change expected value to `TimeSpan.FromHours(1)`. |

### F-10 [TEST QUALITY] `ComputeAsync_work_includes_focus_mode` contradicts spec

| Field | Value |
|---|---|
| File | `tests/RestCue.Core.Tests/UsageEvents/DailyStatisticsServiceTests.cs:351-366` |
| Evidence | The test asserts FocusMode time IS included in work time. The spec (FR-010 event mapping) explicitly excludes FocusMode from work time. |
| Required fix | After fixing F-06, update expected work time to exclude the FocusMode period (expected: 1 h 30 min). |

---

## Areas Verified Without Finding

| Area | Status | Notes |
|---|---|---|
| **Privacy guardrails** | PASS | No window titles, input, clipboard, URLs, or process names stored. Payload uses closed typed enums only. |
| **Focus guardrails** | PASS | Statistics page does not steal focus, block input, or use modal interruption. Window is `CanMinimize`, `CenterScreen`. |
| **Badge/unread/popup** | PASS | No badge, unread indicator, or auto-popup. Accessible only via tray menu "今日統計". |
| **Read-only statistics** | PASS | `DailyStatisticsService` calls `IUsageEventRepository.QueryAsync` only — no `WriteAsync`. UI wires a new `StatisticsWindow` on demand. |
| **Ignored ≠ AutoDismissed** | PASS | `ReminderDismissedPayload.Result` discriminated correctly; counts stored separately. |
| **PassivePause ≠ reset** | PASS | `PassivePauseDetected` increments its own counter, does not affect work segments. |
| **Debt history ordering** | PASS | Ordered by `(occurred_utc, id)` per spec and ADR-0005. |
| **Repository failure** | PASS | Caught in `ComputeAsync`; returns `DailyStatisticsStatus.Failure` with safe error message. |
| **Partial payload decode** | PASS | `TryGetReminderResult` / `TryGetDebtPayload` set `hasPartialData = true` on decode failure; returned as `PartialData` status. |
| **Unknown event types** | PASS | Unmatched `UsageEventType` values fall through to a no-op `break`. |
| **Schema impact** | PASS | No new tables, columns, or migrations. Read-only query against v2 `usage_events`. |
| **Timezone boundary** | PASS | `TimeZoneInfo.ConvertTimeToUtc` used for UTC boundary calculation. `DateTimeOffset` preserves offset. |
| **Cross-midnight state reconstruction** | PASS (partial) | `DetermineInitialState` correctly finds the last state-changing event from 1-day lookback. Default-state bug (F-03) must be fixed first. |
| **DST ambiguous/invalid** | NOT TESTED | No test exercises a DST spring-forward / fall-back boundary. Spec checklist requires this. |

---

## Build Verification

`dotnet build RestCue.sln` fails — blocked by F-01 and F-02.

---

## Known Gaps

1. **No DST transition tests**: the test suite has no case for `TimeZoneInfo` with an ambiguous or invalid local time at the day boundary.
2. **No negative-offset timezone tests**: only UTC and UTC+9 (Tokyo) are covered.
3. **No App-layer integration test**: `WireStatisticsCommand` and the `StatisticsWindow` are not covered by UI tests.
4. **`FormatTimeSpan` for ≥24 h**: If `EffectiveWorkTime` or `LongestContinuousWork` ever exceeded 24 h (which shouldn't happen with correct init state), `"25 小時 0 分鐘"` would display confusingly for a single day.
5. **Debt history display timezone**: `OccurredUtc.LocalDateTime` in `StatisticsWindow.xaml.cs:69` converts to machine-local time, not the timezone the user queried for.
