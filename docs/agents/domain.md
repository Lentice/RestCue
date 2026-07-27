# Domain docs

RestCue uses a single-context domain-document layout.

## Before changing domain behaviour

- Read root `CONTEXT.md` when it exists.
- Read relevant records under `docs/adr/`.
- Treat `docs/product/design-spec.md` and its referenced MVP v1.1 source as the product contract.
- Proceed silently when a domain document does not yet exist.

Use established domain terms in issue titles, code, tests, and documentation. If a proposed change conflicts with an ADR or product guardrail, surface the conflict and request review rather than silently overriding it.

## Layout

```text
/
├── CONTEXT.md
├── docs/
│   ├── agents/
│   ├── adr/
│   └── product/
├── src/
└── tests/
```

