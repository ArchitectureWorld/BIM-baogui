[CmdletBinding()]
param([string]$Server='http://127.0.0.1:55443/',[switch]$WaitForRootTrust,[switch]$VerifyAutoUpdate,[string]$PreviousClientPackage)
$ErrorActionPreference='Stop'
$webRoot=Split-Path -Parent $PSScriptRoot
if($Server -notin @('http://127.0.0.1:55443/','https://192.168.6.45:8443/')){throw 'Only the isolated test host or the approved production HTTPS server is permitted.'}
$scope=if($Server.StartsWith('https:')){'real model copies against production Linux HTTPS'}else{'real model copies against the isolated loopback host and dedicated test database'}
if(Get-Process -Name Revit -ErrorAction SilentlyContinue){throw 'An existing Revit process will not be interrupted.'}
if($WaitForRootTrust){
    if($Server -ne 'https://192.168.6.45:8443/'){throw 'Root trust waiting is restricted to the approved production endpoint.'}
    $release=Get-Content -LiteralPath (Join-Path $webRoot '.local/latest-server-release.json')|ConvertFrom-Json
    & python (Join-Path $PSScriptRoot 'Verify-WebServer.py') --phase after --accounts-enabled --expected-revision $release.sourceCommit
    if($LASTEXITCODE -ne 0){throw 'Pinned-CA production verification failed.'}
}elseif((Invoke-RestMethod -Uri ($Server.TrimEnd('/')+'/health/ready')).state -ne 'ready'){throw 'Experience host is not ready.'}
$baseline=Get-Content -LiteralPath (Join-Path $webRoot '.local/baseline.json')|ConvertFrom-Json
$sources=@($baseline.sourceModels|Where-Object {$_.path -like '*_干净.rvt'})
if($sources.Count -ne 3){throw 'Exactly three known clean source models are required.'}
$run=Join-Path $webRoot ('.local/full-experience-'+(Get-Date -Format yyyyMMdd-HHmmss));New-Item -ItemType Directory -Path (Join-Path $run 'models')|Out-Null
$copies=@();foreach($source in $sources){if((Get-FileHash -LiteralPath $source.path -Algorithm SHA256).Hash -ne $source.sha256){throw 'Original model changed.'};$copy=Join-Path $run ('models/'+[IO.Path]::GetFileName($source.path));Copy-Item -LiteralPath $source.path -Destination $copy;if((Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -ne $source.sha256){throw 'Model copy mismatch.'};$copies+=$copy}
$copies|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $run 'models.json') -Encoding utf8
@{server=$Server;scope=$scope}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $run 'full-flow.json') -Encoding utf8
if($VerifyAutoUpdate){
    if($Server -ne 'https://192.168.6.45:8443/' -or -not $PreviousClientPackage){throw 'Automatic update validation requires the real zetta HTTPS server and a known previous client package.'}
    $old=Get-Content -LiteralPath (Join-Path $PreviousClientPackage 'package.json') -Raw|ConvertFrom-Json
    $latest=Get-Content -LiteralPath (Join-Path $webRoot '.local/latest-client-release.json') -Raw|ConvertFrom-Json
    $localAppData=Join-Path $run 'update-lifecycle/localappdata';$appData=Join-Path $run 'update-lifecycle/appdata'
    $root=Join-Path $localAppData 'HIFC/0.5.0-Web/Revit2020';$addins=Join-Path $appData 'Autodesk/Revit/Addins/2020'
    New-Item -ItemType Directory -Path $root,$addins -Force|Out-Null
    foreach($file in $old.files){$source=Join-Path $PreviousClientPackage ('client/'+$file.name);if((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $file.sha256){throw 'Previous released client payload changed.'};Copy-Item -LiteralPath $source -Destination (Join-Path $root $file.name)}
    if(-not $old.releaseId){$old|Add-Member -NotePropertyName releaseId -NotePropertyValue ('0.5.0-dev-'+([DateTime]$old.createdUtc).ToUniversalTime().ToString('yyyyMMddTHHmmssZ'))}
    $old|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $root 'package.json') -Encoding utf8
    Copy-Item -LiteralPath (Join-Path $latest.package 'update-public-key.json') -Destination (Join-Path $root 'update-public-key.json')
    $owned=Join-Path $addins 'Hifc.Web.addin';$local=Join-Path $addins 'Hifc.V1.addin'
    $target=[Security.SecurityElement]::Escape((Join-Path $root 'Hifc.Web.Revit.dll'))
    [IO.File]::WriteAllText($owned,('<RevitAddIns><AddIn Type="Application"><AddInId>53ef39d3-a187-4fb5-90f8-29e91d747602</AddInId><Assembly>'+ $target +'</Assembly><FullClassName>Hifc.Web.Revit.WebApp</FullClassName></AddIn></RevitAddIns>'),[Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($local,'Protected Local sentinel; not loaded in Revit',[Text.UTF8Encoding]::new($false))
    @{root=$root;manifest=$owned;localManifest=$local;localAppData=$localAppData;appData=$appData}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $run 'update-lifecycle.json') -Encoding utf8
}
$manifest=Join-Path $env:APPDATA 'Autodesk/Revit/Addins/2020/Hifc.Web.NativeTests.addin'
$assembly=Join-Path $webRoot 'tests/Hifc.Web.NativeTests/bin/Release/net48/Hifc.Web.NativeTests.dll'
[xml]$existing=Get-Content -LiteralPath $manifest
if($existing.RevitAddIns.AddIn.AddInId -ne '82c20f55-e1f1-451a-918d-075405720287' -or $existing.RevitAddIns.AddIn.Assembly -ne $assembly){throw 'The Web test manifest does not match this harness.'}
$previous=$env:HIFC_WEB_NATIVE_RUN
try{$env:HIFC_WEB_NATIVE_RUN=$run;$process=Start-Process -FilePath 'C:/Program Files/Autodesk/Revit 2020/Revit.exe' -WindowStyle Normal -PassThru}finally{$env:HIFC_WEB_NATIVE_RUN=$previous}
@{pid=$process.Id;run=$run;sources=$sources;server=$Server;scope=$scope}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $run 'launch.json') -Encoding utf8
Write-Output $run
