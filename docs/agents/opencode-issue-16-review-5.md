# Issue #16 — Final Review 5 (fresh audit)

**Reviewer:** OpenCode (independent review agent)  
**Date:** 2026-07-29  
**Scope:** Full uncommitted diff, tests, spec, ADR-0005, privacy contract. No code/test edits.

**Build:** `dotnet build RestCue.sln` → 0 errors, 0 warnings.  
**Test:** `dotnet test RestCue.sln --no-build` → 345/345 Core, 69/69 App, 40/40 Infrastructure (all pass).  
*Note: +1 Core test (`CooldownEnded_state_is_null_during_handler_on_all_paths`) and +1 App test (`Wire_unwire_re_wire_does_not_duplicate_writes`) added since review-4.*

---

## Review-4 finding re-confirmation

| # | Source | Severity | Description | Actual-code status | Evidence |
|---|--------|----------|-------------|-------------------|----------|
| L1 | review-4 | LOW | `UnwireUsageEventPersistence` doesn't unsubscribe handlers | **FIXED** — code has 17 `-=` lines, then `_eventWriter?.Dispose()`, then `_tracker = null` | `App.xaml.cs:174-191` |
| L5 | review-4 | LOW | `CooldownEnded` state inconsistency in `EnterReminderVisible` | **FIXED** — captures `wasCooldownActive` *before* `cooldownUntil = null`, fires *after* clear. All 5 paths now clear before fire. Test `CooldownEnded_state_is_null_during_handler_on_all_paths` confirms. | `WorkCycleTracker.cs:704-708` |

**Both review-4 findings are resolved in the current code.** The review-4 report was written against an intermediate state before these fixes landed.

---

## Prior review regression check (16 findings)

| Source | Count | Status |
|--------|-------|--------|
| Review-1 (4 findings) | 4/4 | All fixed ✓ |
| Review-2 (6 findings) | 6/6 | All fixed ✓ |
| Review-3 (7 findings) | 7/7 | All fixed ✓ |
| Review-4 (2 findings) | 2/2 | All fixed ✓ |

**Zero regressions detected.**

---

## Re-audit areas

### Schema migration / rollback / recovery
- `SchemaMigrator.EnsureSchemaAsync`: DDL inside `BeginTransactionAsync`/`CommitAsync`; `RollbackAsync` in `catch`. ✓
- v0→v2: creates both tables + indexes. v1→v2: creates `usage_events` + indexes. v2→v2: no-op. v3+: throws `UnsupportedSettingsSchemaException` before any writes. ✓
- Recovery exception-filter order: DB corrupt (SqliteException 11/26) → Settings corrupt (JsonException/NotSupportedException/SettingsValidationException). ✓
- `RecoverFromCorruptionAsync`: backup to `.bak`, recreate DB, default settings. ✓
- `RecoverSettingsOnlyAsync`: upsert only `settings` row; `usage_events` preserved. ✓
- Test `Invalid_settings_json_recovers_settings_only_preserving_usage_events`. ✓
- Test `Future_schema_version_is_rejected_without_downgrade_or_deletion`. ✓

### Typed event coverage / pairing / privacy
- 17 `UsageEventType` values, 17 distinct event seams in `WorkCycleTracker`. ✓
- `UsageEventPayload` discriminated union: `ReminderDismissedPayload`, `RestDebtLevelChangedPayload`. ✓
- `WriteAsync` validates type/payload pairs via `PayloadEventTypes` HashSet; throws `ArgumentException` on mismatch. ✓
- `SerializePayload`/`DeserializePayload` switches handle only 2 known types; unexpected types get `DBNull`/throw. ✓
- No forbidden fields in any payload column (`result`, `previous`, `current` — enum names only). ✓
- `Payload_does_not_contain_forbidden_fields` test scans all event types for `windowTitle`, `clipboard`, `processName`, `url`. ✓
- Diagnostics are fixed non-sensitive strings. `CollectForegroundProcessNames` not used. ✓

### Ordered writer overflow / FIFO / drain / shutdown
- `BackgroundUsageEventWriter`: `Channel<WriteRequest>` bounded at 256, `DropWrite`, `SingleReader=true`. ✓
- FIFO guaranteed; test `Write_preserves_FIFO_ordering` confirms. ✓
- `TryWrite` return value checked; `onError` invoked on drop. ✓
- `Dispose`: `TryComplete()` → `consumerTask.Wait(2s)` drain → `cts.Cancel()` fallback. ✓
- Consumer passes `CancellationToken.None` to `repo.WriteAsync` — in-flight writes never cancelled. ✓
- Shutdown order in `OnExit`: `StopActivityTracking()` → `UnwireUsageEventPersistence()` (dispose writer) → `_lifecycle.Dispose()`. No events can fire during/after dispose. ✓
- ADR-0005 drain guarantee fully met. ✓

### Subscription lifecycle
- `WireUsageEventPersistence`: called once in `OnStartup`; subscribes 17 handlers. ✓
- `UnwireUsageEventPersistence`: unsubscribes all 17 handlers, disposes writer, nulls tracker. ✓
- `MainWindow.StartActivityTracking` subscribes separate UI handlers — no overlap with persistence. ✓

### Spec / ADR accuracy
- `docs/specs/issue-16-v13-usage-event-persistence.md`: All execution and acceptance checklists marked `[x]`. Event list matches actual 17 events. Known limitations documented. ✓
- `docs/adr/0005-usage-event-persistence.md`: Accurately describes schema v2, typed payload contract, UTC normalisation, recovery refinement, `BackgroundUsageEventWriter` drain semantics, type/payload enforcement. ✓
- Known limitations completion report matches current behaviour. ✓

---

## REViEW OK — zero actionable findings

All 18 findings from reviews 1–4 are confirmed fixed in the current code. No new actionable findings were identified in this final audit. The implementation matches the spec, ADR-0005, privacy contract, and all documented behaviours. This is ready for commit.

---

## Appendix: file inventory (uncommitted)

**Modified:**
- `docs/specs/issue-16-v13-usage-event-persistence.md`
- `src/RestCue.App/App.xaml.cs`
- `src/RestCue.App/MainWindow.xaml.cs`
- `src/RestCue.Core/Reminders/WorkCycleTracker.cs`
- `src/RestCue.Core/Settings/SettingsLoadResult.cs`
- `src/RestCue.Infrastructure/Settings/SqliteSettingsRepository.cs`
- `tests/RestCue.Infrastructure.Tests/Settings/SqliteSettingsRepositoryTests.cs`

**New (untracked):**
- `docs/adr/0005-usage-event-persistence.md`
- `docs/agents/opencode-issue-16-handoff.md`
- `docs/agents/opencode-issue-16-review.md` through `-5.md`
- `src/RestCue.App/UsageEvents/BackgroundUsageEventWriter.cs`
- `src/RestCue.Core/UsageEvents/` (4 files)
- `src/RestCue.Infrastructure/UsageEvents/SqliteUsageEventRepository.cs`
- `src/RestCue.Infrastructure/Settings/SchemaMigrator.cs`
- `tests/RestCue.App.Tests/UsageEvents/BackgroundUsageEventWriterTests.cs`
- `tests/RestCue.Core.Tests/Reminders/WorkCycleTrackerNewEventSeamTests.cs`
- `tests/RestCue.Infrastructure.Tests/Settings/SchemaMigratorTests.cs`
- `tests/RestCue.Infrastructure.Tests/UsageEvents/SqliteUsageEventRepositoryTests.cs`
