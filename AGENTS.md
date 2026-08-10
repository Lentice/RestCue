# AGENTS.md

RestCue is a Windows 10/11 eye-break reminder built with C#, .NET 10 LTS, and WPF. Local SQLite only —
no network, no telemetry, no account.

This file loads in every session, so it holds only what every task needs. Load the linked documents when
your task touches their subject, not before.

## Product guardrails

Breaking one of these is a bug, not a tradeoff.

- Never block keyboard or mouse input, steal focus, or use modal/full-screen interruption.
- Never record window titles, input, clipboard or screen content, URLs, or document names.
- Foreground process-name collection is opt-in and disabled by default.
- Keep adjustable timing values out of UI logic.
- Core timing logic must support a fake clock, and measures elapsed time with a monotonic clock — never
  the adjustable wall clock.
- Do not add out-of-scope features without a scope review.

## Where to read what

| When your task is | Read |
|---|---|
| Picking up or filing a ticket | [`docs/agents/issue-tracker.md`](docs/agents/issue-tracker.md) + [`triage-labels.md`](docs/agents/triage-labels.md) |
| Writing a spec for someone else to implement | [`docs/agents/writing-specs.md`](docs/agents/writing-specs.md) |
| Changing domain behaviour | [`docs/product/design-spec.md`](docs/product/design-spec.md) — the product contract in one page — then the relevant ADRs |
| Asking why a design is the way it is | [`docs/adr/`](docs/adr/) |
| Placing new code in a layer | [`docs/architecture/overview.md`](docs/architecture/overview.md) |
| Needing the full product detail | [`docs/product/windows-eye-care-assistant-design-spec-v1.3.md`](docs/product/windows-eye-care-assistant-design-spec-v1.3.md) |
| Verifying, releasing, or accepting a build | [`docs/testing/`](docs/testing/) |
| Explaining data handling to a user | [`docs/privacy.md`](docs/privacy.md), [`docs/known-limitations.md`](docs/known-limitations.md) |
| Wondering what is deferred | [`docs/backlog.md`](docs/backlog.md) |

Use established domain terms in issue titles, code, tests, and documentation. If a change conflicts with
an ADR or a guardrail above, surface the conflict and request review rather than silently overriding it.

## Development

Run `dotnet build RestCue.sln` and `dotnet test RestCue.sln`.

Every completed ticket reports changes, tests, known limitations, and data/schema impact. Do not commit,
push, or close the issue yourself.

## Lint

One gate, run in CI before the build:

```
dotnet format whitespace RestCue.sln --verify-no-changes
```

Drop `--verify-no-changes` to fix in place. Rules live in `.editorconfig`. Deliberately kept to
whitespace only — the `style` and `analyzers` passes need a full Roslyn analysis and cost minutes rather
than seconds.

Conventions, all matching what the codebase already did:

- Line endings are LF, pinned by `.gitattributes`.
- Private fields are `camelCase` with **no** `_` prefix. Where a constructor parameter shares the name,
  assign through `this.`.
- `const` and `static readonly` fields are `PascalCase`.
- Single-line guard clauses (`if (x == null) return;`) stay on one line.

## Working style

- **Think before coding**: state assumptions explicitly; if uncertain, ask rather than guess; present
  multiple interpretations when ambiguity exists; stop when confused and name what is unclear.
- **Simplicity first**: use the minimum code that solves the problem; nothing speculative, no features
  beyond what was asked, no abstractions for single-use code.
- **Goal-driven execution**: define success criteria and iterate until verified; do not just follow steps.
- **Surface conflicts, do not average them**: if two patterns contradict, pick one based on which is more
  recent or more tested, explain why, and flag the other for cleanup. Do not blend conflicting patterns.
