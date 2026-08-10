# Backlog — not yet committed to a release

Confirmed work lives in GitHub Issues. This file holds only the things a maintainer must not forget and
that no issue covers yet.

## Release blockers

- **Code signing.** Unsigned builds trigger SmartScreen on a fresh machine. Needs a certificate and a
  secure CI secret before a public release.

## Under evaluation

- **Focus Assist / deep-work detection.** Evaluate only if the `Ignored` / `AutoDismissed` rates in real
  use show people are being interrupted during deep work. No decision without that evidence.
- **ARM64 support.** Needs a separate build artifact and a CI matrix expansion.
