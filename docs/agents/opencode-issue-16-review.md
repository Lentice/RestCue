# Issue #16 — Independent Review

**Reviewer:** OpenCode (independent review agent)  
**Date:** 2026-07-28  
**Scope:** All uncommitted files for issue #16 — `UsageEvent` types/repo (untracked), `SchemaMigrator` (untracked), `SqliteSettingsRepository` recovery refinement, App wiring, and all test files.

**Build:** `dotnet build RestCue.sln` → 0 errors, 0 warnings.  
**Test:** `dotnet test RestCue.sln --no-build` → 333/333 Core, 62/62 App, 38/38 Infrastructure (all pass).

---

## Findings (ordered by severity)

### 1. HIGH — `SqliteUsageEventRepository` does not normalise `occurredUtc` to UTC

**File:** `src/RestCue.Infrastructure/UsageEvents/SqliteUsageEventRepository.cs:39`

```csharp
command.Parameters.AddWithValue("$occurredUtc", occurredUtc.ToString("O"));
```

The value is stored in whatever offset the caller passes. Query ordering (`ORDER BY occurred_utc, id`) relies on lexical string comparison of ISO 8601 round-trip strings. This comparison is only correct when every value uses the same offset (UTC). If a non-UTC `DateTimeOffset` is ever stored, it will sort incorrectly relative to UTC values.

**Evidence:** The production caller (`App.xaml.cs:186`) always passes `DateTimeOffset.UtcNow` — so this is not currently broken. But the `IUsageEventRepository` interface and the repository itself have no safeguard or documentation requiring UTC.

**Required fix:**
- Normalise to UTC in `SqliteUsageEventRepository.WriteAsync` before formatting:  
  `occurredUtc.ToUniversalTime().ToString("O")`
- OR document the UTC requirement on `IUsageEventRepository.WriteAsync` and validate at the boundary.

---

### 2. MEDIUM — `WireUsageEventPersistence` silently swallows repository creation errors

**File:** `src/RestCue.App/App.xaml.cs:130-138`

```csharp
try
{
    _eventRepository = new SqliteUsageEventRepository(
        LocalSettingsPaths.DatabaseFile);
}
catch
{
    return;  // no diagnostic emitted
}
```

If `SqliteUsageEventRepository` construction fails (e.g. path-permission issue), the error is swallowed silently. The App continues with no event persistence and no diagnostic trace. The spec (issue-16 checklist item) requires: "event write failure 回報可診斷但不敏感的錯誤". While *write* failures are logged, *repository creation* failures are not.

**Required fix:** Add a `Trace.TraceError` with a fixed non-sensitive message in the catch block before returning.

---

### 3. LOW — `SqliteUsageEventRepository` opens connections in `ReadWriteCreate` mode

**File:** `src/RestCue.Infrastructure/UsageEvents/SqliteUsageEventRepository.cs:18`

```csharp
Mode = SqliteOpenMode.ReadWriteCreate,
```

The schema is always pre-created by `SchemaMigrator.EnsureSchemaAsync` (called during `SqliteSettingsRepository.LoadAsync` or `RecoverSettingsOnlyAsync`). The repository never needs to create the database. `ReadWrite` mode is sufficient and prevents accidental orphan-database creation.

**Required fix:** Change to `SqliteOpenMode.ReadWrite` (and verify the storage location still exists — `Path.GetFullPath` in the constructor ensures the directory component is resolved).

---

### 4. LOW — Spec checklist mentions "debt level + 時間" index but no such index exists

**File:** `docs/specs/issue-16-v13-usage-event-persistence.md:44`

> "建立時間、event type + 時間、debt level + 時間查詢所需索引"

Debt level is stored inside the JSON `payload` column. No index can cover queries filtering by debt level. The implementation correctly provides only two indexes (`idx_usage_events_occurred_utc`, `idx_usage_events_type_time`), matching ADR-0005.

**Required fix:** Update the spec checklist item to clarify that debt-level filtering is done client-side after time-range query (or add a separate `debt_level` column if query performance requires it). Remove the implication of a debt-level index if no schema change is intended.

---

## Areas reviewed — clean (no findings)

### Schema migration transaction/rollback

`SchemaMigrator.cs:21-45` — DDL and `PRAGMA user_version` inside `BeginTransactionAsync`/`CommitAsync`; `RollbackAsync` in `catch`. Future schema rejection (v > 2) throws `UnsupportedSettingsSchemaException` before any writes (line 15-16). Fresh DB (v0→v2), v1→v2, repeated-startup idempotency all verified. ✓

### Settings recovery data preservation

`SqliteSettingsRepository.cs:56-63` — Exception-filter order is correct: `IsDatabaseCorrupt` (SqliteException {11,26}) checked before `IsSettingsDocumentCorrupt` (JsonException/NotSupportedException/SettingsValidationException).  
`RecoverSettingsOnlyAsync` (line 91-112) — Preserves `usage_events` table intact, only resets `settings` row via upsert.  
`RecoverFromCorruptionAsync` (line 114-124) — Full backup to `.bak`, deletes WAL/SHM, recreates via `SaveAsync`. ✓  
Test `Invalid_settings_json_recovers_settings_only_preserving_usage_events` confirms usage events survive settings-only recovery. ✓  
Test `Corrupted_database_is_backed_up_and_replaced_with_safe_defaults` confirms full-corruption path. ✓  
Test `Locked_database_propagates_operational_error_without_deleting_valid_settings` confirms BUSY/LOCKED are not treated as corruption. ✓  
Test `Future_schema_version_is_rejected_without_downgrade_or_deletion` confirms v3 is rejected, no `.bak` written, no data loss. ✓

### Event coverage and truthful production wiring

All 11 applicable `WorkCycleTracker` events are subscribed in `WireUsageEventPersistence` (`App.xaml.cs:140-150`). `ReminderSuppressed` is correctly excluded (presentation-only). Mapping matches ADR-0005 table exactly. No invented events (IdleStarted/Ended, BreakCancelled, cooldown). ✓

Only production Core event seams are used — no UI strings, no reverse-engineered data. ✓

### Typed/closed payload privacy

`ReminderDismissed` payload: `{"result":"Snoozed|Ignored|AutoDismissed"}` — closed enum `ReminderResult`.  
`RestDebtLevelChanged` payload: `{"previous":"LevelX","current":"LevelY"}` — closed enum `RestDebtLevel`.  
All other events: null payload.  
No windowTitle, clipboard, processName, URL, document name anywhere in payloads or `event_type`. ✓  
Test `Payload_does_not_contain_forbidden_fields` scans all events for forbidden strings. ✓

### UTC ordering

`SqliteUsageEventRepository.QueryAsync` uses `ORDER BY occurred_utc, id` (line 61).  
`id INTEGER PRIMARY KEY AUTOINCREMENT` acts as deterministic tie-breaker for same-timestamp events. ✓  
Test `Query_with_deterministic_ordering` verifies same-timestamp events have increasing IDs. ✓  
Test `UTC_timestamp_round_trips_correctly` verifies round-trip with +08:00 offset. ✓  
Only blocked by Finding #1 (no UTC normalisation in write path).

### Async write loss/failure behaviour

`App.xaml.cs:184-191` — `WriteEventAsync` wraps `repo.WriteAsync` in try/catch. Error message is fixed, non-sensitive: `"RestCue: failed to persist usage event."`. Core not blocked (fire-and-forget via `_ = WriteEventAsync(...)`). ✓  
`SqliteUsageEventRepository.DefaultTimeout=1` ensures writes don't hang on locked database. ✓  
Test `Operational_failure_does_not_trigger_database_recovery` verifies BUSY/LOCKED write failures don't delete data. ✓

### Duplicate subscriptions

`WireUsageEventPersistence` called once (line 50). `MainWindow.StartActivityTracking` subscribes separate UI handlers. No duplicate persistence subscriptions. ✓  
Unwire in `OnExit` (line 67, `UnwireUsageEventPersistence`) removes all persistence handlers. ✓

### Query behaviour with malformed rows

Test `Single_malformed_event_does_not_corrupt_database` — malformed payload JSON throws `JsonException`; database file remains intact. ✓  
No database deletion or corruption recovery is triggered. ✓

### Other

- `LocalSettingsPaths.DatabaseFile` shared between `SqliteSettingsRepository` and `SqliteUsageEventRepository` — single database file. ✓  
- `SchemaMigrator` is static, stateless, thread-safe for distinct connections. ✓  
- Event ordering: `ResetCycle` fires `RestDebtLevelChanged` before `BreakCompleted` — both are fire-and-forget writes; acceptable for best-effort persistence. ✓  
- No Core project depends on SQLite; storage contracts are in `RestCue.Core.UsageEvents`. ✓  
- ADR-0005 and ADR-0001 alignment verified. ✓

---

## Summary

| Severity | Count | Actionable |
|----------|-------|------------|
| HIGH     | 1     | Yes — UTC normalisation |
| MEDIUM   | 1     | Yes — silent catch diagnostic |
| LOW      | 2     | Yes — `ReadWrite` mode / spec wording |

**Overall assessment:** Clean, well-structured implementation. Four minor actionable issues identified. No safety, privacy, or data-loss concerns in current production paths.

---

## Appendix: files reviewed

- `docs/specs/issue-16-v13-usage-event-persistence.md`
- `docs/adr/0005-usage-event-persistence.md`
- `docs/adr/0001-sqlite-settings-persistence.md`
- `docs/adr/0003-reminder-retry-cooldown-clock-separation.md`
- `docs/adr/0004-rest-debt-levels.md`
- `docs/privacy.md`
- `docs/agents/opencode-issue-16-handoff.md`
- `AGENTS.md`
- `src/RestCue.Core/UsageEvents/UsageEventType.cs`
- `src/RestCue.Core/UsageEvents/UsageEvent.cs`
- `src/RestCue.Core/UsageEvents/IUsageEventRepository.cs`
- `src/RestCue.Infrastructure/UsageEvents/SqliteUsageEventRepository.cs`
- `src/RestCue.Infrastructure/Settings/SchemaMigrator.cs`
- `src/RestCue.Infrastructure/Settings/UnsupportedSettingsSchemaException.cs`
- `src/RestCue.Infrastructure/Settings/SqliteSettingsRepository.cs`
- `src/RestCue.Infrastructure/Settings/LocalSettingsPaths.cs`
- `src/RestCue.Core/Reminders/WorkCycleTracker.cs`
- `src/RestCue.Core/Reminders/ReminderDismissedEventArgs.cs`
- `src/RestCue.Core/Reminders/ReminderResult.cs`
- `src/RestCue.Core/Events/RestDebtLevelChangedEventArgs.cs`
- `src/RestCue.App/App.xaml.cs`
- `src/RestCue.App/MainWindow.xaml.cs`
- `src/RestCue.App/Lifecycle/IStatusWindow.cs`
- `src/RestCue.App/Lifecycle/ApplicationStartup.cs`
- `src/RestCue.App/Lifecycle/ApplicationStartupFailureHandler.cs`
- `tests/RestCue.Infrastructure.Tests/UsageEvents/SqliteUsageEventRepositoryTests.cs`
- `tests/RestCue.Infrastructure.Tests/Settings/SchemaMigratorTests.cs`
- `tests/RestCue.Infrastructure.Tests/Settings/SqliteSettingsRepositoryTests.cs`
