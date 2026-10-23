[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$SourceModel)
$ErrorActionPreference='Stop'
if(Get-Process -Name Revit -ErrorAction SilentlyContinue){throw 'Close Revit normally before starting this isolated acceptance host.'}
$source=(Resolve-Path -LiteralPath $SourceModel).Path
$sourceHash=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
$workspace=Split-Path -Parent $PSScriptRoot
$run=Join-Path $workspace ('validation\site-classification-native-'+(Get-Date -Format yyyyMMdd-HHmmss)+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path (Join-Path $run 'models') -Force | Out-Null
$copy=Join-Path $run 'models\site-copy.rvt'
Copy-Item -LiteralPath $source -Destination $copy
if((Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash.ToLowerInvariant() -ne $sourceHash -or (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() -ne $sourceHash){throw 'Source model changed while preparing the copy.'}
$hostDirectory=Join-Path $run 'host'
New-Item -ItemType Directory -Path $hostDirectory -Force | Out-Null
$hostAssembly=Join-Path $hostDirectory 'Hifc.Native.Tests.dll'
Copy-Item -LiteralPath (Join-Path $workspace 'tests\Hifc.Native.Tests\bin\Release\net48\Hifc.Native.Tests.dll') -Destination $hostAssembly
$addin=Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Autodesk\Revit\Addins\2020\Hifc.V1.SiteAcceptance.addin'
if(Test-Path -LiteralPath $addin){throw 'A site-classification acceptance manifest already exists; inspect it before reuse.'}
$escaped=[System.Security.SecurityElement]::Escape($hostAssembly)
$xml=@"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns><AddIn Type="Application"><Name>H-IFC V1 Acceptance</Name><Assembly>$escaped</Assembly><AddInId>216aa37b-a0f2-4cc8-95cc-45e790fcf404</AddInId><FullClassName>SiteClassificationAcceptanceHost</FullClassName><VendorId>HIV1</VendorId><VendorDescription>Temporary model-copy site classification acceptance</VendorDescription></AddIn></RevitAddIns>
"@
[IO.File]::WriteAllText($addin,$xml,[Text.UTF8Encoding]::new($false))
$result=@{run=$run;source_path=$source;source_sha256=$sourceHash;model=$copy;temporary_addin=$addin;host_sha256=(Get-FileHash -LiteralPath $hostAssembly -Algorithm SHA256).Hash.ToLowerInvariant();result_path=(Join-Path $run 'site-host-result.json')}
$result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $run 'launch.json') -Encoding utf8
$previousRun=$env:HIFC_V1_SITE_TEST_RUN;$previousWorkspace=$env:HIFC_V1_WORKSPACE
try{
    $env:HIFC_V1_SITE_TEST_RUN=$run;$env:HIFC_V1_WORKSPACE=$workspace
    $process=Start-Process -FilePath 'C:\Program Files\Autodesk\Revit 2020\Revit.exe' -WindowStyle Hidden -PassThru
}finally{$env:HIFC_V1_SITE_TEST_RUN=$previousRun;$env:HIFC_V1_WORKSPACE=$previousWorkspace}
$result.pid=$process.Id
$result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $run 'launch.json') -Encoding utf8
$result | ConvertTo-Json -Depth 4 -Compress
