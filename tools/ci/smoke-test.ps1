<#
.SYNOPSIS
    Builds the app unpackaged and runs its built-in smoke test.

.DESCRIPTION
    Starts Weatherly.App.exe with --smoke-test=<report>. The app shows its window, runs the startup
    diagnostics (DPI awareness, Direct3D device), writes the report and exits. Exit code 0 means
    every check passed. Used by CI and usable locally:

        pwsh tools/ci/smoke-test.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'ARM64')]
    [string] $Platform = 'x64',

    [string] $Configuration = 'Release',

    [string] $ReportPath = (Join-Path ([System.IO.Path]::GetTempPath()) 'weatherly-smoke-report.txt'),

    [int] $TimeoutSeconds = 90
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..' '..')
$project = Join-Path $repoRoot 'src' 'Weatherly.App' 'Weatherly.App.csproj'

# Unpackaged, so the exe starts without MSIX deployment and brings the Windows App SDK runtime along.
dotnet build $project -c $Configuration -p:Platform=$Platform -p:WindowsPackageType=None --no-incremental
if ($LASTEXITCODE -ne 0) {
    throw "Building the unpackaged app failed with exit code $LASTEXITCODE."
}

$rid = "win-$($Platform.ToLowerInvariant())"
$exe = Get-ChildItem (Join-Path $repoRoot 'src' 'Weatherly.App' 'bin') -Recurse -Filter 'Weatherly.App.exe' |
    Where-Object { $_.FullName -match "[\\/]$Configuration[\\/]" -and $_.FullName -match "[\\/]$rid[\\/]" } |
    Select-Object -First 1
if (-not $exe) {
    throw "Weatherly.App.exe for $Configuration/$rid was not found."
}

Remove-Item $ReportPath -ErrorAction SilentlyContinue
Write-Host "Starting $($exe.FullName)"
$process = Start-Process -FilePath $exe.FullName -ArgumentList "--smoke-test=$ReportPath" -PassThru
$null = $process.Handle  # Keeps the handle open, otherwise ExitCode is empty after the process ends.

if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    $process.Kill()
    throw "Smoke test did not finish within $TimeoutSeconds s. The app hung or never showed its window."
}

if (Test-Path $ReportPath) {
    Write-Host '--- smoke test report ---'
    Get-Content $ReportPath | Write-Host
    Write-Host '-------------------------'
}
else {
    Write-Host 'The app wrote no smoke test report.'
}

if ($process.ExitCode -ne 0) {
    throw "Smoke test failed with exit code $($process.ExitCode)."
}

if (-not (Test-Path $ReportPath)) {
    throw 'Smoke test exited with code 0 but wrote no report.'
}

Write-Host 'Smoke test passed.'
