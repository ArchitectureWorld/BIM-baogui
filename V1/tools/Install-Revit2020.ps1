[CmdletBinding()]
param([string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
if (Get-Process -Name Revit -ErrorAction SilentlyContinue) { throw 'Close Revit normally before installing. No process will be stopped.' }
$workspace = Split-Path -Parent $PSScriptRoot
# Default to the user-authorized, verified trial package. Building another
# preview does not automatically install it; an explicit package may be used.
if (-not $PackageDirectory) { $PackageDirectory = Join-Path $workspace 'dist\Hifc.V1.Revit2020-0.4.19-preview.1' }
$package = (Resolve-Path -LiteralPath $PackageDirectory).Path
$addinRoot = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Autodesk\Revit\Addins\2020'
$target = Join-Path $addinRoot 'Hifc.V1'
$manifestPath = Join-Path $addinRoot 'Hifc.V1.addin'
foreach ($sourceName in @('Hifc.V1.Revit.dll','Hifc.Reference.Core.dll','settings.json','build-manifest.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $package $sourceName))) { throw "Build package first: $sourceName" }
}
$buildManifest = Get-Content -LiteralPath (Join-Path $package 'build-manifest.json') -Raw | ConvertFrom-Json
$packageVersion = [string]$buildManifest.version
if ($packageVersion -notmatch '^\d+\.\d+\.\d+(?:-[a-zA-Z0-9.-]+)?$') { throw 'Invalid package version.' }
foreach ($entry in $buildManifest.files) {
    if ([IO.Path]::GetFileName([string]$entry.name) -ne [string]$entry.name) { throw 'Invalid manifest file name.' }
    $file = Join-Path $package $entry.name
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $entry.sha256 -or (Get-Item -LiteralPath $file).Length -ne $entry.bytes) { throw "Package manifest mismatch: $($entry.name)" }
}
$oldPlugin = Join-Path $addinRoot 'BIMBaoGui.RevitAddin\BIMBaoGui.RevitAddin.dll'
$oldHash = if (Test-Path -LiteralPath $oldPlugin) { (Get-FileHash -LiteralPath $oldPlugin -Algorithm SHA256).Hash } else { $null }
$run = Join-Path $workspace ('outputs\install-' + (Get-Date -Format yyyyMMdd-HHmmss) + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $run -Force | Out-Null
if (Test-Path -LiteralPath $target) { Copy-Item -LiteralPath $target -Destination (Join-Path $run 'previous-Hifc.V1') -Recurse }
if (Test-Path -LiteralPath $manifestPath) { Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $run 'previous-Hifc.V1.addin') }
New-Item -ItemType Directory -Path $target -Force | Out-Null
foreach ($name in @('Hifc.V1.Revit.dll','Hifc.Reference.Core.dll','Hifc.V1.Revit.pdb','Hifc.Reference.Core.pdb','settings.json','build-manifest.json')) {
    Copy-Item -LiteralPath (Join-Path $package $name) -Destination (Join-Path $target $name) -Force
}
# Previous V1 files have been backed up above. Remove only the two obsolete
# files in our own install directory; other plugins remain untouched.
foreach ($legacy in @('Hifc.Revit.dll','Hifc.Revit.pdb')) {
    $legacyPath = Join-Path $target $legacy
    if (Test-Path -LiteralPath $legacyPath) { Remove-Item -LiteralPath $legacyPath }
}
$assembly = [System.Security.SecurityElement]::Escape((Join-Path $target 'Hifc.V1.Revit.dll'))
$xml = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>H-IFC V1</Name>
    <Assembly>$assembly</Assembly>
    <AddInId>e3075f38-779a-4b07-9423-dd7c737b7139</AddInId>
    <FullClassName>Hifc.Revit.App</FullClassName>
    <VendorId>HIV1</VendorId>
    <VendorDescription>Reference mapping validation</VendorDescription>
  </AddIn>
</RevitAddIns>
"@
[IO.File]::WriteAllText($manifestPath, $xml, [Text.UTF8Encoding]::new($false))
$newOldHash = if (Test-Path -LiteralPath $oldPlugin) { (Get-FileHash -LiteralPath $oldPlugin -Algorithm SHA256).Hash } else { $null }
if ($oldHash -ne $newOldHash) { throw 'Existing BIMBaoGui binary changed unexpectedly.' }
$files = @('Hifc.V1.Revit.dll','Hifc.Reference.Core.dll') | ForEach-Object {
    $sourceHash=(Get-FileHash -LiteralPath (Join-Path $package $_) -Algorithm SHA256).Hash
    $installedHash=(Get-FileHash -LiteralPath (Join-Path $target $_) -Algorithm SHA256).Hash
    if ($sourceHash -ne $installedHash) { throw "Installed hash mismatch: $_" }
    @{ name=$_; sha256=$installedHash.ToLowerInvariant(); matches_build=$true }
}
@{ version=$packageVersion; source_package=$package; installed_directory=$target; addin_manifest=$manifestPath; files=@($files); previous_plugin_sha256=$oldHash; previous_plugin_unchanged=($oldHash -eq $newOldHash) } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $run 'install-receipt.json') -Encoding utf8
Write-Output "Installed: $target"
Write-Output "Receipt: $(Join-Path $run 'install-receipt.json')"
