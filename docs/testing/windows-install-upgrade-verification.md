# Windows Install/Upgrade Verification

## Automated build and lifecycle verification

```powershell
# Restore, build, test, publish, compile installer, and create SHA-256
.\packaging\windows\build-package.ps1

# Requires a clean Windows user with no existing RestCue installation
.\packaging\windows\verify-installer.ps1 `
  -InstallerPath .\artifacts\RestCue-1.3.0-win-x64.exe `
  -IsccPath "${env:LOCALAPPDATA}\Programs\Inno Setup 7\ISCC.exe"
```

The `Package` GitHub Actions workflow runs both commands on a fresh
`windows-latest` runner and uploads the installer, checksum, lifecycle report,
and Inno Setup logs as one artifact. The verifier refuses to run when RestCue
is already installed for the current user.

## Automated execution record

This local run verifies installer mechanics. Its checksum is evidence for the
tested working tree, not a release checksum; use the checksum produced by the
corresponding clean CI run for a release.

| Date | OS build | App version | Installer SHA-256 | Scenario | Actual | Result |
|---|---|---|---|---|---|---|
| 2026-07-29 | 10.0.26200.0 | 1.3.0 | `6F665197062A054D28F2623ED866CEA5839A39C280AE04DD3F95561C4DFD08BD` | Clean silent install | Per-user install completed without elevation; binaries and uninstall registration were present. | PASS |
| 2026-07-29 | 10.0.26200.0 | installer metadata 1.2.999 → 1.3.0 | same run | Installer version transition | The current binaries were packaged under two installer versions; installed metadata changed to 1.3.0 and the user-data sentinel was preserved. This does not replace the real previous-release/schema test below. | PASS |
| 2026-07-29 | 10.0.26200.0 | 1.3.0 | same run | Same-version repair | Repair completed; binaries and user-data sentinel remained present. | PASS |
| 2026-07-29 | 10.0.26200.0 | 1.3.0 | same run | Malformed-package rejection | A malformed package was rejected before installation; installed version, binaries, and sentinel remained available. This does not exercise mid-install rollback. | PASS |
| 2026-07-29 | 10.0.26200.0 | 1.3.0 → 1.2.999 | same run | Downgrade rejection | Installer logged an explicit rejection and kept 1.3.0 installed. | PASS |
| 2026-07-29 | 10.0.26200.0 | 1.3.0 | same run | Silent uninstall | Binaries, uninstall registration, and app-owned startup registration were removed; user-data sentinel was preserved. | PASS |

## Manual Windows/UI matrix

Automation does not prove tray rendering, Add/Remove Programs UI, SmartScreen
presentation, or behavior on every supported Windows version.

| Scenario | Required environment | Expected | Result |
|---|---|---|---|
| Clean install and launch (Win10) | Clean Windows 10 22H2 VM with .NET 10 Desktop Runtime | No UAC; tray appears; DB is created only after first launch. | BLOCKED — VM unavailable |
| Clean install and launch (Win11) | Clean supported Windows 11 VM with .NET 10 Desktop Runtime | No UAC; tray appears; DB is created only after first launch. | BLOCKED — clean VM unavailable |
| Upgrade with real schema data | Previous supported release with non-default settings and usage events | Settings, event count, ordering, and schema version remain valid. | BLOCKED — release fixture and clean VM unavailable |
| Mid-install file-lock failure | Previous release running or target binary locked | Error is diagnosable; previous executable and database remain usable. | BLOCKED — interactive VM run required |
| Add/Remove Programs uninstall | Clean Win10 and Win11 VMs | Process/tray/shortcuts/startup registration/binaries removed; user data preserved. | BLOCKED — clean VMs unavailable |

## Known limitations

- The automated failed-upgrade case covers rejection of a malformed package
  before file replacement. A mid-install file-lock failure remains manual.
- The framework-dependent package requires .NET 10 Desktop Runtime.
- The package is unsigned; SmartScreen warning behavior remains a release
  blocker and requires manual verification.
