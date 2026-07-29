# Performance & Privacy Validation

## Commands

```powershell
# Full short-run validation (excluding long soak)
dotnet test RestCue.sln --filter "Category!=LongRun"

# Validation project only (short tests)
dotnet test tests/RestCue.Validation.Tests/RestCue.Validation.Tests.csproj --filter "Category!=LongRun"

# Long soak (Windows only, 8 hours for release candidate)
$env:RESTCUE_SOAK_MINUTES = "480"
dotnet test tests/RestCue.Validation.Tests/RestCue.Validation.Tests.csproj `
  --filter "Category=LongRun" `
  --logger "trx;LogFileName=soak.trx" `
  --results-directory artifacts/validation
```

## Environment

| Field | Value |
|---|---|
| Date (UTC) | |
| OS build | |
| .NET SDK | |
| App commit | |
| Configuration | Debug / Release |
| Hardware (CPU/RAM) | |

## Short-run results

| Check | Command | Threshold | Result |
|---|---|---|---|
| state scenarios | `dotnet test --filter "FullyQualifiedName~StateTransitionScenarioTests"` | 九個 phase 全覆蓋 | PASS/FAIL |
| polling cadence | `dotnet test --filter "FullyQualifiedName~PollingCadenceTests"` | 每 tick 1 sample | PASS/FAIL |
| tray update count | `dotnet test --filter "FullyQualifiedName~TrayUpdateCountTests"` | 穩定狀態 0 次重繪 | PASS/FAIL |
| sqlite write count | `dotnet test --filter "FullyQualifiedName~SqliteWriteCadenceTests"` | 純 poll 0 writes | PASS/FAIL |
| privacy denylist | `dotnet test --filter "FullyQualifiedName~PrivacyDenylistTests"` | 0 命中 | PASS/FAIL |
| process name opt-in | `dotnet test --filter "FullyQualifiedName~ProcessNameOptInTests"` | 預設為 null | PASS/FAIL |

## Soak results

| Sample | Elapsed | CPU % avg | WorkingSet MB | PrivateBytes MB |
|---|---|---|---|---|

| Sample | Handles | Threads | DB KB | Writes |
|---|---|---|---|---|
