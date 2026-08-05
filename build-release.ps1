<#
.SYNOPSIS
    Build RestCue in Release configuration.
.DESCRIPTION
    Builds and optionally publishes RestCue.App. When -SelfContained is used,
    the .NET runtime is bundled (no runtime install required on target machine).
.PARAMETER SkipTests
    Skip running unit tests.
.PARAMETER SelfContained
    Bundle .NET runtime and native dependencies into the publish output.
    Default is framework-dependent (requires .NET 10 runtime on target machine).
.PARAMETER PublishDir
    Output directory for publish (default: artifacts\publish\win-x64).
.PARAMETER Configuration
    Build configuration (default: Release).
.EXAMPLE
    .\build-release.ps1
    Build + test, framework-dependent publish.
.EXAMPLE
    .\build-release.ps1 -SelfContained
    Build + test, self-contained publish with bundled runtime.
.EXAMPLE
    .\build-release.ps1 -SkipTests
    Build + publish only, skip tests.
#>

param(
    [switch]$SkipTests,
    [switch]$SelfContained,
    [string]$PublishDir = "",
    [string]$Configuration = "Release"
)

$RepoRoot = $PSScriptRoot
$SlnPath = "$RepoRoot\RestCue.sln"
$Rid = "win-x64"

if (-not $PublishDir) {
    $PublishDir = "$RepoRoot\artifacts\publish\$Rid"
}

Write-Host "=== RestCue Release Build ===" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration"
Write-Host "Self-contained: $SelfContained"
Write-Host "Publish dir: $PublishDir"

Write-Host "`n[1/3] Restoring..." -ForegroundColor Yellow
dotnet restore $SlnPath
if ($LASTEXITCODE -ne 0) { throw "Restore failed." }

Write-Host "`n[2/3] Building..." -ForegroundColor Yellow
dotnet build $SlnPath --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

if (-not $SkipTests) {
    Write-Host "`n[2b/3] Testing..." -ForegroundColor Yellow
    dotnet test $SlnPath --configuration $Configuration --no-build --filter "Category!=LongRun"
    if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
}

Write-Host "`n[3/3] Publishing..." -ForegroundColor Yellow
$publishArgs = @(
    "$RepoRoot\src\RestCue.App\RestCue.App.csproj"
    # No --framework: the project has a single TFM, and pinning it here drifts
    # whenever the required Windows SDK version changes.
    "--configuration", $Configuration
    "--runtime", $Rid
    "--output", $PublishDir
)

if ($SelfContained) {
    $publishArgs += "--self-contained", "true"
    $publishArgs += "-p:PublishSingleFile=true"
    $publishArgs += "-p:EnableCompressionInSingleFile=true"
    $publishArgs += "-p:DebugType=None"
    $publishArgs += "-p:DebugSymbols=false"
} else {
    $publishArgs += "--self-contained", "false"
}

dotnet publish @publishArgs
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

Write-Host "`n=== Build complete ===" -ForegroundColor Cyan
Write-Host "Output: $PublishDir"
