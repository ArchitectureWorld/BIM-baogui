[CmdletBinding()]
param([string]$HelperPath='')
$ErrorActionPreference='Stop'
$webRoot=Split-Path -Parent $PSScriptRoot
$helper=if($HelperPath){$HelperPath}else{Join-Path $webRoot 'deploy/client/InstallPayload.ps1'}
if(Test-Path -LiteralPath $helper){. $helper}
if(-not(Get-Command New-HifcPayloadStage -ErrorAction SilentlyContinue)){throw 'Immutable installer payload staging is missing.'}
$run=Join-Path $webRoot ('.local/installer-payload-'+[Guid]::NewGuid().ToString('N'))
$package=Join-Path $run 'package';$root=Join-Path $run 'installation'
New-Item -ItemType Directory -Path (Join-Path $package 'client'),$root -Force|Out-Null
$names=@('Hifc.Web.Revit.dll','Hifc.Web.Client.dll','Hifc.Web.Contracts.dll','Newtonsoft.Json.dll','Hifc.Web.Update.exe','Hifc.Web.Update.exe.config')
foreach($name in $names){[IO.File]::WriteAllText((Join-Path $package ('client/'+$name)),'verified fixture '+$name)}
$metadata=[pscustomobject]@{version='0.5.0-Web';addInId='53ef39d3-a187-4fb5-90f8-29e91d747602';releaseId='0.5.0-dev-20261029T000000Z';sourceCommit=('a'*40);files=@($names|ForEach-Object {$p=Join-Path $package ('client/'+$_);[pscustomobject]@{name=$_;sha256=(Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash;length=(Get-Item -LiteralPath $p).Length}})}
function Save-Metadata {$metadata|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $package 'package.json') -Encoding utf8}
function Assert([bool]$value,[string]$message){if(-not $value){throw $message}}
Save-Metadata
$stage=New-HifcPayloadStage -PackageRoot $package -InstallationRoot $root -Package $metadata
Assert (-not(Test-Path -LiteralPath $stage.TargetDirectory)) 'A candidate was activated before component verification.'
Assert (Test-Path -LiteralPath (Join-Path $stage.CandidateDirectory 'package.json')) 'Candidate metadata is missing.'
$version=Complete-HifcPayloadStage -Stage $stage
[IO.File]::WriteAllText((Join-Path $root 'current.fixture'),$version)
$hashes=@{};foreach($name in $names){$hashes[$name]=(Get-FileHash -LiteralPath (Join-Path $version $name) -Algorithm SHA256).Hash}
Write-Output 'PASS new payload stages before publication'
$again=New-HifcPayloadStage -PackageRoot $package -InstallationRoot $root -Package $metadata
Assert $again.Existing 'Verified existing release was not reused.'
Assert ($again.CandidateDirectory -eq $version) 'Same release used a mutable alternate directory.'
Write-Output 'PASS same release is reused without copying'
[IO.File]::AppendAllText((Join-Path $package 'client/Hifc.Web.Revit.dll'),'different payload')
$metadata.files[0].sha256=(Get-FileHash -LiteralPath (Join-Path $package 'client/Hifc.Web.Revit.dll') -Algorithm SHA256).Hash
$metadata.files[0].length=(Get-Item -LiteralPath (Join-Path $package 'client/Hifc.Web.Revit.dll')).Length;Save-Metadata
$rejected=$false;try{New-HifcPayloadStage -PackageRoot $package -InstallationRoot $root -Package $metadata|Out-Null}catch{$rejected=$true}
Assert $rejected 'A changed payload reused an already published release id.'
foreach($name in $names){Assert ((Get-FileHash -LiteralPath (Join-Path $version $name) -Algorithm SHA256).Hash -eq $hashes[$name]) 'An active release file changed.'}
Write-Output 'PASS mismatched same release is rejected without changing active bytes'
$metadata.releaseId='0.5.0-dev-20261029T000001Z';Save-Metadata
$pending=New-HifcPayloadStage -PackageRoot $package -InstallationRoot $root -Package $metadata
Remove-HifcPayloadStage -Stage $pending
Assert (-not(Test-Path -LiteralPath $pending.CandidateDirectory)) 'Failed component preparation left staging active.'
Assert ([IO.File]::ReadAllText((Join-Path $root 'current.fixture')) -eq $version) 'A failed candidate changed the current pointer.'
Write-Output 'PASS failed component preparation preserves current installation'
Remove-Item -LiteralPath (Join-Path $package 'client/Hifc.Web.Update.exe.config')
$rejected=$false;try{New-HifcPayloadStage -PackageRoot $package -InstallationRoot $root -Package $metadata|Out-Null}catch{$rejected=$true}
Assert $rejected 'An incomplete copy was accepted.'
Assert (@(Get-ChildItem -LiteralPath (Join-Path $root 'releases') -Directory|Where-Object Name -Like '.install-*').Count -eq 0) 'An incomplete staging directory survived.'
foreach($name in $names){Assert ((Get-FileHash -LiteralPath (Join-Path $version $name) -Algorithm SHA256).Hash -eq $hashes[$name]) 'Copy failure changed active payload.'}
Write-Output 'PASS copy failure cannot damage the active release'
@{passed=5;failed=0;run=$run;scope='real isolated filesystem; no Revit process or installation touched'}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $run 'result.json') -Encoding utf8
Write-Output ('PASS immutable installer payload: '+$run)
