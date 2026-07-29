# MVP v1.3 Dogfooding — Event Aggregates

## Recalculation method

All aggregates derived from `usage_events` table via app's #21 JSON export.

- **Date range**: TBD (full 5 workdays).
- **Timezone**: UTC for storage, converted to local for daily boundaries.
- **Event conditions**:
  - `BreakCompleted`: `event_type = 'BreakCompleted'`
  - `Idle reset`: `event_type = 'IdleEnded'` paired with preceding `IdleStarted`
  - `Passive Pause`: `event_type = 'PassivePauseDetected'`
  - `Snooze`: `payload.result = 'Snoozed'`
  - `Ignored`: `payload.result = 'Ignored'`
  - `AutoDismissed`: `payload.result = 'AutoDismissed'`
  - Debt levels: `RestDebtLevelChanged` payload with `previous`/`current`

## Full-period aggregates

*To be filled after 5 workdays.*

| Metric | Count | Notes |
|---|---|---|
| BreakCompleted | | |
| Idle reset | | |
| Passive Pause | | |
| Snooze | | |
| Ignored | | |
| AutoDismissed | | |
| Total reminder outcomes | | |
| Completion rate | | |

## Debt level analysis

*To be filled after 5 workdays.*

| Level | Arrivals | Completions | Completion rate | Notes |
|---|---|---|---|---|
| Level 0 | | | | |
| Level 1 | | | | |
| Level 2 | | | | |
| Level 3 | | | | |
| Level 4 | | | | |
