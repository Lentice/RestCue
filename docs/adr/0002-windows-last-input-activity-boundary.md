# ADR-0002: Windows last-input activity and privacy boundary

## Context

RestCue must distinguish effective work from Idle without reading or retaining input
content. The Idle threshold is configurable, Windows API calls can fail, and the
threshold boundary must be testable without waiting for real user input.

## Decision

Core owns `IUserActivityMonitor`, an availability-aware idle-duration sample, and the
threshold evaluator. Infrastructure implements the monitor with only
`GetLastInputInfo` and `GetTickCount`; it does not install input hooks or receive key
or mouse payloads. App polls the monitor once per second and presents the evaluated
Working or Idle status.

The evaluator treats the configured threshold as inclusive. An unavailable Windows
sample evaluates to Idle so unknown activity is never counted as effective work.
Tick-count subtraction uses unsigned wraparound semantics.

## Alternatives

- Keyboard and mouse hooks were rejected because they expose input events that RestCue
  neither needs nor is permitted to retain.
- Reading `Environment.TickCount64` alongside the 32-bit last-input tick was rejected
  because the different counters complicate wraparound handling.
- Treating API failure as Working was rejected because it would accumulate work time
  without evidence of activity.

## Consequences

- Tests can provide a fake `IUserActivityMonitor` and verify exact threshold and
  recovery behaviour.
- App displays Idle during transient API failure; it does not currently distinguish
  unavailable activity from genuine Idle in the status page.
- Session lock, sleep, and resume remain separate platform signals for a later slice.

## Review Trigger

Review when session and power events are integrated, when Windows changes the
last-input API contract, or if dogfooding shows that transient failures require a
separate user-visible state.
