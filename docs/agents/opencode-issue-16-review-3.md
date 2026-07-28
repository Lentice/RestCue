# Issue #16 — Independent Review 3 (after review-2 fixes)

**Reviewer:** OpenCode (independent review agent)  
**Date:** 2026-07-29  
**Scope:** All uncommitted files for issue #16 — Core types, Infrastructure repo/migration, App wiring/BackgroundUsageEventWriter, all tests, ADR-0005.

**Build:** `dotnet build RestCue.sln` → 0 errors, 0 warnings.  
**Test:** `dotnet test RestCue.sln --no-build` → 344/344 Core, 62/62 App, 40/40 Infrastructure (all pass).  
*Note: Core tests increased from 333 (review-1) to 344 (+11 new event seam tests). Infrastructure holds at 40.*

---

## Review-1 & Review-2 finding confirmation

| # | Source | Severity | Description | Status | Evidence |
|---|--------|----------|-------------|--------|----------|
| R1-1 | review-1 | HIGH | UTC not normalised | **FIXED** — `.ToUniversalTime()` in `SqliteUsageEventRepository.cs:41` | `WriteAsync` line 41 |
| R1-2 | review-1 | MEDIUM | Silent catch on repo creation | **FIXED** — `Trace.TraceError` in `App.xaml.cs:139` | `App.xaml.cs:137-141` |
| R1-3 | review-1 | LOW | `ReadWriteCreate` → `ReadWrite` | **FIXED** — `SqliteUsageEventRepository.cs:20` | line 20 |
| R1-4 | review-1 | LOW | Spec implied debt-level index | **FIXED** — spec line 43-45 updated | `issue-16-v13-usage-event-persistence.md:49-50` |
| R2-1 | review-2 | MEDIUM | Spec required events Core can't emit | **FIXED** — 6 new event seams added to `WorkCycleTracker`; ADR-0005 updated with full 17-event table; `WorkCycleTrackerNewEventSeamTests` (11 tests) confirm firing conditions | `WorkCycleTracker.cs:167-175` |
| R2-2 | review-2 | MEDIUM | `JsonElement?` payload not closed | **FIXED** — `UsageEventPayload` discriminated union with `ReminderDismissedPayload`, `RestDebtLevelChangedPayload`; `UsageEvent.cs:7` uses `UsageEventPayload?` | `UsageEvent.cs:7`, `UsageEventPayload.cs:6-10` |
| R2-3 | review-2 | LOW | `ManualStartBreak` silent | **FIXED** — `ManualStartBreak()` fires `BreakStarted` at `WorkCycleTracker.cs:327` | line 327 |
| R2-4 | review-2 | LOW | Orphaned fire-and-forget tasks | **FIXED** — `BackgroundUsageEventWriter` with `Channel<WriteRequest>` replaces `_ = Task` pattern | `BackgroundUsageEventWriter.cs` |
| R2-5 | review-2 | LOW | `Trace.TraceError` listener gap | **FIXED** — `BackgroundUsageEventWriter` accepts `Action<string>? onError` seam; production passes `msg => Trace.TraceError(msg)` | `BackgroundUsageEventWriter.cs:15-16`, `App.xaml.cs:145` |
| R2-6 | review-2 | LOW | `RecoveredFromCorruption` flag misleading | **FIXED** — XML doc comments on `SettingsLoadResult.cs:7-19` detail both recovery paths | `SettingsLoadResult.cs:7-19` |

**All 10 findings from review-1 and review-2 are confirmed fixed.** ✓

---

## New findings (ordered by severity)

### M1. MEDIUM — `BackgroundUsageEventWriter.Dispose` does not reliably drain buffered writes before cancellation

**File:** `BackgroundUsageEventWriter.cs:34-45`

```csharp
public void Dispose()
{
    channel.Writer.TryComplete();   // step 1: complete channel
    cts.Cancel();                    // step 2: cancel immediately (no drain window)
    try
    {
        consumerTask.GetAwaiter().GetResult();  // step 3: wait for exit
    }
    catch (OperationCanceledException) { }
}
```

**Evidence:** `TryComplete()` marks the channel as complete (no new writes). The consumer task runs on a background thread. `cts.Cancel()` is called synchronously before the consumer gets a CPU slice to drain buffered items. The consumer's `ReadAllAsync(ct)` checks the cancellation token on its next `await foreach` iteration and throws `OperationCanceledException`, discarding any remaining queued writes.

**Impact:** The ADR-0005 (line 141) states: "On `Dispose()` the channel is completed and remaining items are drained (with cancellation support)." The drain step is effectively skipped — buffered events at shutdown are silently dropped. On normal app exit, this means 0–256 events (the full channel capacity) could be lost.

**Required fix:** Restructure `Dispose` to drain before cancel:

```csharp
public void Dispose()
{
    channel.Writer.TryComplete();
    // Give consumer a window to drain remaining items
    try { consumerTask.Wait(TimeSpan.FromSeconds(2)); }
    catch (AggregateException) { }
    if (!consumerTask.IsCompleted)
    {
        cts.Cancel();
        try { consumerTask.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
    }
}
```

Or, restructure `ConsumeAsync` to use `CancellationToken.None` for the drain phase and `ct` only for abort on shutdown hang.

---

### M2. MEDIUM — Shutdown cancellation aborts in-flight SQLite writes; last N events silently lost

**File:** `BackgroundUsageEventWriter.cs:36-37, 53`

`cts.Cancel()` in `Dispose` propagates to the `CancellationToken` passed to `repository.WriteAsync(..., ct)`. If a write is in progress when cancellation fires, `SqliteConnection.OpenAsync` or `ExecuteNonQueryAsync` may throw `OperationCanceledException`. The consumer's catch block logs the fixed diagnostic but the event is lost.

**Evidence:** `ConsumeAsync` line 53: `await repository.WriteAsync(request.EventType, request.OccurredUtc, request.Payload, ct);`  
`Dispose` line 37: `cts.Cancel();`  
The in-flight write is aborted → catch at line 55-58 logs → event lost.

**Impact:** Same as M1 — best-effort persistence, so single events on shutdown are acceptable. But the combination of M1 (buffered items not drained) + M2 (in-flight write aborted) means **every** event still in the channel or currently being written at shutdown is lost.

**Required fix:** As part of the drain phase in M1, use `CancellationToken.None` for the repository write during drain, but allow cancellation to abort if drain is stuck (e.g., database is locked).

---

### M3. MEDIUM — `IUsageEventRepository.WriteAsync` allows unvalidated `EventType`+`Payload` combinations; mismatches cause query-time failures

**Files:** `IUsageEventRepository.cs:5`, `SqliteUsageEventRepository.cs:26-30, 83-96, 98-113`

**Evidence:** The interface accepts any `UsageEventType` with any `UsageEventPayload?`. The serializer (`SerializePayload`) uses `payload switch` and maps unexpected types to `DBNull.Value` (silent data loss). The deserializer (`DeserializePayload`) uses `eventType switch` and creates the corresponding payload type. A type/payload mismatch (e.g., `ReminderDismissed` event type with `RestDebtLevelChangedPayload` JSON) would store a JSON structure that the deserializer cannot parse (`GetProperty("result")` on JSON with `previous`/`current` → `KeyNotFoundException`), making the entire query fail.

**Impact:** Production wiring is correct — only 2 payload-carrying event types exist and are correctly paired. But a future contributor could introduce a bug (wrong payload for event type) that would render the `usage_events` table unreadable via `QueryAsync` for the affected time range. No compile-time protection exists.

**Required fix:** Add validation in `SqliteUsageEventRepository.WriteAsync` that rejects invalid type/payload combinations:

```csharp
static readonly HashSet<UsageEventType> PayloadEventTypes = [UsageEventType.ReminderDismissed, UsageEventType.RestDebtLevelChanged];

public Task WriteAsync(UsageEventType eventType, DateTimeOffset occurredUtc, UsageEventPayload? payload, CancellationToken ct)
{
    if (payload != null && !PayloadEventTypes.Contains(eventType))
        throw new ArgumentException($"Event type {eventType} does not accept a payload.", nameof(payload));
    if (payload == null && PayloadEventTypes.Contains(eventType))
        throw new ArgumentException($"Event type {eventType} requires a payload.", nameof(payload));
    // ...
}
```

Alternatively, redesign the interface with separate write methods per event category (with/without payload).

---

### L1. LOW — `UnwireUsageEventPersistence` does not unsubscribe from tracker events

**File:** `App.xaml.cs:170-177`

**Evidence:** `UnwireUsageEventPersistence` disposes the writer and sets `_eventWriter = null`, but leaves all 17 event handler lambdas subscribed to `tracker` (lines 147-168). In `OnExit`, `_statusWindow.StopActivityTracking()` is called *after* `UnwireUsageEventPersistence`. If a pending `DispatcherTimer` Tick fires between these two calls, `_eventWriter` is null and `_eventWriter?.Write(...)` drops the event silently.

**Impact:** Harmless given the current shutdown order (timer Stop follows immediately), but fragile. A future reordering could cause events to fire on a disposed/disposed writer. No NRE risk due to `?.` operator.

**Required fix:** Unsubscribe all event handlers in `UnwireUsageEventPersistence`, or move `StopActivityTracking()` before `UnwireUsageEventPersistence()` in `OnExit`.

---

### L2. LOW — `BackgroundUsageEventWriter` has no unit tests

**Evidence:** No test file exists for `BackgroundUsageEventWriter`. The following behaviors are untested:
- Channel full → `DropWrite` drops oldest items (not just newest)
- `Dispose` drain behavior (M1/M2)
- Consumer error handling (write failure → `onError` callback)
- FIFO ordering with concurrent producers
- `Write` on completed channel is silently dropped

**Impact:** The class is a thin wrapper around `Channel<T>`, so risk is low. But the ADR-0005 explicitly describes behavior (ordered delivery, drain, bounded memory) that has no test coverage.

**Required fix:** Add focus tests for `BackgroundUsageEventWriter` covering: write ordering, channel full drop, dispose drain, write-failure error callback, no-throw on disposed write, and concurrent producer safety.

---

### L3. LOW — Channel-full drops produce no diagnostic

**File:** `BackgroundUsageEventWriter.cs:31`

**Evidence:** `channel.Writer.TryWrite(...)` ignores the return value. When the channel is full (`BoundedChannelFullMode.DropWrite`), `TryWrite` returns `false` and the item is silently dropped. Unlike repository write failures (which log via `onError`), channel-full drops have no diagnostic.

**Impact:** During a burst of events (e.g., rapid `RestDebtLevelChanged` firing), events are silently dropped. The ADR accepts this ("queues or drops if channel is full") but does not require a diagnostic. However, a `Trace.TraceWarning` on drop would aid debugging channel sizing.

**Required fix (optional):** Check the return value of `TryWrite` and invoke `onError` when dropping:
```csharp
if (!channel.Writer.TryWrite(...))
    onError?.Invoke("RestCue: usage event channel full; event dropped.");
```

---

### L4. LOW — Malformed-event test doesn't verify good event survivability

**File:** `tests/RestCue.Infrastructure.Tests/UsageEvents/SqliteUsageEventRepositoryTests.cs:231-258`

**Evidence:** The test `Single_malformed_event_does_not_corrupt_database` writes a valid event, inserts a malformed row via raw SQL, then verifies:
1. `QueryAsync` throws `JsonException`
2. The database file still exists

It does **not** verify that the valid event remains queryable (e.g., by opening a new connection and querying a narrower time range that excludes the malformed row). A file-existence check alone does not confirm the data is intact.

**Required fix (optional):** After the `Assert.ThrowsAnyAsync<JsonException>`, create a new `SqliteUsageEventRepository`, query a narrower range covering only the valid event, and assert it's returned successfully.

---

## Semantic event audit — verified correct

### Event firing truthfulness and order

All paths checked for correct event order and single-firing guarantees:

| Transition | Events fired (in order) | Verified at |
|---|---|---|
| Working → Idle (EnterIdle) | BreakCancelled¹ → CooldownEnded² → IdleStarted → RestDebtLevelChanged³ | `WorkCycleTracker.cs:674-701` |
| Idle → Working (TickIdle) | IdleEnded → ResetCycle⁴ | `WorkCycleTracker.cs:242-249` |
| ReminderVisible → BreakInProgress (StartBreak) | BreakStarted | `WorkCycleTracker.cs:301-311` |
| Any → BreakInProgress (ManualStartBreak) | BreakStarted | `WorkCycleTracker.cs:313-328` |
| BreakInProgress → Working (TickBreak) | ResetCycle⁴ → BreakCompleted | `WorkCycleTracker.cs:665-672` |
| BreakInProgress → Working (HandleUnlock/HandleResume) | BreakCancelled → ResetCycle⁴ | `WorkCycleTracker.cs:473-520` |
| BreakInProgress → Idle (EnterIdle) | BreakCancelled → CooldownEnded² → IdleStarted → RestDebtLevelChanged³ | `WorkCycleTracker.cs:674-701` |
| ReminderVisible → Working (Ignore) | CooldownStarted → ReminderDismissed(Ignored) | `WorkCycleTracker.cs:342-358` |
| ReminderVisible → Working (AutoDismiss) | CooldownStarted → ReminderDismissed(AutoDismissed) | `WorkCycleTracker.cs:742-758` |
| ReminderVisible → Snoozed | ReminderDismissed(Snoozed) | `WorkCycleTracker.cs:330-340` |
| Working → PendingReminder (cooldown expired) | CooldownEnded | `WorkCycleTracker.cs:546-587` |
| PendingReminder → ReminderVisible | CooldownEnded⁵ → ReminderShown | `WorkCycleTracker.cs:703-740` |
| Various → Disabled | CooldownEnded⁶ → Disabled | `WorkCycleTracker.cs:411-424` |
| Disabled → Working (Enable) | ResetCycle⁴ → Enabled | `WorkCycleTracker.cs:426-434` |

¹ Only if `CurrentPhase == BreakInProgress`  
² Only if `cooldownUntil.HasValue`  
³ Only if `previousLevel != Level0`  
⁴ ResetCycle fires: CooldownEnded (if active) → RestDebtLevelChanged (if non-zero)  
⁵ Only if `cooldownUntil.HasValue`  
⁶ Only if `wasCooldownActive`

### No double-firing guaranteed

All event-firing paths clear the corresponding state *before or after* the event, preventing double-fire:
- `CooldownEnded`: clears `cooldownUntil` immediately after fire in all paths except `ResetCycle` (uses `wasCooldownActive` flag).
- `BreakCancelled`: cleared by ResetCycle or phase change after firing.
- `IdleStarted`: `CurrentPhase` set to `Idle` before firing; `TickIdle` does not fire it.
- `IdleEnded`: fires once in `TickIdle`; phase changes to Working immediately after.

### Break completion vs cancellation — mutually exclusive

- `BreakCompleted` fires only from `TickBreak`/`TickActivityUnavailable` when break duration elapses.
- `BreakCancelled` fires only from `HandleUnlock`/`HandleResume`/`EnterIdle` interrupting a break.
- `HandleLock`/`HandleSleep` explicitly skip `BreakInProgress` in their phase conditions (lines 466, 496), so they don't cancel or complete a break.
- `BreakCompleted` test (`BreakCompleted_does_not_fire_BreakCancelled`) confirms mutual exclusion. ✓

### Cooldown lifecycle completeness

`CooldownStarted` fires in exactly 2 paths: `Ignore()` (line 356), `TryAutoDismiss()` (line 755).  
`CooldownEnded` fires in exactly 5 paths: `TryEnterPendingReminderFromWorking` (558), `EnterReminderVisible` (706), `ResetCycle` (781), `EnterIdle` (696), `Disable` (422).  
All 5 ending paths clear `cooldownUntil` via the same variable or a saved flag, ensuring single-fire. ✓

### Idle enter/exit — truthful, non-duplicate

- `IdleStarted`: fires exactly once per idle entry, from `EnterIdle` (line 697). Not fired on repeated `TickIdle`.
- `IdleEnded`: fires exactly once per idle exit, from `TickIdle` (line 246). Followed by `ResetCycle`.
- Known limitation (documented in spec completion report): `IdleEnded` not fired for direct `ResetCycle()` calls from `HandleLock`/`HandleSleep`. This is truthful — those paths don't pass through `TickIdle`.

---

## BackgroundUsageEventWriter audit

### Bounded-channel overflow reporting

**Channel:** `Channel.CreateBounded<WriteRequest>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropWrite })`  
- Capacity: 256 items (~17 KB assuming average 64-byte request).
- Overflow behavior: `DropWrite` — when full, the writer call fails (returns `false`) and the new item is rejected.
- **No diagnostic on overflow** — `TryWrite` return value is ignored. See finding L3.

### FIFO ordering

`Channel<T>` is `SingleReader = true` by default for bounded channels. The single consumer reads items in insertion order. Concurrent producers may interleave but FIFO order is preserved per-producer (channel uses a lock-free queue internally). **FIFO is correct.** ✓

### Concurrent producers

`Channel<T>.TryWrite` is thread-safe. Event handlers from `WorkCycleTracker` (all on Dispatcher thread) and potential future cross-thread producers are safe. **No issue.** ✓

### Dispose/drain

See findings M1, M2.

### No-write-after-dispose

After `Dispose()` calls `channel.Writer.TryComplete()`, all subsequent `TryWrite` calls return `false` and are silently dropped. The `Write` method ignores the return value. **Safe but silent.** ✓

### App subscription lifecycle

- `WireUsageEventPersistence`: called once in `OnStartup` (line 50). Subscribes 17 handlers. ✓
- `UnwireUsageEventPersistence`: called once in `OnExit` (line 67). Disposes writer, sets null. **Does not unsubscribe handlers.** See finding L1.
- No duplicate subscriptions: `WireUsageEventPersistence` is called once. ✓
- `MainWindow.StartActivityTracking` subscribes separate UI handlers — no overlap. ✓

---

## Event-type/payload closed-set verification

| UsageEventType | Payload required? | Payload type | Write validation | Read validation |
|---|---|---|---|---|
| ReminderShown | No | null | — | returns null ✓ |
| BreakStarted | No | null | — | returns null ✓ |
| BreakCompleted | No | null | — | returns null ✓ |
| BreakCancelled | No | null | — | returns null ✓ |
| PassivePauseDetected | No | null | — | returns null ✓ |
| ReminderDismissed | **Yes** | `ReminderDismissedPayload(ReminderResult)` | Serializes `{result}` ✓ | Parses `result` ✓ |
| IdleStarted | No | null | — | returns null ✓ |
| IdleEnded | No | null | — | returns null ✓ |
| CooldownStarted | No | null | — | returns null ✓ |
| CooldownEnded | No | null | — | returns null ✓ |
| Paused | No | null | — | returns null ✓ |
| Resumed | No | null | — | returns null ✓ |
| FocusModeStarted | No | null | — | returns null ✓ |
| FocusModeEnded | No | null | — | returns null ✓ |
| Disabled | No | null | — | returns null ✓ |
| Enabled | No | null | — | returns null ✓ |
| RestDebtLevelChanged | **Yes** | `RestDebtLevelChangedPayload(Previous, Current)` | Serializes `{previous,current}` ✓ | Parses `previous,current` ✓ |

**Closed-set guarantee:** Write goes through `SerializePayload` switch (only 2 arms + default to `DBNull.Value`). Read goes through `DeserializePayload` switch (only 2 arms + default null). Unknown payload types are silently converted to null on both paths. **No enforcement at the interface boundary.** See finding M3.

---

## Migration rollback/recovery/privacy audit

### Schema migration transaction

`SchemaMigrator.cs:21-45` — DDL and `PRAGMA user_version` inside `BeginTransactionAsync`/`CommitAsync`; `RollbackAsync` in catch. DDL is transactional in SQLite. ✓

### Version transitions

| From | To | Behaviour |
|---|---|---|
| 0 (fresh) | 2 | Creates both tables + indexes + sets version ✓ |
| 1 | 2 | Creates `usage_events` + indexes; settings preserved ✓ |
| 2 | 2 | No-op (returns at version check) ✓ |
| 3+ | — | Throws `UnsupportedSettingsSchemaException`; no writes/downgrade ✓ |

### Recovery separation

- `IsDatabaseCorrupt` (SqliteException 11/26) checked before `IsSettingsDocumentCorrupt` (Json/NotSupportedException/SettingsValidationException). ✓
- `RecoverFromCorruptionAsync`: full backup to `.bak`, recreates DB, saves defaults. ✓
- `RecoverSettingsOnlyAsync`: upserts only the `settings` row; `usage_events` preserved in-place. ✓
- `RecoveredFromCorruption` flag documented for both paths; `CorruptBackupPath` distinguishes them. ✓

### Operational failure non-destructiveness

- BUSY/LOCKED: `DefaultTimeout=1` ensures fast failure; `IsDatabaseCorrupt` does not match `SqliteErrorCode` 5/6 → propagated. ✓
- Test `Locked_database_propagates_operational_error_without_deleting_valid_settings`. ✓
- Test `Operational_failure_does_not_trigger_database_recovery`. ✓

### Privacy

- Payload contains only enum-name strings (`ReminderResult`, `RestDebtLevel`). ✓
- Test `Payload_does_not_contain_forbidden_fields` scans JSON for `windowTitle`, `clipboard`, `processName`, `url`. ✓
- Diagnostics are fixed non-sensitive strings. ✓
- No window titles, input, clipboard/screen content, URLs, document names, or process names in any column. ✓
- `CollectForegroundProcessNames` is opt-in, not used by event persistence. ✓

---

## Summary

| Severity | Count | Actionable |
|----------|-------|------------|
| MEDIUM   | 3     | Yes — Dispose drain gap (M1), shutdown write abortion (M2), unvalidated EventType+Payload pairing (M3) |
| LOW      | 4     | Yes — Missing unsubscribe (L1), no writer tests (L2), silent channel drops (L3), weak malformed-row test (L4) |

**Overall assessment:** All 10 findings from reviews 1 and 2 are fixed. The implementation adds the 6 new truthful event seams, the `BackgroundUsageEventWriter` channel architecture, and the `UsageEventPayload` discriminated union, all correctly wired and tested. New findings are primarily around the shutdown drain contract (M1/M2) and defensive interface design (M3). No safety, privacy, or data-corruption concerns in production paths.

**Recommendation:** Fix M1/M2 (dispose drain), add type/payload validation in M3, then address L1–L4 at convenience. Without M1/M2, the ADR-0005 drain guarantee is not met; with M3, a future type/payload mismatch could make the event log unreadable.

---

## Appendix: files reviewed

- `docs/specs/issue-16-v13-usage-event-persistence.md`
- `docs/adr/0005-usage-event-persistence.md`
- `docs/adr/0001-sqlite-settings-persistence.md`
- `docs/adr/0003-reminder-retry-cooldown-clock-separation.md`
- `docs/adr/0004-rest-debt-levels.md`
- `docs/privacy.md`
- `docs/product/design-spec.md`
- `docs/agents/opencode-issue-16-handoff.md`
- `docs/agents/opencode-issue-16-review.md`
- `docs/agents/opencode-issue-16-review-2.md`
- `AGENTS.md`
- `src/RestCue.Core/UsageEvents/UsageEventType.cs`
- `src/RestCue.Core/UsageEvents/UsageEvent.cs`
- `src/RestCue.Core/UsageEvents/UsageEventPayload.cs`
- `src/RestCue.Core/UsageEvents/IUsageEventRepository.cs`
- `src/RestCue.Infrastructure/UsageEvents/SqliteUsageEventRepository.cs`
- `src/RestCue.Infrastructure/Settings/SchemaMigrator.cs`
- `src/RestCue.Infrastructure/Settings/SqliteSettingsRepository.cs`
- `src/RestCue.Infrastructure/Settings/SettingsLoadResult.cs`
- `src/RestCue.Infrastructure/Settings/UnsupportedSettingsSchemaException.cs`
- `src/RestCue.Infrastructure/Settings/LocalSettingsPaths.cs`
- `src/RestCue.Core/Reminders/WorkCycleTracker.cs`
- `src/RestCue.Core/Reminders/ReminderDismissedEventArgs.cs`
- `src/RestCue.Core/Reminders/ReminderResult.cs`
- `src/RestCue.Core/Domain/RestDebtLevel.cs`
- `src/RestCue.Core/Settings/SettingsLoadResult.cs`
- `src/RestCue.App/App.xaml.cs`
- `src/RestCue.App/MainWindow.xaml.cs`
- `src/RestCue.App/Lifecycle/IStatusWindow.cs`
- `src/RestCue.App/UsageEvents/BackgroundUsageEventWriter.cs`
- `tests/RestCue.Infrastructure.Tests/UsageEvents/SqliteUsageEventRepositoryTests.cs`
- `tests/RestCue.Infrastructure.Tests/Settings/SchemaMigratorTests.cs`
- `tests/RestCue.Infrastructure.Tests/Settings/SqliteSettingsRepositoryTests.cs`
- `tests/RestCue.Core.Tests/Reminders/WorkCycleTrackerNewEventSeamTests.cs`
