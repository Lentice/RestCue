# OpenCode handoff — RestCue issue #16

Use the **implement** SKILL to implement GitHub issue #16.

## Read completely before editing

1. `AGENTS.md`
2. `docs/specs/issue-16-v13-usage-event-persistence.md`
3. `docs/agents/domain.md`
4. `docs/privacy.md`
5. `docs/product/design-spec.md` and relevant v1.3 sections 9.1–9.3, 10, 15,
   C-006/C-007
6. `docs/adr/0001-sqlite-settings-persistence.md`
7. ADRs 0003 and 0004
8. `SqliteSettingsRepository`, its tests, App composition/startup wiring, and
   actual typed events/results introduced by issues #11/#12/#14

## Required design

- Implement schema v2 with a reusable transaction-based migration runner.
- Fresh databases go directly to latest schema; v1 upgrades preserve `settings`
  exactly; repeated startup is idempotent; future versions are rejected without
  writes/downgrade.
- Use append-only `usage_events` with integer primary-key ordering, UTC round-trip
  timestamp, closed event type, and minimal typed payload. Index for chronological,
  type+time, and debt-level+time queries only where the schema supports it.
- No arbitrary metadata dictionaries or UI strings. Never store titles, input,
  clipboard/screen content, URLs, document names, or process name unless an existing
  explicit opt-in contract is actually wired (do not add it in this issue).
- Persist the spec-listed v1.3 events from production Core event seams. Where the
  current Core has no distinct cooldown event, model only truthful events/results;
  do not infer user behavior or invent duplicate events.
- Deterministic query order is `occurred_utc`, then integer id.
- Operational SQLite failures (BUSY/LOCKED/permission/I/O), future schema, and
  single malformed event/settings rows must never delete or recreate the database.
- Refine ADR-0001 recovery: invalid settings JSON may recover only the settings
  document/default, preserving valid usage events. Actual CORRUPT/NOTADB handling
  must be explicit and tested.
- Event write failures must not unwind or block Core state transitions. Diagnostics
  must be fixed/non-sensitive; do not include payload, database path, or raw exception.
- Keep Core free of SQLite; place storage contracts/types at the narrowest suitable
  boundary and avoid speculative abstractions.
- Add/update ADR with envelope, UTC/order semantics, v1→v2 transaction/rollback,
  compatibility, retention decision, privacy, and recovery behavior.
- Do not modify unrelated files or commit. Supervising agent owns full tests/commit.

## Test budget

Use TDD. Run focused Infrastructure migration/repository tests and minimal App wiring
tests only; do not run the full solution suite. Include fresh/v1/v2/future,
idempotency, rollback/failure, reopen/query ordering, all supported events, privacy
field/content, and non-destructive operational failures.

Update the issue spec checklist/report only for verified work. Report changed files,
focused tests, known migration/recovery limitations, and exact schema/data impact.
