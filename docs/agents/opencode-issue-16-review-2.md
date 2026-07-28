# Issue #16 — Independent Review 2 (after fixes)

**Reviewer:** OpenCode (independent review agent)  
**Date:** 2026-07-28  
**Scope:** All uncommitted files for issue #16 — same boundary as review-1.

**Build:** `dotnet build RestCue.sln` → 0 errors, 0 warnings.  
**Test:** `dotnet test RestCue.sln --no-build` → 62/62 App, 333/333 Core, 40/40 Infrastructure (all pass).  
*Note: Infrastructure tests increased from 38 (review-1) to 40 — 2 new tests added since review-1.*

---

## Review-1 finding confirmation

| # | Severity | Description | Status | Evidence |
|---|----------|-------------|--------|----------|
| 1 | HIGH | `SqliteUsageEventRepository` does not normalise `occurredUtc` to UTC | **FIXED** — line 39 now calls `.ToUniversalTime()` before `ToString("O")` | `SqliteUsageEventRepository.cs:39` |
| 2 | MEDIUM | `WireUsageEventPersistence` silently swallows repository creation errors | **FIXED** — line 137 now has `Trace.TraceError("RestCue: failed to create usage event repository.")` | `App.xaml.cs:136-138` |
| 3 | LOW | `ReadWriteCreate` mode should be `ReadWrite` | **FIXED** — line 18 now uses `SqliteOpenMode.ReadWrite` | `SqliteUsageEventRepository.cs:18` |
| 4 | LOW | Spec checklist implies debt-level index that doesn't exist | **FIXED** — spec line 43-45 now reads "Debt level 存於 JSON payload 中，無獨立索引" | `issue-16-v13-usage-event-persistence.md:43-45` |

All 4 review-1 findings are confirmed fixed. ✓

---

## Semantic event coverage audit

The issue spec (lines 23-25) requires at least these events persisted:
`PassivePauseDetected`, `IdleStarted`/`IdleEnded`, `BreakCompleted`/`BreakCancelled`, `ReminderSnoozed`/`Ignored`/`AutoDismissed`, cooldown-related semantics, `RestDebtLevelChanged`.

| Spec-required event | Core has distinct event? | Persisted? | Payload |
|---|---|---|---|
| PassivePauseDetected | ✓ `PassivePauseDetected` | ✓ | null |
| IdleStarted | **✗** No Core event in `WorkCycleTracker.EnterIdle()` | **✗** | — |
| IdleEnded | **✗** No Core event in `ResetCycle()` | **✗** | — |
| BreakCompleted | ✓ `BreakCompleted` | ✓ | null |
| BreakCancelled | **✗** No Core event (`HandleLock`/`HandleSleep`/`HandleResume` call `ResetCycle` silently) | **✗** | — |
| Snoozed/Ignored/AutoDismissed | ✓ `ReminderDismissed(ReminderResult)` | ✓ | `{"result":"Snoozed"\|"Ignored"\|"AutoDismissed"}` |
| cooldown semantics | **✗** Cooldown is internal state only; no event emitted | **✗** | — |
| RestDebtLevelChanged | ✓ `RestDebtLevelChanged` | ✓ | `{"previous":"LevelX","current":"LevelY"}` |

Additional events persisted that are not in the spec's minimum list: `ReminderShown`, `Paused`, `Resumed`, `FocusModeStarted`, `FocusModeEnded`, `Disabled`, `Enabled`.

The ADR-0005 table (lines 54-71) and handoff document justify these gaps: "Events without a distinct Core seam (IdleStarted/Ended, BreakCancelled, cooldown) are not invented; only truthful Core events are persisted." The spec itself does **not** explicitly resolve this gap.

---

## Findings (ordered by severity)

### 1. MEDIUM — Spec lists required events that Core cannot emit

**Files:** `docs/specs/issue-16-v13-usage-event-persistence.md:23-25`, `docs/adr/0005-usage-event-persistence.md:70-71`

The issue spec requires persistence of `IdleStarted`, `IdleEnded`, `BreakCancelled`, and cooldown-related events. The current `WorkCycleTracker` has no distinct event seam for any of these:
- `EnterIdle()` (line 656) silently zeroes debt and transitions to `Idle` — no `IdleStarted` event.
- `ResetCycle()` (line 730) silently transitions from `Idle` back to `Working` — no `IdleEnded` event.
- `HandleLock()`/`HandleSleep()`/`HandleResume()`/`HandleUnlock()` (lines 449-503) call `ResetCycle()` during a break — no `BreakCancelled` event.
- Cooldown is internal state (`cooldownUntil`) — no cooldown-started/ended event.

ADR-0005 and the handoff document acknowledge this and declare these events "not invented." But the **issue spec itself** still lists them as required without a note acknowledging the gap. A future reader of the spec will expect these events to exist.

**Required fix:** Either:
- (a) Add Core events for IdleStarted, IdleEnded, BreakCancelled and wire them to persistence (scope increase — adds 2 new events + 1 enum value + wiring); or
- (b) Update the issue spec to explicitly document these as deferred or out-of-scope, with a cross-reference to ADR-0005 section "Events without a distinct Core seam."

**Recommendation:** (b) is lighter and matches the ADR's explicit reasoning. (a) would be needed for issue #17 (statistics) if IdleStarted/BreakCancelled are required for accurate recalculation.

---

### 2. MEDIUM — `UsageEvent.Payload` is `JsonElement?` which is not a closed/typed contract

**File:** `src/RestCue.Core/UsageEvents/UsageEvent.cs:9`

```csharp
public sealed record UsageEvent(
    long Id,
    DateTimeOffset OccurredUtc,
    UsageEventType EventType,
    JsonElement? Payload);
```

The spec (line 40-41) requires "封閉的 event type 與 typed payload 契約；不得接受任意欄位 bag." ADR-0005 (lines 111-114) states "Payload contains only closed typed values."

However, `JsonElement? Payload` accepts **arbitrary JSON** — nothing at the type level prevents storing `{"windowTitle":"foo","userData":"bar"}`. The interface (`IUsageEventRepository.WriteAsync`) also takes `JsonElement?` with no constraints.

At runtime the production wiring only passes closed payloads (`ReminderResult` enum names and `RestDebtLevel` enum names), but there is no compile-time or interface-level enforcement. A future contributor could easily pass arbitrary JSON without triggering a compiler error.

**Required fix:** Replace `JsonElement?` with a discriminated payload type:
- Create a `UsageEventPayload` base class/sealed union, or
- Push payload serialization into the repository implementation and accept only closed typed args at the interface boundary.

Alternatively, document that runtime enforcement by convention is deliberate and accepted.

---

### 3. LOW — `ManualStartBreak` fires no persistence event

**File:** `src/RestCue.Core/Reminders/WorkCycleTracker.cs:305-319`

When a user clicks "Break Now" from the tray (`MainWindow.StartBreakNow` → `workCycleTracker.ManualStartBreak()`), the tracker transitions to `BreakInProgress` but fires **no Core event**. The persistence wiring (`App.xaml.cs`) therefore writes no `UsageEvent` for this action.

This means manually started breaks are invisible in the event log. Events like `BreakCompleted` may still fire later when the break duration elapses, but there is no record of *when* the manual break started or *that* it was user-initiated vs. triggered by the normal flow.

`StartBreak()` (called from the normal reminder flow, line 294-303) also fires no event — but in that flow the `ReminderShown` event precedes it, so the chain is inferable.

**Required fix:** Add a `BreakStarted` event to `WorkCycleTracker` that fires from both `StartBreak()` and `ManualStartBreak()`, map it to a `UsageEventType.BreakStarted` value, and subscribe in `WireUsageEventPersistence`.

---

### 4. LOW — Orphaned fire-and-forget tasks on rapid event sequences

**File:** `src/RestCue.App/App.xaml.cs:172-178`

```csharp
private void WriteEvent(UsageEventType type, JsonElement? payload = null)
{
    var repo = _eventRepository;
    if (repo == null) return;
    _ = WriteEventAsync(repo, type, payload);
}
```

`_ = WriteEventAsync(...)` discards the task. With rapid event sequences (e.g., `RestDebtLevelChanged` firing multiple times in consecutive ticks, or rapid Pause/Resume cycles), each call creates a separate orphaned async task. The `DefaultTimeout=1` in `SqliteUsageEventRepository` prevents hangs, and the `catch` in `WriteEventAsync` prevents exceptions, but:

- Orphaned tasks are not observable — a failure is logged but there is no way to detect that N writes have been lost.
- If many events fire rapidly (e.g., 60 ticks/second of some pathological case), the thread pool could accumulate discarded tasks. This is unlikely given `DispatcherTimer` at 1-second intervals and only a few event types changing per tick, but there is no protection.

**Required fix (optional):** Add a `SemaphoreSlim(1,1)` to serialize writes, or add a bounded `Channel<UsageEvent>` with a single consumer. This is **optional** — the current best-effort behaviour satisfies the spec requirement "write failures must not block Core state transitions." Document the trade-off if the semaphore is added.

---

### 5. LOW — `Trace.TraceError` diagnostic requires a configured listener

**File:** `src/RestCue.App/App.xaml.cs:137,191`

Persistence-failure diagnostics use `Trace.TraceError("RestCue: ...")`. If no `TraceListener` is configured (which is the default for a WPF app unless the `app.config` sets it up), these diagnostics are silently dropped.

Contrast with `ApplicationStartupFailureHandler` which accepts a diagnostic `Action<string>` — a more testable and explicit pattern.

**Severity rationale:** LOW because the message is intentionally non-sensitive and silent dropping is acceptable for fire-and-forget diagnostics. But the inconsistency with the startup failure handler pattern is worth noting.

**Required fix (optional):** Inject an `Action<string>` diagnostic callback into `WireUsageEventPersistence` for consistency with the rest of the startup path, or add a `system.diagnostics` trace listener in `App.config`.

---

### 6. LOW — `RecoveredFromCorruption` flag is misleading for settings-only recovery

**File:** `src/RestCue.Infrastructure/Settings/SqliteSettingsRepository.cs:111`

`RecoverSettingsOnlyAsync` returns `new(AppSettings.Default, RecoveredFromCorruption: true)` — but this is **not** database corruption. It is a settings-document recovery. ADR-0005 explicitly separates these two concepts (lines 98-108), but the flag name conflates them.

The caller (`App.xaml.cs` via `_startup.InitializeAsync`) likely uses `RecoveredFromCorruption` to show a one-time notification. Since both paths should show the notification, the behaviour is correct but the naming is misleading for future maintainers.

**Required fix:** Either rename the property to `Recovered` (more general) or add a separate `RecoveryKind` enum. For now, add a comment documenting that the flag covers both corruption and settings-document recovery.

---

## Areas reviewed — clean (no findings)

### Schema migration (transaction/rollback, idempotency, future rejection)

`SchemaMigrator.cs:21-45` — DDL inside `BeginTransactionAsync`/`CommitAsync`; `RollbackAsync` in `catch`. Version 0→2, 1→2, 2→2 (no-op), >2 (rejection) all verified. ✓

### UTC round-trip and ordering

`SqliteUsageEventRepository.cs:39,71-72` — `WriteAsync` normalises via `.ToUniversalTime()`; `QueryAsync` normalises both boundaries and read-back values. `ORDER BY occurred_utc, id` for deterministic ordering. ✓  
Tests `UTC_timestamp_round_trips_correctly`, `Non_Utc_offset_is_normalised_to_Utc`, `Query_boundary_is_normalised_to_Utc`, `Query_with_deterministic_ordering` all pass. ✓

### Recovery refinement (ADR-0001 amendment)

Exception-filter order is correct: `IsDatabaseCorrupt` (SqliteException {11,26}) before `IsSettingsDocumentCorrupt` (JsonException, NotSupportedException, SettingsValidationException).  
`RecoverSettingsOnlyAsync` preserves `usage_events` table intact. Tests confirm both recovery paths. ✓

### Privacy boundary

No forbidden fields in any payload or event_type across all 11 UsageEventType values. `Payload_does_not_contain_forbidden_fields` test scans all events. Production wiring only passes enum-name strings. ✓

### Duplicate subscription prevention

`WireUsageEventPersistence` called once at startup (`App.xaml.cs:50`). `UnwireUsageEventPersistence` called once in `OnExit`. `MainWindow.StartActivityTracking` subscribes separate UI handlers — no overlap. ✓

### Event write failure isolation

`WriteEventAsync` catches all exceptions, logs fixed non-sensitive message, does not unwind Core. `DefaultTimeout=1` prevents hangs. `Operational_failure_does_not_trigger_database_recovery` test verifies no data loss under BUSY/LOCKED. ✓

### Single malformed row handling

`Single_malformed_event_does_not_corrupt_database` test — malformed payload JSON throws `JsonException` in query, database file remains intact. No corruption recovery triggered. ✓

### Schema DDL correctness

All required indexes (`idx_usage_events_occurred_utc`, `idx_usage_events_type_time`) verified in `Usage_events_indexes_are_created` test. Table columns match spec. ✓

---

## Summary

| Severity | Count | Actionable |
|----------|-------|------------|
| MEDIUM   | 2     | Yes — spec gap for missing Core events; unchecked `JsonElement?` payload |
| LOW      | 4     | Yes — ManualStartBreak silent; orphaned tasks; Trace listener; misleading flag |

**Overall assessment:** Implementation quality is strong. All review-1 findings are fixed. 6 new findings identified — 2 medium (spec/type gaps) and 4 low (operational/documentation polish). No safety, privacy, or data-loss concerns. The most impactful finding is the spec-vs-Core event gap (#1): the spec must explicitly acknowledge which events are deferred, otherwise a future reader will expect IdleStarted/Ended/BreakCancelled/cooldown events to exist.

---

## Appendix: files reviewed

Same set as review-1 with the addition of `docs/agents/opencode-issue-16-review.md` (to confirm findings).
