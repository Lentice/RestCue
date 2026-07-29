# Windows Install/Upgrade Verification

## Build & package

```powershell
# Full build and test
dotnet build RestCue.sln --configuration Release
dotnet test RestCue.sln --configuration Release --no-build --filter "Category!=LongRun"

# Package
.\packaging\windows\build-package.ps1
```

## Environment

| Date | OS build | App version | Installer SHA-256 |
|---|---|---|---|
| 2026-07-29 | 10.0.26200.0 | 1.3.0 (45fca87) | `0C342E88028A79190F37C9F26326991C7E37DF383C1D94C1CE8D8996675E3293` |

## Test matrix

| Date | OS build | App version / commit | Installer SHA-256 | Scenario | Steps | Expected | Actual | Result |
|---|---|---|---|---|---|---|---|---|
| 2026-07-29 | 10.0.26200.0 | 1.3.0 (45fca87) | 0C342E88... | Clean install (Win11) | Run installer, launch app | Tray appears, no UAC, DB created on first launch | | BLOCKED |
| | | | | Clean install (Win10) | | | | BLOCKED |
| | | | | Upgrade from previous | Install older version, add data, install new | Data preserved, version updated | | BLOCKED |
| | | | | Same-version repair | Run installer over existing | Repair completes, data preserved | | BLOCKED |
| | | | | Failed-upgrade recovery | Lock files, run installer | Clean error, old version works | | BLOCKED |
| | | | | Uninstall | Remove via Add/Remove Programs | App stops, binaries removed, user data preserved | | BLOCKED |

## Notes

- All BLOCKED: requires interactive Windows GUI testing with human operator.
- Installer compile verified: `RestCue-1.3.0-win-x64.exe` (3.27 MB, SHA-256).
- Framework-dependent publish: requires .NET 10 Desktop Runtime.
- No code signing: SmartScreen warning expected.
