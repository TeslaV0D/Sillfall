<#
.SYNOPSIS
    Builds the app unpackaged and runs its built-in smoke test, including a look at the real screen.

.DESCRIPTION
    Three runs of Weatherly.App.exe with --smoke-test=<report>. In each run the app checks itself
    (overlay windows, click-through, z-order, focus, first frame, idle rendering, window tracking
    after moving its own window) and holds its window while this script takes a screenshot:

      1. Calibration pattern, visible in screenshots: frame, corner markers and the center cross
         (on top of the app window) must be on screen.
      2. Same, excluded from capture (--exclude-from-capture): the pattern must not be on screen.
      3. Surface debug layer only (--show-surfaces --no-test-pattern): the green line must sit on
         the two rows directly above the app window, the orange lines on the two columns left and
         right of it. The window position comes from DWM, independently of the app's tracker.
      4. Calibration drops with the debug layer (--test-drops --show-surfaces --no-test-pattern): the app
         checks the render loop itself (frame rate, zero allocations, stop when idle). The screenshot must
         show drops resting on the app window's top edge and the debug lines beside it, both drawn by
         animated frames. (The debug lines are drawn after the sprites, so falling drops never cover them.)

    Then the sandbox runs for a few seconds and its window is captured for review.

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
[DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr window, int attribute, int[] rect, int size);
'@

# Visible frame of a window in physical pixels (DWMWA_EXTENDED_FRAME_BOUNDS = 9): left, top, right, bottom.
function Get-WindowBounds([IntPtr] $Window) {
    $rect = [int[]]::new(4)
    $result = [Weatherly.Ci.Native]::DwmGetWindowAttribute($Window, 9, $rect, 16)
    if ($result -ne 0) {
        throw "DwmGetWindowAttribute failed with 0x$($result.ToString('X8'))."
    }
    return @{ Left = $rect[0]; Top = $rect[1]; Right = $rect[2]; Bottom = $rect[3] }
}
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

# A probe expects a color (Match = $true) or expects anything but that color (Match = $false).
function New-Probe([string] $Name, [int] $X, [int] $Y, [int[]] $Color, [bool] $Match) {
    return @{ Name = $Name; X = $X; Y = $Y; R = $Color[0]; G = $Color[1]; B = $Color[2]; Match = $Match }
}

# An area probe expects at least MinimumCount pixels close to a color inside a rectangle.
function New-AreaProbe([string] $Name, [int] $Left, [int] $Top, [int] $Right, [int] $Bottom, [int[]] $Color, [int] $Tolerance, [int] $MinimumCount) {
    return @{ Name = $Name; Area = $true; Left = $Left; Top = $Top; Right = $Right; Bottom = $Bottom; R = $Color[0]; G = $Color[1]; B = $Color[2]; Tolerance = $Tolerance; MinimumCount = $MinimumCount }
}

function Measure-Area($Bitmap, $Probe) {
    $count = 0
    for ($y = [Math]::Max($Probe.Top, 0); $y -lt [Math]::Min($Probe.Bottom, $Bitmap.Height); $y++) {
        for ($x = [Math]::Max($Probe.Left, 0); $x -lt [Math]::Min($Probe.Right, $Bitmap.Width); $x++) {
            $pixel = $Bitmap.GetPixel($x, $y)
            if ([Math]::Abs($pixel.R - $Probe.R) -le $Probe.Tolerance -and [Math]::Abs($pixel.G - $Probe.G) -le $Probe.Tolerance -and [Math]::Abs($pixel.B - $Probe.B) -le $Probe.Tolerance) {
                $count++
            }
        }
    }
    return $count
}

$magenta = @(255, 0, 255)
$red = @(255, 0, 0)
$green = @(0, 255, 0)
$cyan = @(0, 255, 255)
$surfaceTop = @(51, 242, 77)     # debug color of top edges
$surfaceSide = @(255, 153, 26)   # debug color of window sides

# Calibration pattern on the primary monitor at 100 % scaling: the frame is 2 px, the corner markers
# end 26 px from the edge. In smoke test mode the app centers its window, so the center cross lies on
# top of it: this proves that the overlay stays above a window activated after it.
function Get-PatternProbes($Bitmap, $Window, [bool] $Visible) {
    $right = $Bitmap.Width - 1
    return @(
        (New-Probe 'frame top left' 0 0 $magenta $Visible),
        (New-Probe 'frame top right' $right 0 $magenta $Visible),
        (New-Probe 'red marker top left' 12 12 $red $Visible),
        (New-Probe 'green marker top right' ($right - 12) 12 $green $Visible),
        (New-Probe 'cyan cross over the app window' ([int]($Bitmap.Width / 2)) ([int]($Bitmap.Height / 2)) $cyan $Visible)
    )
}

# Debug layer at 100 % scaling: 2 px bands right outside the window frame, nothing on the frame itself
# and nothing one pixel further out.
function Get-SurfaceProbes($Bitmap, $Window) {
    $centerX = [int](($Window.Left + $Window.Right) / 2)
    $centerY = [int](($Window.Top + $Window.Bottom) / 2)
    return @(
        (New-Probe 'top band, row above the frame' $centerX ($Window.Top - 1) $surfaceTop $true),
        (New-Probe 'top band, second row above the frame' $centerX ($Window.Top - 2) $surfaceTop $true),
        (New-Probe 'third row above the frame is free' $centerX ($Window.Top - 3) $surfaceTop $false),
        (New-Probe 'top row of the frame is free' $centerX $Window.Top $surfaceTop $false),
        (New-Probe 'left band, column left of the frame' ($Window.Left - 1) $centerY $surfaceSide $true),
        (New-Probe 'left band, second column' ($Window.Left - 2) $centerY $surfaceSide $true),
        (New-Probe 'third column left of the frame is free' ($Window.Left - 3) $centerY $surfaceSide $false),
        (New-Probe 'left column of the frame is free' $Window.Left $centerY $surfaceSide $false),
        (New-Probe 'right band, column right of the frame' $Window.Right $centerY $surfaceSide $true),
        (New-Probe 'right band, second column' ($Window.Right + 1) $centerY $surfaceSide $true),
        (New-Probe 'third column right of the frame is free' ($Window.Right + 2) $centerY $surfaceSide $false)
    )
}

# Calibration drops resting on the app window's top edge: a 6 px disc centered 3 px above the frame,
# in the color (217, 235, 255) at 90 % alpha. The band above the green debug line holds their upper half.
# Whatever lies behind (terminal, desktop) shines through by 10 %, hence the generous tolerance.
function Get-DropProbes($Bitmap, $Window) {
    $restingDrop = @(205, 220, 240)
    return @(
        (New-AreaProbe 'resting drops on the app window' $Window.Left ($Window.Top - 6) $Window.Right ($Window.Top - 2) $restingDrop 30 20)
    ) + (Get-SurfaceProbes $Bitmap $Window | Where-Object { $_.Name -like 'left band*' -or $_.Name -like 'right band*' })
}

function Invoke-SmokeRun([string] $Name, [string[]] $ExtraArguments, [scriptblock] $Probes) {
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
        # The app names its window handle in the report. Process.MainWindowHandle is not reliable here:
        # the process also owns the (visible, topmost) overlay windows.
        $pattern = '^INFO\tApp window handle (\d+)$'
        $handleLine = Get-Content $report | Where-Object { $_ -match $pattern } | Select-Object -First 1
        if (-not ($handleLine -match $pattern)) {
            throw "Smoke run '$Name': the report names no app window handle."
        }
        $window = Get-WindowBounds ([IntPtr][long]$Matches[1])
        Write-Host "App window: ($($window.Left), $($window.Top)) - ($($window.Right), $($window.Bottom))"
        $bitmap = Get-PrimaryScreenshot $screenshot
        try {
            foreach ($probe in & $Probes $bitmap $window) {
                if ($probe.Area) {
                    $count = Measure-Area $bitmap $probe
                    $verdict = if ($count -ge $probe.MinimumCount) { 'ok' } else { 'FAILED' }
                    Write-Host "  probe $verdict`: $($probe.Name): $count pixel(s) near rgb($($probe.R), $($probe.G), $($probe.B)), at least $($probe.MinimumCount) expected"
                    if ($count -lt $probe.MinimumCount) {
                        $screenFailures += "$($probe.Name): $count pixel(s), at least $($probe.MinimumCount) expected"
                    }
                    continue
                }

                $found = Test-Color $bitmap $probe.X $probe.Y $probe.R $probe.G $probe.B
                $actual = $bitmap.GetPixel($probe.X, $probe.Y)
                $verdict = if ($found -eq $probe.Match) { 'ok' } else { 'FAILED' }
                Write-Host "  probe $verdict`: $($probe.Name) at ($($probe.X), $($probe.Y)) is rgb($($actual.R), $($actual.G), $($actual.B))"
                if ($found -ne $probe.Match) {
                    $expectation = if ($probe.Match) { 'expected' } else { 'not expected' }
                    $screenFailures += "$($probe.Name) at ($($probe.X), $($probe.Y)) is rgb($($actual.R), $($actual.G), $($actual.B)), rgb($($probe.R), $($probe.G), $($probe.B)) $expectation"
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

# The sandbox renders fake windows and drops into a normal window. It exits by itself and returns 0 if it rendered.
function Invoke-SandboxRun {
    Write-Host '=== Sandbox ==='
    $sandbox = Get-ChildItem (Join-Path $repoRoot 'tools' 'Weatherly.Sandbox' 'bin') -Recurse -Filter 'Weatherly.Sandbox.exe' |
        Where-Object { $_.FullName -match "[\\/]$Configuration[\\/]" } |
        Select-Object -First 1
    if (-not $sandbox) {
        throw 'Weatherly.Sandbox.exe was not found. Build the solution first.'
    }

    $process = Start-Process -FilePath $sandbox.FullName -ArgumentList '--exit-after=6' -PassThru
    $null = $process.Handle
    Start-Sleep -Seconds 4
    $bitmap = Get-PrimaryScreenshot (Join-Path $PreviewDirectory 'desktop-sandbox.png')
    $bitmap.Dispose()
    if (-not $process.WaitForExit(30000)) {
        $process.Kill()
        throw 'The sandbox did not exit.'
    }
    if ($process.ExitCode -ne 0) {
        throw "The sandbox failed with exit code $($process.ExitCode)."
    }
    Write-Host 'Sandbox passed.'
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

Invoke-SmokeRun -Name 'visible' -ExtraArguments @() -Probes { param($b, $w) Get-PatternProbes $b $w $true }
Invoke-SmokeRun -Name 'excluded-from-capture' -ExtraArguments @('--exclude-from-capture') -Probes { param($b, $w) Get-PatternProbes $b $w $false }
Invoke-SmokeRun -Name 'surfaces' -ExtraArguments @('--show-surfaces', '--no-test-pattern') -Probes { param($b, $w) Get-SurfaceProbes $b $w }
Invoke-SmokeRun -Name 'drops' -ExtraArguments @('--test-drops', '--show-surfaces', '--no-test-pattern') -Probes { param($b, $w) Get-DropProbes $b $w }
Invoke-SandboxRun

Write-Host 'Smoke test passed.'
