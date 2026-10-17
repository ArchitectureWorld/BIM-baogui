[CmdletBinding()]
param([string]$Server='http://127.0.0.1:55443/')
$ErrorActionPreference='Stop'
$webRoot=Split-Path -Parent $PSScriptRoot
if($Server -notin @('http://127.0.0.1:55443/','https://192.168.6.45:8443/')){throw 'Only the isolated test host or the approved production HTTPS server is permitted.'}
$scope=if($Server.StartsWith('https:')){'real model copies against production Linux HTTPS'}else{'real model copies against the isolated loopback host and dedicated test database'}
if(Get-Process -Name Revit -ErrorAction SilentlyContinue){throw 'An existing Revit process will not be interrupted.'}
if((Invoke-RestMethod -Uri ($Server.TrimEnd('/')+'/health/ready')).state -ne 'ready'){throw 'Dedicated experience host is not ready.'}
$baseline=Get-Content -LiteralPath (Join-Path $webRoot '.local/baseline.json')|ConvertFrom-Json
$sources=@($baseline.sourceModels|Where-Object {$_.path -like '*_干净.rvt'})
if($sources.Count -ne 3){throw 'Exactly three known clean source models are required.'}
$run=Join-Path $webRoot ('.local/full-experience-'+(Get-Date -Format yyyyMMdd-HHmmss));New-Item -ItemType Directory -Path (Join-Path $run 'models')|Out-Null
$copies=@();foreach($source in $sources){if((Get-FileHash -LiteralPath $source.path -Algorithm SHA256).Hash -ne $source.sha256){throw 'Original model changed.'};$copy=Join-Path $run ('models/'+[IO.Path]::GetFileName($source.path));Copy-Item -LiteralPath $source.path -Destination $copy;if((Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -ne $source.sha256){throw 'Model copy mismatch.'};$copies+=$copy}
$copies|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $run 'models.json') -Encoding utf8
@{server=$Server;scope=$scope}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $run 'full-flow.json') -Encoding utf8
$manifest=Join-Path $env:APPDATA 'Autodesk/Revit/Addins/2020/Hifc.Web.NativeTests.addin'
$assembly=Join-Path $webRoot 'tests/Hifc.Web.NativeTests/bin/Release/net48/Hifc.Web.NativeTests.dll'
[xml]$existing=Get-Content -LiteralPath $manifest
if($existing.RevitAddIns.AddIn.AddInId -ne '82c20f55-e1f1-451a-918d-075405720287' -or $existing.RevitAddIns.AddIn.Assembly -ne $assembly){throw 'The Web test manifest does not match this harness.'}
$previous=$env:HIFC_WEB_NATIVE_RUN
try{$env:HIFC_WEB_NATIVE_RUN=$run;$process=Start-Process -FilePath 'C:/Program Files/Autodesk/Revit 2020/Revit.exe' -WindowStyle Normal -PassThru}finally{$env:HIFC_WEB_NATIVE_RUN=$previous}
@{pid=$process.Id;run=$run;sources=$sources;server=$Server;scope=$scope}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $run 'launch.json') -Encoding utf8
Write-Output $run
