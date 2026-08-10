# Writing an issue spec

Work is tracked in GitHub Issues (see [`issue-tracker.md`](issue-tracker.md)). Most issues are worked
straight from the issue body. A **spec** is the extra artifact you write when an issue is too large or
too constrained to hand over as prose — it goes in `docs/specs/issue-<n>-<slug>.md`.

A spec is written by whoever holds the full context and implemented by someone holding none. Every rule
below exists to survive that gap.

- **Self-contained.** The implementer reads `AGENTS.md`, the spec, and the GitHub issue — nothing else.
  Fold in the product rules, defaults, privacy boundaries and acceptance conditions the ticket needs, so
  nobody has to read the 1300-line product spec to start.
- **One outcome per spec**, sized half a day to two days. If it grows past that, split it before writing.
- **Quote the binding constraints inline** — the guardrails from `AGENTS.md`, the relevant clauses of
  [`../product/design-spec.md`](../product/design-spec.md), and the ADRs that bind the change. Only ask
  the implementer to touch an ADR when the spec explicitly says to.
- **Name the files to read and trace, including the call sites** — not just the file to be edited.
- **Non-goals carry their reasons.** A non-goal with no reason gets re-litigated by the next reader.
- **Acceptance criteria are observable, and the checks are executable**: the exact `dotnet test` filter
  or manual step, with its expected result.
- **State overrides in the new spec.** Never edit a spec for closed work — it is the historical record.
- **Status lives in GitHub, never in the spec file.** Two copies of a status drift, and a spec that
  still reads "open" after the work shipped tells the next cold reader to build it again.

If the issue's comments conflict with the spec, **stop and request review** — do not blend the two
semantics.

## Retired specs

Specs for issues #8–#25 were removed on 2026-08-07 once all of them shipped. The behaviour they
described now lives in the code, the tests, and `docs/adr/`; the specs themselves are in git history.
