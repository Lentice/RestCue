# AGENTS.md

RestCue is a Windows 10/11 eye-break reminder built with C#, .NET 10 LTS, and WPF.

## Product guardrails

- Never block keyboard or mouse input, steal focus, or use modal/full-screen interruption.
- Keep adjustable timing values out of UI logic.
- Never record window titles, input, clipboard or screen content, URLs, or document names.
- Foreground process-name collection is opt-in and disabled by default.
- Core timing logic must support a fake clock.
- Do not add out-of-scope features without a scope review.

## Development

Run `dotnet build RestCue.sln` and `dotnet test RestCue.sln`.

Every completed ticket must report changes, tests, known limitations, and data/schema impact.

## Agent skills

### Issue tracker

Work is tracked in GitHub Issues for `Lentice/RestCue`. See `docs/agents/issue-tracker.md`.

### Triage labels

Use the canonical triage label vocabulary. See `docs/agents/triage-labels.md`.

### Domain docs

This is a single-context repository. Read `CONTEXT.md` and relevant ADRs before changing domain behaviour. See `docs/agents/domain.md`.
