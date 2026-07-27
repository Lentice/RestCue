# Issue tracker: GitHub

Issues and PRDs live in GitHub Issues for `Lentice/RestCue`. Infer the repository from the configured `origin` remote and use the GitHub connector or `gh` CLI.

## Conventions

- Create one issue per independently deliverable tracer-bullet slice.
- Read the complete issue body and comments before implementation.
- Apply `ready-for-agent` only when acceptance criteria and blockers are explicit.
- Use GitHub native issue dependencies for blocking edges. If unavailable, add a `Blocked by: #<number>` reference to the issue body.
- Do not close or modify a parent issue while publishing child tickets.

## Pull requests as a triage surface

**PRs as a request surface: no.**

## Skill routing

- When a skill says “publish to the issue tracker,” create a GitHub issue.
- When a skill says “fetch the relevant ticket,” read the complete GitHub issue and comments.
- The frontier is any open, unassigned ticket whose blocking issues are all closed.

