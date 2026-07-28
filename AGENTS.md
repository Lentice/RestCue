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

## Working Style
- **Think before coding**: state assumptions explicitly; if uncertain, ask rather than guess; present multiple interpretations when ambiguity exists; stop when confused and name what is unclear.
- **Simplicity first**: use the minimum code that solves the problem; nothing speculative, no features beyond what was asked, no abstractions for single-use code.
- **Goal-driven execution**: define success criteria and iterate until verified; do not just follow steps.
- **Surface conflicts, do not average them**: if two patterns contradict, pick one based on which is more recent or more tested, explain why, and flag the other for cleanup. Do not blend conflicting patterns.
