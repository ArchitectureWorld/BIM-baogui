[CmdletBinding()]
param([string]$SourceModel='D:\18_建模项目\湖北BIM云平台\测试文件\01\revit原文件\总图.0008_干净.rvt', [ValidateSet('Hidden','Normal')][string]$WindowStyle='Normal')
$ErrorActionPreference='Stop'
$webRoot=Split-Path -Parent $PSScriptRoot
if(Get-Process -Name Revit -ErrorAction SilentlyContinue){ throw 'Revit is already running; it will not be interrupted.' }
$assembly=Join-Path $webRoot 'tests\Hifc.Web.NativeTests\bin\Release\net48\Hifc.Web.NativeTests.dll'
if(-not(Test-Path -LiteralPath $assembly)){throw 'Build the Web native test project first.'}
if(-not(Test-Path -LiteralPath $SourceModel)){throw 'Source model not found.'}
$webRun=Join-Path $webRoot ('.local\native-'+(Get-Date -Format yyyyMMdd-HHmmss))
New-Item -ItemType Directory -Path (Join-Path $webRun 'models') -Force | Out-Null
$copy=Join-Path $webRun ('models\'+[IO.Path]::GetFileName($SourceModel))
Copy-Item -LiteralPath $SourceModel -Destination $copy
$sourceHash=(Get-FileHash -LiteralPath $SourceModel -Algorithm SHA256).Hash
if((Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -ne $sourceHash){throw 'Model copy hash mismatch.'}
@{model=$copy;source=$SourceModel;sourceSha256=$sourceHash}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $webRun 'config.json') -Encoding utf8
$manifest=Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2020\Hifc.Web.NativeTests.addin'
$xml='<?xml version="1.0" encoding="utf-8"?><RevitAddIns><AddIn Type="Application"><Name>HIFC Web Native Tests</Name><Assembly>'+[Security.SecurityElement]::Escape($assembly)+'</Assembly><AddInId>82c20f55-e1f1-451a-918d-075405720287</AddInId><FullClassName>HifcWebNativeHost</FullClassName><VendorId>HIWB</VendorId><VendorDescription>Temporary Web model copy validation</VendorDescription></AddIn></RevitAddIns>'
if(Test-Path -LiteralPath $manifest){
    if([IO.File]::ReadAllText($manifest).Trim() -ne $xml){throw 'Existing test manifest does not match this Web harness; it will not be replaced.'}
} else {[IO.File]::WriteAllText($manifest,$xml,[Text.UTF8Encoding]::new($false))}
$previousRun=$env:HIFC_WEB_NATIVE_RUN
try { $env:HIFC_WEB_NATIVE_RUN=$webRun; $hostProcess=Start-Process -FilePath 'C:\Program Files\Autodesk\Revit 2020\Revit.exe' -WindowStyle $WindowStyle -PassThru }
finally { $env:HIFC_WEB_NATIVE_RUN=$previousRun }
@{pid=$hostProcess.Id;run=$webRun;manifest=$manifest;source=$SourceModel;sourceSha256=$sourceHash}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $webRun 'launch.json') -Encoding utf8
Write-Output ($webRun)
