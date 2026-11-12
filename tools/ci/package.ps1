<#
.SYNOPSIS
    Builds the MSIX package for one architecture and signs it with a development certificate.

.DESCRIPTION
    Publishes Sillfall.App as an MSIX (x64 or ARM64). The identity stamped into the manifest is the
    development identity, unless the reserved store identity is passed in:

        SILLFALL_PACKAGE_NAME
        SILLFALL_PACKAGE_PUBLISHER            certificate subject, must start with CN=
        SILLFALL_PUBLISHER_DISPLAY_NAME

    Two packages are written:

      <Product>_<version>_<arch>.unsigned.msix   for Partner Center, which signs it itself
      <Product>_<version>_<arch>.msix            signed with a certificate created for this run
      <Product>_<version>_<arch>.cer             that certificate, so the signed package can be sideloaded

    The private key is not kept. On a version tag the tag must be v<version> from Directory.Build.props.

        pwsh tools/ci/package.ps1 -Platform x64 -OutputDirectory .\packages
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'ARM64')]
    [string] $Platform = 'x64',

    [string] $Configuration = 'Release',

    [string] $OutputDirectory = (Join-Path ([System.IO.Path]::GetTempPath()) 'sillfall-package')
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..' '..')
$project = Join-Path $repoRoot 'src' 'Sillfall.App' 'Sillfall.App.csproj'

function Read-BuildProperty([string] $Name) {
    [xml] $props = Get-Content -Raw (Join-Path $repoRoot 'Directory.Build.props')
    $value = @($props.Project.PropertyGroup.$Name) | Where-Object { $_ }
    if (-not $value) {
        throw "Directory.Build.props has no $Name."
    }
    return [string] $value[0]
}

function Get-SignTool {
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $tool = Get-ChildItem -Path $kits -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.DirectoryName -match '\\x64$' } |
        Sort-Object { $_.Directory.Parent.Name } -Descending |
        Select-Object -First 1
    if (-not $tool) {
        throw "signtool.exe was not found under $kits."
    }
    return $tool.FullName
}

function Test-PackageEntry([string] $Path, [string] $EntryName) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        return $null -ne $zip.GetEntry($EntryName)
    }
    finally {
        $zip.Dispose()
    }
}

$version = Read-BuildProperty 'Version'
$product = Read-BuildProperty 'Product'
$packageVersion = if ($version.Split('.').Count -eq 3) { "$version.0" } else { $version }

if ($env:GITHUB_REF_TYPE -eq 'tag' -and $env:GITHUB_REF_NAME -ne "v$version") {
    throw "Tag $($env:GITHUB_REF_NAME) does not match v$version from Directory.Build.props."
}

$publisher = $env:SILLFALL_PACKAGE_PUBLISHER
if ($publisher -and $publisher -notmatch '^CN=.+') {
    throw 'SILLFALL_PACKAGE_PUBLISHER must be a certificate subject starting with CN=.'
}

$rawDir = Join-Path $OutputDirectory 'raw'
New-Item -ItemType Directory -Force -Path $rawDir | Out-Null

$publish = @(
    'publish', $project,
    '-c', $Configuration,
    '-p:Platform=' + $Platform,
    '-p:RuntimeIdentifier=win-' + $Platform.ToLowerInvariant(),
    '-p:WindowsPackageType=MSIX',
    '-p:GenerateAppxPackageOnBuild=true',
    '-p:AppxBundle=Never',
    '-p:AppxPackageSigningEnabled=false',
    '-p:AppxPackageDir=' + $rawDir
)
if ($env:SILLFALL_PACKAGE_NAME) {
    $publish += '-p:SillfallPackageName=' + $env:SILLFALL_PACKAGE_NAME
}
if ($publisher) {
    $publish += '-p:SillfallPackagePublisher=' + $publisher
}
if ($env:SILLFALL_PUBLISHER_DISPLAY_NAME) {
    $publish += '-p:SillfallPackagePublisherDisplayName=' + $env:SILLFALL_PUBLISHER_DISPLAY_NAME
}

Write-Host "Publishing $product $packageVersion ($Platform)"
& dotnet @publish
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

# The Windows App Runtime sits next to the app package, under Dependencies, already signed by Microsoft.
# It is not our package. Sideload needs it when the runtime is not installed yet.
$built = @(Get-ChildItem -Path $rawDir -Recurse -Filter *.msix | Where-Object { $_.FullName -notmatch '\\Dependencies\\' })
if ($built.Count -ne 1) {
    $names = ($built | ForEach-Object { $_.FullName }) -join ', '
    throw "Expected one app .msix under $rawDir, found $($built.Count): $names"
}

$arch = $Platform.ToLowerInvariant()
$baseName = "${product}_${packageVersion}_${arch}"
$unsigned = Join-Path $OutputDirectory "$baseName.unsigned.msix"
$signed = Join-Path $OutputDirectory "$baseName.msix"
Copy-Item $built[0].FullName $unsigned -Force
Copy-Item $unsigned $signed -Force
$dependencies = Get-ChildItem -Path $rawDir -Recurse -Directory -Filter Dependencies | Select-Object -First 1
if ($dependencies) {
    Copy-Item $dependencies.FullName (Join-Path $OutputDirectory 'Dependencies') -Recurse -Force
}
Remove-Item $rawDir -Recurse -Force

if (-not (Test-PackageEntry $unsigned 'AppxManifest.xml')) {
    throw "$unsigned has no AppxManifest.xml."
}
if (Test-PackageEntry $unsigned 'AppxSignature.p7x') {
    throw "$unsigned is already signed; the store upload has to be unsigned."
}

# The certificate subject is the publisher the manifest was stamped with. Read it back from the package
# so a mismatch fails here instead of at install time.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($unsigned)
try {
    $entry = $zip.GetEntry('AppxManifest.xml')
    if (-not $entry) {
        throw "$unsigned has no AppxManifest.xml."
    }
    $reader = [System.IO.StreamReader]::new($entry.Open())
    try {
        $manifest = [xml] $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }
}
finally {
    $zip.Dispose()
}
$ns = New-Object System.Xml.XmlNamespaceManager($manifest.NameTable)
$ns.AddNamespace('m', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
$identity = $manifest.SelectSingleNode('/m:Package/m:Identity', $ns)
if (-not $identity) {
    throw "AppxManifest.xml in $unsigned has no package identity."
}
$subject = [string] $identity.Publisher
if ($identity.Version -ne $packageVersion) {
    throw "Package version $($identity.Version) does not match $packageVersion."
}

$certificate = New-SelfSignedCertificate -Subject $subject -Type CodeSigningCert -CertStoreLocation 'Cert:\CurrentUser\My' -HashAlgorithm SHA256 -KeyExportPolicy Exportable -NotAfter (Get-Date).AddYears(1)
$pfx = Join-Path $OutputDirectory "$baseName.pfx"
$password = [guid]::NewGuid().ToString('N')
$secure = ConvertTo-SecureString -String $password -Force -AsPlainText
try {
    Export-PfxCertificate -Cert $certificate -FilePath $pfx -Password $secure | Out-Null
    Export-Certificate -Cert $certificate -FilePath (Join-Path $OutputDirectory "$baseName.cer") | Out-Null

    $signtool = Get-SignTool
    & $signtool sign /fd SHA256 /f $pfx /p $password /tr http://timestamp.acs.microsoft.com /td SHA256 $signed
    if ($LASTEXITCODE -ne 0) {
        Write-Warning 'The timestamp server did not sign; retrying without a timestamp. The package stays valid for the certificate lifetime.'
        & $signtool sign /fd SHA256 /f $pfx /p $password $signed
        if ($LASTEXITCODE -ne 0) {
            throw "signtool failed with exit code $LASTEXITCODE."
        }
    }
}
finally {
    Remove-Item $pfx -Force -ErrorAction SilentlyContinue
    Remove-Item -Path $certificate.PSPath -Force -ErrorAction SilentlyContinue
}

if (-not (Test-PackageEntry $signed 'AppxSignature.p7x')) {
    throw "$signed has no signature."
}

$sums = Join-Path $OutputDirectory "SHA256SUMS-$arch.txt"
@(
    Get-FileHash -Algorithm SHA256 $unsigned
    Get-FileHash -Algorithm SHA256 $signed
) | ForEach-Object { '{0}  {1}' -f $_.Hash.ToLowerInvariant(), (Split-Path $_.Path -Leaf) } |
    Set-Content -Encoding ascii $sums

Write-Host "Unsigned: $unsigned"
Write-Host "Sideload: $signed"
Write-Host "Publisher: $subject"
