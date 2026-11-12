<#
.SYNOPSIS
    Soak test of the app (PLAN.md, M13): runs it for a long time with everything on and checks for leaks.
.DESCRIPTION
    Builds the app unpackaged and starts it with --soak=<minutes>. The app turns all six effects on, invites visitors,
    moves and resizes its window every few seconds and samples private memory, the managed heap, handles and GDI/USER
    objects every 30 seconds. After warm-up the growth must stay within SoakAnalysis' limits; the report also notes the
    CPU load and the frame times (for the budgets of PLAN.md, section 9, on real hardware).
    The samples are written to soak.csv next to the report.

    The full test is 8 hours, on a real PC (GitHub jobs stop after 6 hours):
        pwsh tools/ci/soak-test.ps1 -Minutes 480
    CI runs 5 minutes on every push, which catches fast leaks only.
#>
[CmdletBinding()]
param(
    [double] $Minutes = 5,
    [ValidateSet('x64', 'ARM64')]
    [string] $Platform = 'x64',
    [string] $Configuration = 'Release',
    [string] $ReportDirectory = (Join-Path ([System.IO.Path]::GetTempPath()) 'weatherly-soak')
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..' '..')
$project = Join-Path $repoRoot 'src' 'Weatherly.App' 'Weatherly.App.csproj'

dotnet build $project -c $Configuration -p:Platform=$Platform -p:WindowsPackageType=None
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}

$rid = if ($Platform -eq 'x64') { 'win-x64' } else { 'win-arm64' }
$exe = Get-ChildItem (Join-Path $repoRoot 'src' 'Weatherly.App' 'bin') -Recurse -Filter 'Weatherly.App.exe' |
    Where-Object { $_.FullName -match "[\\/]$Configuration[\\/]" -and $_.FullName -match "[\\/]$rid[\\/]" } |
    Select-Object -First 1
if (-not $exe) {
    throw "Weatherly.App.exe for $Configuration/$rid was not found."
}

New-Item -ItemType Directory -Force -Path $ReportDirectory | Out-Null
$report = Join-Path $ReportDirectory 'soak-report.txt'
Remove-Item $report -ErrorAction SilentlyContinue

$minutesText = $Minutes.ToString([System.Globalization.CultureInfo]::InvariantCulture)
Write-Host "=== Soak run, $minutesText minute(s) ==="
$process = Start-Process -FilePath $exe.FullName -ArgumentList @("--smoke-test=$report", "--soak=$minutesText", '--no-test-pattern') -PassThru
$null = $process.Handle

# The app ends itself after the given time; a few minutes more for start-up and the last sample.
if (-not $process.WaitForExit([int](($Minutes + 5) * 60 * 1000))) {
    $process.Kill()
    throw "The soak run did not finish within $($Minutes + 5) minutes."
}

if (Test-Path $report) {
    Write-Host '--- report ---'
    Get-Content $report | Write-Host
    Write-Host '--------------'
}

if ($process.ExitCode -ne 0) {
    throw "The soak run failed with exit code $($process.ExitCode)."
}

Write-Host 'Soak run passed.'
