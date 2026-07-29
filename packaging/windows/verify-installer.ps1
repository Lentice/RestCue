<#
.SYNOPSIS
    Exercises the RestCue installer lifecycle on a clean Windows CI user.
.DESCRIPTION
    Verifies silent clean install, upgrade, same-version repair, rejected
    upgrade input recovery, downgrade rejection, and uninstall. The script
    refuses to run when RestCue is already installed so it cannot disturb a
    developer workstation.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$InstallerPath,

    [Parameter(Mandatory)]
    [string]$IsccPath
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$installer = Get-Item (Resolve-Path $InstallerPath)
$iscc = Get-Item (Resolve-Path $IsccPath)
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\RestCue_is1"
$verificationRoot = Join-Path $repoRoot "artifacts\verification"
$installDir = Join-Path $verificationRoot "installed"
$logDir = Join-Path $verificationRoot "logs"
$reportPath = Join-Path $verificationRoot "installer-lifecycle.md"
$sentinelDir = Join-Path $env:LOCALAPPDATA "RestCue"
$sentinelPath = Join-Path $sentinelDir "b08-ci-verification-sentinel.txt"
$startupShortcut = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Startup\RestCue.lnk"
$startupKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$startupValueName = "RestCue"
$results = [System.Collections.Generic.List[object]]::new()

function Add-Result {
    param(
        [string]$Scenario,
        [bool]$Passed,
        [string]$Actual
    )

    $script:results.Add([pscustomobject]@{
        Scenario = $Scenario
        Result = if ($Passed) { "PASS" } else { "FAIL" }
        Actual = $Actual.Replace("|", "\|")
    })

    if (-not $Passed) {
        throw "$Scenario failed: $Actual"
    }
}

function Invoke-Setup {
    param(
        [string]$Path,
        [string]$LogPath,
        [switch]$Uninstall
    )

    $arguments = @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART")
    if (-not $Uninstall) {
        $arguments += "/SP-"
        $arguments += "/CURRENTUSER"
        $arguments += "/DIR=$installDir"
    }
    $arguments += "/LOG=$LogPath"

    $process = Start-Process -FilePath $Path -ArgumentList $arguments -Wait -PassThru
    return $process.ExitCode
}

function Get-PreviousVersion {
    param([version]$Version)

    if ($Version.Build -gt 0) {
        return [version]::new($Version.Major, $Version.Minor, $Version.Build - 1)
    }
    if ($Version.Minor -gt 0) {
        return [version]::new($Version.Major, $Version.Minor - 1, 999)
    }
    if ($Version.Major -gt 0) {
        return [version]::new($Version.Major - 1, 999, 999)
    }

    throw "Cannot derive a previous version from $Version."
}

if (Test-Path $uninstallKey) {
    throw "RestCue is already installed for this user. Use a clean CI user or VM."
}
if (Test-Path $sentinelPath) {
    throw "Verification sentinel already exists: $sentinelPath"
}
if ((Get-ItemProperty -Path $startupKey -Name $startupValueName -ErrorAction SilentlyContinue).$startupValueName) {
    throw "RestCue startup registration already exists for this user."
}

$versionMatch = [regex]::Match(
    $installer.Name,
    '^RestCue-(?<version>\d+\.\d+\.\d+)-win-x64\.exe$')
if (-not $versionMatch.Success) {
    throw "Installer name does not contain the expected product version: $($installer.Name)"
}

$currentVersion = [version]$versionMatch.Groups["version"].Value
$previousVersion = Get-PreviousVersion $currentVersion
$issPath = Join-Path $repoRoot "packaging\windows\RestCue.iss"
$previousInstaller = Join-Path $repoRoot "artifacts\RestCue-$previousVersion-win-x64.exe"

New-Item -ItemType Directory -Force -Path $verificationRoot, $logDir, $sentinelDir | Out-Null
Set-Content -Path $sentinelPath -Value "Created only for B08 CI verification." -Encoding utf8

try {
    & $iscc.FullName "/dMyAppVersion=$previousVersion" $issPath
    Add-Result "Build previous-version fixture" ($LASTEXITCODE -eq 0 -and (Test-Path $previousInstaller)) "ISCC exit code: $LASTEXITCODE"

    $exitCode = Invoke-Setup $previousInstaller (Join-Path $logDir "clean-install.log")
    $displayVersion = (Get-ItemProperty $uninstallKey).DisplayVersion
    Add-Result "Clean install" ($exitCode -eq 0 -and (Test-Path (Join-Path $installDir "RestCue.exe")) -and $displayVersion -eq $previousVersion.ToString() -and -not (Test-Path $startupShortcut)) "Exit $exitCode; installed version $displayVersion; installer did not take ownership of startup registration."

    New-ItemProperty `
        -Path $startupKey `
        -Name $startupValueName `
        -Value "`"$(Join-Path $installDir 'RestCue.exe')`"" `
        -PropertyType String `
        -Force | Out-Null

    $exitCode = Invoke-Setup $installer.FullName (Join-Path $logDir "upgrade.log")
    $displayVersion = (Get-ItemProperty $uninstallKey).DisplayVersion
    Add-Result "Upgrade" ($exitCode -eq 0 -and $displayVersion -eq $currentVersion.ToString() -and (Test-Path $sentinelPath)) "Exit $exitCode; installed version $displayVersion; user-data sentinel preserved."

    $exitCode = Invoke-Setup $installer.FullName (Join-Path $logDir "repair.log")
    Add-Result "Same-version repair" ($exitCode -eq 0 -and (Test-Path (Join-Path $installDir "RestCue.exe")) -and (Test-Path $sentinelPath)) "Exit $exitCode; binaries and user-data sentinel present."

    $invalidInstaller = Join-Path $verificationRoot "invalid-upgrade.exe"
    [System.IO.File]::WriteAllBytes($invalidInstaller, [byte[]](0x4D, 0x5A, 0x00, 0x00))
    $invalidRejected = $false
    try {
        $invalidExitCode = Invoke-Setup $invalidInstaller (Join-Path $logDir "failed-upgrade.log")
        $invalidRejected = $invalidExitCode -ne 0
    }
    catch {
        $invalidRejected = $true
    }
    $displayVersion = (Get-ItemProperty $uninstallKey).DisplayVersion
    Add-Result "Failed-upgrade recovery" ($invalidRejected -and $displayVersion -eq $currentVersion.ToString() -and (Test-Path (Join-Path $installDir "RestCue.exe")) -and (Test-Path $sentinelPath)) "Invalid upgrade rejected; installed version $displayVersion and existing data remained available."

    $downgradeLog = Join-Path $logDir "downgrade-rejection.log"
    $exitCode = Invoke-Setup $previousInstaller $downgradeLog
    $displayVersion = (Get-ItemProperty $uninstallKey).DisplayVersion
    $downgradeWasRejected = (Get-Content -Raw $downgradeLog).Contains(
        "InitializeSetup returned False; aborting.")
    Add-Result "Downgrade rejection" ($downgradeWasRejected -and $displayVersion -eq $currentVersion.ToString()) "Installer logged an explicit rejection; installed version remained $displayVersion."

    $uninstaller = Join-Path $installDir "unins000.exe"
    $exitCode = Invoke-Setup $uninstaller (Join-Path $logDir "uninstall.log") -Uninstall
    $startupValue = (Get-ItemProperty -Path $startupKey -Name $startupValueName -ErrorAction SilentlyContinue).$startupValueName
    Add-Result "Uninstall" ($exitCode -eq 0 -and -not (Test-Path $installDir) -and -not (Test-Path $uninstallKey) -and -not $startupValue -and (Test-Path $sentinelPath)) "Exit $exitCode; binaries and startup registration removed; user-data sentinel preserved."
}
finally {
    if (Test-Path $uninstallKey) {
        $uninstaller = Join-Path $installDir "unins000.exe"
        if (Test-Path $uninstaller) {
            try {
                Invoke-Setup $uninstaller (Join-Path $logDir "cleanup-uninstall.log") -Uninstall | Out-Null
            }
            catch {
                Write-Warning "Cleanup uninstall failed: $_"
            }
        }
    }

    if (Test-Path $sentinelPath) {
        Remove-Item -LiteralPath $sentinelPath -Force
    }
    if ((Get-ItemProperty -Path $startupKey -Name $startupValueName -ErrorAction SilentlyContinue).$startupValueName) {
        Remove-ItemProperty -Path $startupKey -Name $startupValueName -Force
    }
    if (Test-Path $previousInstaller) {
        Remove-Item -LiteralPath $previousInstaller -Force
    }
    if (Test-Path (Join-Path $verificationRoot "invalid-upgrade.exe")) {
        Remove-Item -LiteralPath (Join-Path $verificationRoot "invalid-upgrade.exe") -Force
    }
}

$reportLines = @(
    "# Installer lifecycle verification"
    ""
    "- Date (UTC): $(Get-Date -AsUTC -Format 'yyyy-MM-ddTHH:mm:ssZ')"
    "- OS: $([System.Environment]::OSVersion.VersionString)"
    "- Current installer: $($installer.Name)"
    "- SHA-256: $((Get-FileHash -Algorithm SHA256 $installer.FullName).Hash)"
    ""
    "| Scenario | Result | Actual |"
    "|---|---|---|"
)
foreach ($result in $results) {
    $reportLines += "| $($result.Scenario) | $($result.Result) | $($result.Actual) |"
}

Set-Content -Path $reportPath -Value $reportLines -Encoding utf8
Write-Host "Installer lifecycle verification passed. Report: $reportPath"
