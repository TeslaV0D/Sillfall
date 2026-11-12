<#
.SYNOPSIS
    Builds the setup .exe for one architecture (ADR-0018).

.DESCRIPTION
    Publishes Sillfall.App unpackaged and self-contained (the .NET runtime and the Windows App SDK travel with the
    app), then wraps the folder with Inno Setup (tools/installer/Sillfall.iss). Inno Setup 6.3 or newer has to be
    installed; on a CI runner without it, the script installs it with Chocolatey.

    Output in -OutputDirectory:
      Sillfall-Setup-<version>-<arch>.exe
      SHA256SUMS-setup-<arch>.txt

.EXAMPLE
    pwsh tools/ci/installer.ps1 -Platform x64
#>
param(
    [ValidateSet('x64', 'ARM64')]
    [string] $Platform = 'x64',

    [string] $Configuration = 'Release',

    [string] $OutputDirectory = (Join-Path ([System.IO.Path]::GetTempPath()) 'sillfall-installer')
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..' '..')
$project = Join-Path $repoRoot 'src' 'Sillfall.App' 'Sillfall.App.csproj'
$script = Join-Path $repoRoot 'tools' 'installer' 'Sillfall.iss'
$exeName = 'Sillfall.App.exe'

function Read-BuildProperty([string] $Name) {
    [xml] $props = Get-Content -Raw (Join-Path $repoRoot 'Directory.Build.props')
    # @() around the whole pipeline: a single match must stay an array, or [0] takes its first character.
    $value = @(@($props.Project.PropertyGroup.$Name) | Where-Object { $_ })
    if (-not $value) {
        throw "Directory.Build.props has no $Name."
    }
    return [string] $value[0]
}

function Get-InnoSetupCompiler {
    $candidates = @(
        (Get-Command iscc.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    ) | Where-Object { $_ -and (Test-Path $_) }
    return $candidates | Select-Object -First 1
}

$version = Read-BuildProperty 'Version'
if ($env:GITHUB_REF_TYPE -eq 'tag' -and $env:GITHUB_REF_NAME -ne "v$version") {
    throw "Tag $($env:GITHUB_REF_NAME) does not match v$version from Directory.Build.props."
}

$arch = $Platform.ToLowerInvariant()
$publishDir = Join-Path $OutputDirectory "publish-$arch"
if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

$publish = @(
    'publish', $project,
    '-c', $Configuration,
    "-p:Platform=$Platform",
    "-p:RuntimeIdentifier=win-$arch",
    '-p:WindowsPackageType=None',
    '--self-contained', 'true',
    '-o', $publishDir
)
Write-Host "Publishing Sillfall $version ($Platform, unpackaged)"
& dotnet @publish
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}
if (-not (Test-Path (Join-Path $publishDir $exeName))) {
    throw "$exeName is missing in $publishDir."
}

$iscc = Get-InnoSetupCompiler
if (-not $iscc) {
    if (-not $env:CI) {
        throw 'Inno Setup 6 was not found. Install it from https://jrsoftware.org/isinfo.php and run again.'
    }
    Write-Host 'Installing Inno Setup with Chocolatey'
    & choco install innosetup --yes --no-progress
    if ($LASTEXITCODE -ne 0) {
        throw "choco install innosetup failed with exit code $LASTEXITCODE."
    }
    $iscc = Get-InnoSetupCompiler
    if (-not $iscc) {
        throw 'ISCC.exe was not found after installing Inno Setup.'
    }
}

Write-Host "Compiling the setup with $iscc"
& $iscc "/DAppVersion=$version" "/DArch=$arch" "/DPublishDir=$publishDir" "/O$OutputDirectory" $script
if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed with exit code $LASTEXITCODE."
}

$setup = Join-Path $OutputDirectory "Sillfall-Setup-$version-$arch.exe"
if (-not (Test-Path $setup)) {
    throw "Expected $setup."
}
Remove-Item $publishDir -Recurse -Force

$hash = Get-FileHash -Algorithm SHA256 $setup
'{0}  {1}' -f $hash.Hash.ToLowerInvariant(), (Split-Path $setup -Leaf) |
    Set-Content -Encoding ascii (Join-Path $OutputDirectory "SHA256SUMS-setup-$arch.txt")

Write-Host "Setup: $setup"
