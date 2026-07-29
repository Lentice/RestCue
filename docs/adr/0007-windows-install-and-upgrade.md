# ADR 0007: Windows Install and Upgrade Strategy

## Context

RestCue targets Windows 10/11 as a per-user tray application. Users need a
reproducible way to install, upgrade, and uninstall without requiring
administrator privileges. The app's data lives in `%LocalAppData%\RestCue\`,
separate from the binaries.

Existing ADRs constrain this decision:
- ADR-0001 (SQLite settings persistence) defines corruption recovery and
  prohibits schema downgrade.
- ADR-0005 (usage event persistence) defines schema v2 and transactional
  migration.
- ADR-0006 (break guide completion semantics) is unrelated.

## Decision

We will use **Inno Setup** for a single per-user installer `.exe`, with
**framework-dependent publishing** targeting `win-x64`, single SemVer version
source in `Directory.Build.props`, and user data preserved on uninstall.

## Installer technology: Inno Setup

- **Why**: Single `.exe`, script in version control, no MSI SDK/admin
  requirement, well-documented per-user install support.
- **Why not MSI**: Requires WiX or VS Setup project, both add SDK dependency
  and typically require admin elevation for per-machine install. A per-user MSI
  is possible but less documented and harder to script for CI.
- **Why not Squirrel/Winget/Chocolatey**: Each introduces a separate update
  framework or store dependency. RestCue's small scope (one .exe + dependencies)
  does not justify the additional surface.

## Per-user vs per-machine: per-user

- Install to `%LocalAppData%\Programs\RestCue`.
- App data stays at `%LocalAppData%\RestCue\restcue.db` (unchanged).
- No UAC prompt during install.
- Matches app's existing data location convention.

## Publish mode: framework-dependent

- Requires .NET 10 Desktop Runtime on the target machine.
- Smaller package size (~1 MB vs ~80 MB self-contained).
- Runtime security patches handled by .NET updates.
- If users without runtime become a problem, switch to self-contained in a
  future ADR amendment.

## Runtime identifier: `win-x64`

- Covers >95% of Windows 10/11 desktop machines.
- ARM64 support would require a separate build and is deferred.

## Versioning: SemVer in `Directory.Build.props`

- Single source: `Directory.Build.props` defines `<Version>`.
- `RestCue.App.csproj` inherits it and passes it to
  `AssemblyVersion`/`FileVersion`.
- The installer script reads the version from the built `.exe`.
- CI injects the version via `-p:Version=...`.

## Upgrade detection

- Fixed `AppId=RestCue` + `AppVerName` with version comparison.
- Same version = repair/reinstall.
- Older version rejected: `SetupVersion=lt` → error message, no downgrade.
- Consistent with ADR-0001/0005 "no schema downgrade" policy.

## Code signing

- Not implemented in this ADR. Binaries are unsigned.
- SmartScreen warning is expected and documented in known limitations.
- Signing requires a code signing certificate and secure CI secret
  provisioning — tracked as a future release blocker.

## User data on uninstall

- **Preserved by default.** Uninstall removes binaries, shortcuts, and startup
  registration only.
- User can manually clear data via app's #21 Export/Clear feature.
- Installer explicitly does not delete or touch `%LocalAppData%\RestCue\`.
- This matches `docs/privacy.md` and the out-of-scope rule against silent
  deletion.

## Startup registration

- Owned by the app itself (#19 Settings UI).
- Installer does not write startup registry keys.
- Uninstall removes any leftover startup entries to avoid orphan references.

## Alternatives considered

| Alternative | Rejected because |
|---|---|
| MSI (WiX Toolset) | Requires admin, larger SDK dependency, harder CI setup |
| Squirrel.Windows | Opinionated about update flow, not pure install/uninstall |
| Chocolatey package | Depends on Chocolatey being installed |
| Manual xcopy deploy | No clean uninstall, no Start Menu integration |

## Consequences

- Positive: Single reproducible command to build package; no admin required;
  version cannot drift between installer and binary; data safety on uninstall.
- Negative: Unsigned binary triggers SmartScreen; framework-dependent requires
  .NET runtime pre-installed; no downgrade path.

## Review trigger

Revisit when:
- Code signing certificate is obtained.
- ARM64 support is required.
- Self-contained publishing becomes necessary for a store-like distribution.
- .NET version requirement changes (e.g., .NET 11 LTS).
