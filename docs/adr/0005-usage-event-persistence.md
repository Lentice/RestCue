# ADR-0005: Usage event persistence — v2 schema, append-only envelope, UTC ordering

## Context

Issue #16 adds persistence for v1.3 usage events to enable later daily-statistics
recalculation (issue #17). Existing schema v1 has only the `settings` key/value table.
A new `usage_events` table is needed alongside it. The database must migrate from v1→v2
transactionally, and ADR-0001's whole-database recovery for invalid settings must be
refined to preserve valid usage events.

## Decision

### Schema version 2

`PRAGMA user_version` is moved from 1 to 2. A reusable `SchemaMigrator` class handles
all schema transitions in a single transaction:

| From | To | Action |
|------|----|--------|
| 0 (new DB) | 2 | Create both `settings` and `usage_events` tables + indexes |
| 1 | 2 | Create `usage_events` table + indexes; `settings` preserved |
| 2 | 2 | No-op (idempotent) |
| > 2 | — | Throw `UnsupportedSettingsSchemaException`, no writes/downgrade |

### `usage_events` table

```sql
CREATE TABLE IF NOT EXISTS usage_events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    occurred_utc TEXT NOT NULL,
    event_type TEXT NOT NULL,
    payload TEXT
);

CREATE INDEX IF NOT EXISTS idx_usage_events_occurred_utc
    ON usage_events (occurred_utc);

CREATE INDEX IF NOT EXISTS idx_usage_events_type_time
    ON usage_events (event_type, occurred_utc);
```

- `id`: auto-increment integer; acts as deterministic tie-breaker when two events
  share the same `occurred_utc` timestamp.
- `occurred_utc`: ISO 8601 round-trip format (`O` specifier), UTC only (normalised
  at write and read).
- `event_type`: closed set of `UsageEventType` values, stored as string.
- `payload`: nullable JSON text for event-specific data. Deserialised back into
  closed typed `UsageEventPayload` subtypes on read.

### Typed payload contract

Payload is a closed discriminated union — not arbitrary JSON:

- `ReminderDismissedPayload(ReminderResult Result)`
- `RestDebtLevelChangedPayload(RestDebtLevel Previous, RestDebtLevel Current)`

No other payload types exist. The repository serialises/deserialises each subtype
deterministically based on `event_type`. No UI strings, metadata bags, window
titles, process names, or sensitive content may appear in any payload.

### Event envelope

Each persisted event corresponds to exactly one Core event firing:

| Core event | UsageEventType | Payload |
|---|---|---|
| `ReminderShown` | ReminderShown | null |
| `BreakStarted` (from `StartBreak`/`ManualStartBreak`) | BreakStarted | null |
| `BreakCompleted` | BreakCompleted | null |
| `BreakCancelled` (from `HandleResume`/`HandleUnlock`/`EnterIdle` interrupting a break) | BreakCancelled | null |
| `PassivePauseDetected` | PassivePauseDetected | null |
| `ReminderDismissed(Snoozed)` | ReminderDismissed | `ReminderDismissedPayload(Snoozed)` |
| `ReminderDismissed(Ignored)` | ReminderDismissed | `ReminderDismissedPayload(Ignored)` |
| `ReminderDismissed(AutoDismissed)` | ReminderDismissed | `ReminderDismissedPayload(AutoDismissed)` |
| `IdleStarted` | IdleStarted | null |
| `IdleEnded` | IdleEnded | null |
| `CooldownStarted` | CooldownStarted | null |
| `CooldownEnded` | CooldownEnded | null |
| `Paused` | Paused | null |
| `Resumed` | Resumed | null |
| `FocusModeStarted` | FocusModeStarted | null |
| `FocusModeEnded` | FocusModeEnded | null |
| `Disabled` | Disabled | null |
| `Enabled` | Enabled | null |
| `RestDebtLevelChanged` | RestDebtLevelChanged | `RestDebtLevelChangedPayload` |

Events without a distinct Core seam (BreakStarted prior to this issue, IdleStarted/Ended
prior to this issue, CooldownStarted/Ended prior to this issue) have been added to
`WorkCycleTracker` as truthful event seams. See `WorkCycleTrackerNewEventSeamTests`
for exact firing conditions.

### Query ordering

All queries return rows ordered by `(occurred_utc, id)` for deterministic ordering
within the same timestamp. This supports cross-day/stats recalculation in issue #17.

### UTC normalisation

`SqliteUsageEventRepository` normalises all timestamp parameters to UTC before
storage or query:
- `WriteAsync`: `occurredUtc.ToUniversalTime()` before `ToString("O")`
- `QueryAsync`: `from.ToUniversalTime()` and `to.ToUniversalTime()`
- Read-back: `DateTimeOffset.Parse().ToUniversalTime()`

This guarantees lexical `ORDER BY occurred_utc` comparison works correctly
regardless of caller offset. The same normalisation applies to query boundaries
so that range queries are consistent with stored UTC strings.

### Database mode

`SqliteUsageEventRepository` opens connections in `SqliteOpenMode.ReadWrite` (not
`ReadWriteCreate`). The schema is always pre-created by `SchemaMigrator.EnsureSchemaAsync`
(via `SqliteSettingsRepository` during startup). This prevents accidental orphan-database
creation and follows least-privilege principles.

### Recovery refinement (ADR-0001 amendment)

Previous behaviour: any corrupt/bad settings document triggered whole-database backup
and recreation, discarding all data.

New behaviour:
- **Database corruption** (SQLite error 11 CORRUPT or 26 NOTADB): full backup to
  `.corrupt-{timestamp}-{guid}.bak`, recreate database, save default settings.
- **Settings document corruption** (JsonException, NotSupportedException,
  SettingsValidationException): reset only the `settings` row to `AppSettings.Default`
  via upsert; `usage_events` table is preserved in-place.

`SettingsLoadResult.RecoveredFromCorruption` is `true` in both cases; check
`CorruptBackupPath` to distinguish (non-null only for database-level corruption).

### Write failure handling

#### BackgroundUsageEventWriter

The App layer uses a `BackgroundUsageEventWriter` that serialises writes through a
single bounded `Channel<WriteRequest>` with a background consumer task:
- Producers (event handlers) call `writer.Write(...)` which is non-blocking (queues
  or drops if channel is full). `TryWrite` return value is checked; drops invoke
  `onError` with a fixed diagnostic.
- A single consumer thread reads requests in FIFO order and calls
  `repo.WriteAsync` with `CancellationToken.None` (in-flight writes are never
  cancelled).
- On `Dispose()`:
  1. Channel is completed (`TryComplete()`).
  2. Consumer task is waited for up to 2 seconds to drain buffered items.
  3. If drain times out, `CancellationTokenSource.Cancel()` is called to abort
     remaining items (fallback only).
- Write failures log a fixed non-sensitive diagnostic via `Action<string> onError`.
- Shutdown order in `App.OnExit`: activity timer stopped first, then writer
  disposed (prevents event handlers firing during/after disposal).

This replaces the previous fire-and-forget `_ = Task` pattern, providing ordered
delivery, bounded memory use, drain-on-exit (best-effort), and observability of
both write failures and channel-full drops.

#### Diagnostic seam

`BackgroundUsageEventWriter` accepts an `Action<string>? onError` callback,
consistent with `ApplicationStartupFailureHandler`. Tests can inject a capturing
callback; production wiring passes `msg => Trace.TraceError(msg)`.

### EventType+Payload enforcement

`SqliteUsageEventRepository.WriteAsync` validates that event type and payload
combinations match the closed contract:
- `PayloadEventTypes` static set defines which types require payload
  (`ReminderDismissed`, `RestDebtLevelChanged`).
- If `payload != null` for a non-payload type: `ArgumentException`.
- If `payload == null` for a payload type: `ArgumentException`.
- If payload is present but wrong subtype (e.g. `ReminderDismissedPayload`
  for `RestDebtLevelChanged` event type): `ArgumentException` from the
  production wiring is caught by the writer; from direct API calls, the
  mismatch is caught at query time by `DeserializePayload` which throws
  `InvalidOperationException` on unexpected event types with stored JSON.

This prevents a future contributor from silently corrupting the event log
with mismatched type/payload combinations.

### Privacy

- Payload uses only closed typed values (enum names, level names).
- No window titles, input, clipboard/screen content, URLs, document names, or process
  names are stored in the `payload` or `event_type` columns.
- Diagnostics on write failure use only fixed non-sensitive messages.

## Alternatives considered

- **Aggregate-only statistics** were rejected because raw events are needed for
  recalculation (spec requirement).
- **Three separate event tables** (one per event category) add complexity without
  benefit for append-only storage.
- **JSON metadata bag in payload** was rejected for privacy and type-safety reasons.
  Payload is strictly typed at the Core boundary (`UsageEventPayload` discriminated
  union).
- **Fire-and-forget `_ = Task`** was replaced by `BackgroundUsageEventWriter` for
  ordered delivery and drain-on-exit semantics.

## Consequences

- Fresh installations create schema v2 directly.
- v1 databases upgrade in-place, preserving all settings.
- Single malformed settings document does not destroy valid usage events.
- Future schema versions read `user_version` and are rejected without writes.
- Query ordering supports deterministic recalculation across UTC day boundaries.
- `IUsageEventRepository` is in Core; SQLite dependency stays in Infrastructure.
- `UsageEventPayload` discriminated union enforces closed payloads at compile time.
- `BackgroundUsageEventWriter` provides non-blocking ordered writes with drain
  support.
- The App composition root subscribes to 17 tracker events (11 from the original
  set, plus 6 new truthful seams: BreakStarted, BreakCancelled, IdleStarted,
  IdleEnded, CooldownStarted, CooldownEnded).

## Review Trigger

Review this decision when:
- Issue #17 adds daily statistics recalculation requiring different query patterns.
- A new schema version (v3) adds indexes or columns.
- Multiple process access requires concurrent write handling.
- Retention/deletion policy is implemented for old events.
