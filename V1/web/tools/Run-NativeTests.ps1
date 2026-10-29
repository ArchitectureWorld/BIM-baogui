[CmdletBinding()]
param([string]$SourceModel='D:\18_建模项目\湖北BIM云平台\测试原始文件\01\revit原文件\总图.0008_干净.rvt', [ValidateSet('Hidden','Normal')][string]$WindowStyle='Normal')
$ErrorActionPreference='Stop'
$webRoot=Split-Path -Parent $PSScriptRoot
if(Get-Process -Name Revit -ErrorAction SilentlyContinue){ throw 'Revit is already running; it will not be interrupted.' }
if(-not(Test-Path -LiteralPath $SourceModel)){throw 'Source model not found.'}
$webRun=Join-Path $webRoot ('.local\native-'+(Get-Date -Format yyyyMMdd-HHmmss))
New-Item -ItemType Directory -Path (Join-Path $webRun 'models') -Force | Out-Null
$copy=Join-Path $webRun ('models\'+[IO.Path]::GetFileName($SourceModel))
Copy-Item -LiteralPath $SourceModel -Destination $copy
$sourceHash=(Get-FileHash -LiteralPath $SourceModel -Algorithm SHA256).Hash
if((Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -ne $sourceHash){throw 'Model copy hash mismatch.'}
@{model=$copy;source=$SourceModel;sourceSha256=$sourceHash}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $webRun 'config.json') -Encoding utf8
. (Join-Path $PSScriptRoot 'Prepare-WebAcceptance.ps1')
$acceptance=Initialize-HifcWebAcceptance -Run $webRun
$previousRun=$env:HIFC_WEB_NATIVE_RUN
try { $env:HIFC_WEB_NATIVE_RUN=$webRun; $hostProcess=Start-Process -FilePath 'C:\Program Files\Autodesk\Revit 2020\Revit.exe' -WindowStyle $WindowStyle -PassThru }
finally { $env:HIFC_WEB_NATIVE_RUN=$previousRun }
@{pid=$hostProcess.Id;run=$webRun;manifest=$manifest;source=$SourceModel;sourceSha256=$sourceHash}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $webRun 'launch.json') -Encoding utf8
Write-Output ($webRun)
