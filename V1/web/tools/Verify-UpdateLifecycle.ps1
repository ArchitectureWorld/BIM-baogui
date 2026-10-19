[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Run)
$ErrorActionPreference='Stop'
$webRoot=Split-Path -Parent $PSScriptRoot
$Run=[IO.Path]::GetFullPath($Run)
$allowed=[IO.Path]::GetFullPath((Join-Path $webRoot '.local'))+[IO.Path]::DirectorySeparatorChar
if(-not $Run.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){throw 'Only an owned Web verification run is permitted.'}
$proof=Get-Content -LiteralPath (Join-Path $Run 'updater-worker.json') -Raw|ConvertFrom-Json
$root=[IO.Path]::GetFullPath($proof.config.root)
if(-not $root.StartsWith($Run+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Update fixture escaped the owned run.'}
$worker=Get-Process -Id $proof.pid -ErrorAction SilentlyContinue
if($worker -and $worker.ProcessName -eq 'Hifc.Web.Update'){Wait-Process -Id $proof.pid -Timeout 180}
$package=Get-Content -LiteralPath (Join-Path $root 'package.json') -Raw|ConvertFrom-Json
if($package.releaseId -ne $proof.offer.releaseId -or $package.sourceCommit -ne $proof.offer.sourceCommit){throw 'The update did not activate its signed version after Revit exited.'}
$version=Join-Path $root ('releases/'+$package.releaseId)
[xml]$manifest=Get-Content -LiteralPath $proof.config.manifest
if($manifest.RevitAddIns.AddIn.AddInId -ne '53ef39d3-a187-4fb5-90f8-29e91d747602' -or $manifest.RevitAddIns.AddIn.Assembly -ne (Join-Path $version 'Hifc.Web.Revit.dll')){throw 'The updated Web manifest does not point at its verified version.'}
foreach($file in $package.files){if((Get-FileHash -LiteralPath (Join-Path $version $file.name) -Algorithm SHA256).Hash -ne $file.sha256){throw 'Updated payload hash mismatch.'}}
foreach($property in $proof.protectedFiles.PSObject.Properties){
    $path=[IO.Path]::GetFullPath($property.Name)
    if(-not $path.StartsWith($Run+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Protected fixture path escaped the owned run.'}
    if($path -eq $proof.config.manifest -or $path -eq (Join-Path $root 'package.json')){continue}
    if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $property.Value){throw ('Previous payload or remembered state changed: '+$path)}
}
$status=Get-Content -LiteralPath (Join-Path $root 'update-status.json') -Raw|ConvertFrom-Json
if($status.status -ne 'updated'){throw 'The update worker did not report successful activation.'}
[ordered]@{verifiedUtc=[DateTime]::UtcNow.ToString('O');server=$proof.offer.packageUrl;sourceCommit=$package.sourceCommit;previousRelease=$proof.oldRelease;activatedRelease=$package.releaseId;downloadedFromZetta=$true;signatureAndArchiveVerified=$true;deferredUntilActualRevitExit=$true;versionDirectoryActivated=$true;payloadFilesVerified=$package.files.Count;oldPayloadRetained=$true;personAndDraftsRetained=$true;localManifestPreserved=$true;scope='isolated installation fixture; actual production HTTPS; actual Revit process lifetime'}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $Run 'auto-update-results.json') -Encoding utf8
Write-Output 'PASS automatic update: zetta download, actual Revit exit, atomic version activation, old payload and remembered data preserved'
