<#
.SYNOPSIS
    Builds the app unpackaged and runs its built-in smoke test, including a look at the real screen.

.DESCRIPTION
    Two runs of Weatherly.App.exe with --smoke-test=<report>:

      1. Overlay visible in screenshots. The app checks itself (overlay windows, click-through,
         focus, first frame, idle rendering). While it holds its window, this script takes a
         screenshot of the desktop and checks that the calibration pattern is really on screen:
         magenta frame in the corners, red and green corner markers.
      2. Overlay excluded from capture (--exclude-from-capture). The same screenshot must not
         show the pattern.

    Screenshots are saved to -PreviewDirectory so CI can upload them for review. Usable locally:

        pwsh tools/ci/smoke-test.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'ARM64')]
    [string] $Platform = 'x64',

    [string] $Configuration = 'Release',

    [string] $ReportDirectory = (Join-Path ([System.IO.Path]::GetTempPath()) 'weatherly-smoke'),

    [string] $PreviewDirectory = (Join-Path ([System.IO.Path]::GetTempPath()) 'weatherly-smoke'),

    [int] $TimeoutSeconds = 90
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..' '..')
$project = Join-Path $repoRoot 'src' 'Weatherly.App' 'Weatherly.App.csproj'

Add-Type -AssemblyName System.Drawing
Add-Type -Namespace Weatherly.Ci -Name Native -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
[DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
'@
# Physical pixels, like the overlay itself (-4 = DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2).
[void][Weatherly.Ci.Native]::SetProcessDpiAwarenessContext([IntPtr]::new(-4))

function Get-PrimaryScreenshot([string] $Path) {
    $width = [Weatherly.Ci.Native]::GetSystemMetrics(0)   # SM_CXSCREEN
    $height = [Weatherly.Ci.Native]::GetSystemMetrics(1)  # SM_CYSCREEN
    $bitmap = [System.Drawing.Bitmap]::new($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen(0, 0, 0, 0, $bitmap.Size)
    }
    finally {
        $graphics.Dispose()
    }
    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    return $bitmap
}

function Test-Color($Bitmap, [int] $X, [int] $Y, [int] $R, [int] $G, [int] $B) {
    $pixel = $Bitmap.GetPixel($X, $Y)
    $tolerance = 24
    return ([Math]::Abs($pixel.R - $R) -le $tolerance) -and ([Math]::Abs($pixel.G - $G) -le $tolerance) -and ([Math]::Abs($pixel.B - $B) -le $tolerance)
}

# Probe points on the primary monitor at 100 % scaling: the frame is 2 px, the corner markers end
# 26 px from the edge. In smoke test mode the app centers its window on the primary monitor, so the
# center cross lies on top of it: this proves that the overlay stays above a window activated after it.
function Get-PatternProbes($Bitmap) {
    $right = $Bitmap.Width - 1
    return @(
        @{ Name = 'frame top left'; X = 0; Y = 0; R = 255; G = 0; B = 255 },
        @{ Name = 'frame top right'; X = $right; Y = 0; R = 255; G = 0; B = 255 },
        @{ Name = 'red marker top left'; X = 12; Y = 12; R = 255; G = 0; B = 0 },
        @{ Name = 'green marker top right'; X = $right - 12; Y = 12; R = 0; G = 255; B = 0 },
        @{ Name = 'cyan cross over the app window'; X = [int]($Bitmap.Width / 2); Y = [int]($Bitmap.Height / 2); R = 0; G = 255; B = 255 }
    )
}

function Invoke-SmokeRun([string] $Name, [string[]] $ExtraArguments, [bool] $ExpectPatternOnScreen) {
    $report = Join-Path $ReportDirectory "smoke-report-$Name.txt"
    $screenshot = Join-Path $PreviewDirectory "desktop-$Name.png"
    Remove-Item $report -ErrorAction SilentlyContinue

    Write-Host "=== Smoke run '$Name' ==="
    $arguments = @("--smoke-test=$report", '--smoke-hold=4000') + $ExtraArguments
    $process = Start-Process -FilePath $script:exe.FullName -ArgumentList $arguments -PassThru
    $null = $process.Handle  # Keeps the handle open, otherwise ExitCode is empty after the process ends.

    # The report appears when the self-check is done. The app then holds its window for the screenshot.
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while (-not (Test-Path $report) -and -not $process.HasExited -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 200
    }

    $screenFailures = @()
    if ((Test-Path $report) -and -not $process.HasExited) {
        Start-Sleep -Milliseconds 500
        $bitmap = Get-PrimaryScreenshot $screenshot
        try {
            foreach ($probe in Get-PatternProbes $bitmap) {
                $found = Test-Color $bitmap $probe.X $probe.Y $probe.R $probe.G $probe.B
                if ($found -ne $ExpectPatternOnScreen) {
                    $actual = $bitmap.GetPixel($probe.X, $probe.Y)
                    $screenFailures += "$($probe.Name) at ($($probe.X), $($probe.Y)) is rgb($($actual.R), $($actual.G), $($actual.B)), pattern expected: $ExpectPatternOnScreen"
                }
            }
        }
        finally {
            $bitmap.Dispose()
        }
        Write-Host "Screenshot: $screenshot"
    }

    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        $process.Kill()
        throw "Smoke run '$Name' did not finish within $TimeoutSeconds s. The app hung or never showed its window."
    }

    if (Test-Path $report) {
        Write-Host '--- report ---'
        Get-Content $report | Write-Host
        Write-Host '--------------'
    }
    else {
        throw "Smoke run '$Name' wrote no report (exit code $($process.ExitCode))."
    }

    foreach ($failure in $screenFailures) {
        Write-Host "SCREEN FAILED: $failure"
    }

    if ($process.ExitCode -ne 0) {
        throw "Smoke run '$Name' failed with exit code $($process.ExitCode)."
    }

    if ($screenFailures.Count -gt 0) {
        throw "Smoke run '$Name': the screenshot does not match ($($screenFailures.Count) probe(s))."
    }

    Write-Host "Smoke run '$Name' passed."
}

New-Item -ItemType Directory -Force -Path $ReportDirectory, $PreviewDirectory | Out-Null

# Unpackaged, so the exe starts without MSIX deployment and brings the Windows App SDK runtime along.
# --no-incremental: the packaged build writes to the same folders. Reusing its output leaves the
# unpackaged exe without the WinUI resources (XamlParseException for themeresources.xaml).
dotnet build $project -c $Configuration -p:Platform=$Platform -p:WindowsPackageType=None --no-incremental
if ($LASTEXITCODE -ne 0) {
    throw "Building the unpackaged app failed with exit code $LASTEXITCODE."
}

$rid = "win-$($Platform.ToLowerInvariant())"
$script:exe = Get-ChildItem (Join-Path $repoRoot 'src' 'Weatherly.App' 'bin') -Recurse -Filter 'Weatherly.App.exe' |
    Where-Object { $_.FullName -match "[\\/]$Configuration[\\/]" -and $_.FullName -match "[\\/]$rid[\\/]" } |
    Select-Object -First 1
if (-not $script:exe) {
    throw "Weatherly.App.exe for $Configuration/$rid was not found."
}

Invoke-SmokeRun -Name 'visible' -ExtraArguments @() -ExpectPatternOnScreen $true
Invoke-SmokeRun -Name 'excluded-from-capture' -ExtraArguments @('--exclude-from-capture') -ExpectPatternOnScreen $false

Write-Host 'Smoke test passed.'
