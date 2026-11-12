# Publishes an unsigned MSIX for one architecture.
# Store signing replaces this package; the artifact is for inspection and for a later winget upload.
# Usage (repo root, Windows):
#   pwsh tools/ci/package.ps1 -Platform x64 -OutputDirectory artifacts/msix
param(
    [ValidateSet('x64', 'ARM64')]
    [string] $Platform = 'x64',
    [string] $Configuration = 'Release',
    [string] $OutputDirectory = 'artifacts/msix'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$project = Join-Path $repoRoot 'src\Sillfall.App\Sillfall.App.csproj'

Write-Host "Publishing Sillfall ($Configuration, $Platform)"
dotnet publish $project `
    -c $Configuration `
    -p:Platform=$Platform `
    -p:GenerateAppxPackageOnBuild=true `
    -p:AppxPackageSigningEnabled=false `
    -p:AppxBundle=Never `
    -p:UapAppxPackageBuildMode=SideloadOnly
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }

$bin = Join-Path $repoRoot "src\Sillfall.App\bin\$Platform\$Configuration"
$packages = @(Get-ChildItem $bin -Recurse -Include *.msix, *.msixbundle, *.msixupload -File -ErrorAction SilentlyContinue)
if ($packages.Count -eq 0) {
    throw "No MSIX was produced under $bin."
}

New-Item -ItemType Directory -Force -Path (Join-Path $repoRoot $OutputDirectory) | Out-Null
$arch = $Platform.ToLowerInvariant()
foreach ($package in $packages) {
    $name = "Sillfall-0.1.0-$arch$($package.Extension)"
    if ($packages.Count -gt 1) {
        $name = "Sillfall-0.1.0-$arch-$($package.BaseName)$($package.Extension)"
    }

    $dest = Join-Path $repoRoot (Join-Path $OutputDirectory $name)
    Copy-Item $package.FullName $dest -Force
    Write-Host "Package: $dest"
}
