[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$workflowPath = Join-Path $repoRoot ".github\workflows\package.yml"
$workflow = Get-Content -Raw $workflowPath
$builderPath = Join-Path $repoRoot "packaging\windows\build-package.ps1"
$builder = Get-Content -Raw $builderPath

$requiredFragments = @(
    ".\packaging\windows\build-package.ps1",
    ".\packaging\windows\verify-installer.ps1",
    "artifacts/verification/**",
    "Get-Date -Format `"0.0.yy.Mdd`""
)

$failures = [System.Collections.Generic.List[string]]::new()

foreach ($fragment in $requiredFragments[0..2]) {
    if (-not $workflow.Contains($fragment)) {
        $failures.Add("package.yml is missing required fragment: $fragment")
    }
}

if ($workflow.Contains($requiredFragments[3])) {
    $failures.Add("package.yml must use the product version, not a date-derived version.")
}

if ($builder -match 'dotnet\s+restore[^\r\n]*--configuration') {
    $failures.Add("build-package.ps1 passes unsupported --configuration to dotnet restore.")
}

if ($builder -match '--framework\s+') {
    $failures.Add("build-package.ps1 pins a publish --framework that can drift from the project-declared TFM.")
}

if ($builder -match '(?s)param\s*\([^)]*\$Version') {
    $failures.Add("build-package.ps1 must not expose an installer-only version override.")
}

if ($failures.Count -gt 0) {
    throw ($failures -join [Environment]::NewLine)
}

Write-Host "Package workflow contract passed."
