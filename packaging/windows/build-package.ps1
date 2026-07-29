<#
.SYNOPSIS
    Build RestCue installer package from a clean clone state.
.DESCRIPTION
    Publishes RestCue.App with Release configuration, generates the Inno Setup
    installer, and outputs SHA-256 checksum to artifacts/.
.PARAMETER Version
    Version string for the package (default: auto-detect from built assembly).
.PARAMETER Configuration
    Build configuration (default: Release).
.EXAMPLE
    .\packaging\windows\build-package.ps1
#>

param(
    [string]$Version,
    [string]$Configuration = "Release"
)

$RepoRoot = Resolve-Path "$PSScriptRoot\..\.."
$PublishDir = "$RepoRoot\artifacts\publish\win-x64"
$InstallerDir = "$RepoRoot\artifacts"

Write-Host "=== RestCue Package Builder ===" -ForegroundColor Cyan
Write-Host "Repository: $RepoRoot"
Write-Host "Configuration: $Configuration"

# Step 1: Restore, build, test (Release)
Write-Host "`n[1/5] Restoring..." -ForegroundColor Yellow
dotnet restore "$RepoRoot\RestCue.sln" --configuration $Configuration
if ($LASTEXITCODE -ne 0) { throw "Restore failed." }

Write-Host "`n[2/5] Building..." -ForegroundColor Yellow
dotnet build "$RepoRoot\RestCue.sln" --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

Write-Host "`n[3/5] Testing..." -ForegroundColor Yellow
dotnet test "$RepoRoot\RestCue.sln" --configuration $Configuration --no-build --filter "Category!=LongRun"
if ($LASTEXITCODE -ne 0) { throw "Tests failed." }

# Step 2: Publish
Write-Host "`n[4/5] Publishing..." -ForegroundColor Yellow
dotnet publish "$RepoRoot\src\RestCue.App\RestCue.App.csproj" `
    --configuration $Configuration `
    --no-build `
    --framework net10.0-windows `
    --runtime win-x64 `
    --self-contained false `
    --output $PublishDir
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

# Auto-detect version from published assembly
if (-not $Version) {
    $exePath = "$PublishDir\RestCue.exe"
    if (Test-Path $exePath) {
        $vi = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exePath)
        $Version = "$($vi.FileMajorPart).$($vi.FileMinorPart).$($vi.FileBuildPart)"
        Write-Host "Auto-detected version: $Version"
    } else {
        $Version = "1.3.0"
        Write-Host "Using default version: $Version"
    }
}

# Step 3: Build installer
Write-Host "`n[5/5] Building installer..." -ForegroundColor Yellow
$issPath = "$RepoRoot\packaging\windows\RestCue.iss"
$isccPath = if (Test-Path "${env:ProgramFiles}\Inno Setup 7\ISCC.exe") {
    "${env:ProgramFiles}\Inno Setup 7\ISCC.exe"
} elseif (Test-Path "${env:LOCALAPPDATA}\Programs\Inno Setup 7\ISCC.exe") {
    "${env:LOCALAPPDATA}\Programs\Inno Setup 7\ISCC.exe"
} else {
    throw "Inno Setup (ISCC.exe) not found. Install from https://jrsoftware.org/isdl.php"
}

& $isccPath "/dMyAppVersion=$Version" $issPath
if ($LASTEXITCODE -ne 0) { throw "Installer build failed." }

# Step 4: Generate SHA-256
$installerPath = Get-ChildItem "$InstallerDir\RestCue-$Version-win-x64*.exe" | Select-Object -First 1
if ($installerPath) {
    $hash = Get-FileHash -Algorithm SHA256 -Path $installerPath.FullName
    $hash | Format-List
    $hash.Hash | Out-File -FilePath "$($installerPath.FullName).sha256" -Encoding ascii
    Write-Host "`nSHA-256 saved to: $($installerPath.FullName).sha256" -ForegroundColor Green
} else {
    Write-Warning "Installer not found at expected path."
}

Write-Host "`n=== Package build complete ===" -ForegroundColor Cyan
Write-Host "Installer: $InstallerDir"
