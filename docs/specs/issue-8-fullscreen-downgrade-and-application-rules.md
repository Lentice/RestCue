# Issue 8 specification: full-screen downgrade and application rules

## Goal

Deliver low-interruption reminder handling for configured foreground applications and
full-screen contexts, without weakening RestCue's privacy or non-interruption
guardrails.

## Scope

- Provide a small, usable default application-rule set at startup. It must be consumed
  by the existing one-second activity polling loop; no settings UI or persistence work
  is part of this issue.
- Keep foreground process-name collection opt-in and disabled by default. Do not read,
  store, or log window titles, URLs, document names, input, clipboard, or screen data.
- Distinguish `TrayOnly` and `Silent` rule outcomes. `TrayOnly` presents a visible,
  non-activating tray cue. `Silent` presents no tray cue. A suppressed due reminder
  remains deferred so it can be presented normally when the context permits.
- Detect actual full-screen foreground windows through a testable Infrastructure Win32
  adapter. Ordinary maximized windows, auto-hide-taskbar maximized windows, and shell
  windows are not full-screen. A detection failure or unknown result must take the
  low-interruption path, but must not be represented as confirmed full-screen.
- Keep all Win32 interop in Infrastructure and preserve Core's fake-clock testability.
- Update the tray icon/state visibly for the low-interruption presentation, restore it
  when normal reminder/status resumes, and never steal focus, block input, or use a
  modal/full-screen interruption.
- Changing foreground context or rules must not recreate the tracker or reset already
  accumulated effective work time.

## Out of scope

- Settings UI, editing/persisting application rules, telemetry, history, or new data
  collection.
- Changes to user-owned product specifications under `docs/product/`.

## Acceptance checklist

- [ ] Startup passes usable default application rules into activity tracking.
- [ ] A matching opt-in foreground-process rule is applied during normal one-second
      polling (within two seconds).
- [ ] Process-name collection remains disabled by default; no forbidden data is read or
      retained.
- [ ] `TrayOnly` visibly changes the non-activating tray presentation.
- [ ] `Silent` clears/does not show that tray cue.
- [ ] Changing a pending suppression from `TrayOnly` to `Silent` or full-screen clears
      a previously visible tray cue.
- [ ] Deferred reminders are restored normally when suppression ends.
- [ ] Valid borderless monitor-geometry evidence identifies full-screen.
- [ ] Maximized, auto-hide-taskbar, shell, and unknown/failure paths do not produce a
      normal popup; unknown/failure is explicitly distinguishable from confirmed
      full-screen.
- [ ] Core contains no Win32 calls and accumulated time survives context/rule changes.
- [ ] Focused unit tests cover rules, tray semantics/restoration, full-screen,
      maximized, shell, failure/unknown, and tracker accumulation.
- [ ] `dotnet build RestCue.sln`, `dotnet test RestCue.sln --no-build`, and
      `git diff --check` succeed with `TEMP` and `TMP` set to repository `.tmp`.

## Constraints

Follow AGENTS.md and ADR-0002's availability-safe approach: unavailable evidence must
never cause a normal interruption or count unverified activity. Do not stage or commit
as part of implementation; final review and commit are owned by the primary agent.

## Data and schema impact

None. This issue must not add persistence, settings schema changes, or new collected
data.
