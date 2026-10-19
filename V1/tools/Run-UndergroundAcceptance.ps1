[CmdletBinding()]
param([string]$Template='C:\ProgramData\Autodesk\RVT 2020\Templates\China\DefaultCHSCHS.rte')
$ErrorActionPreference='Stop'
if(Get-Process -Name Revit -ErrorAction SilentlyContinue){throw 'Close Revit normally before starting this isolated acceptance host.'}
$templatePath=(Resolve-Path -LiteralPath $Template).Path
$workspace=Split-Path -Parent $PSScriptRoot
$run=Join-Path $workspace ('validation\underground-native-'+(Get-Date -Format yyyyMMdd-HHmmss)+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path (Join-Path $run 'models') -Force | Out-Null
$hostDirectory=Join-Path $run 'host'
New-Item -ItemType Directory -Path $hostDirectory -Force | Out-Null
$hostAssembly=Join-Path $hostDirectory 'Hifc.Native.Tests.dll'
Copy-Item -LiteralPath (Join-Path $workspace 'tests\Hifc.Native.Tests\bin\Release\net48\Hifc.Native.Tests.dll') -Destination $hostAssembly
$addin=Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Autodesk\Revit\Addins\2020\Hifc.V1.UndergroundAcceptance.addin'
if(Test-Path -LiteralPath $addin){throw 'An underground acceptance manifest already exists; inspect it before reuse.'}
$escaped=[System.Security.SecurityElement]::Escape($hostAssembly)
$xml=@"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns><AddIn Type="Application"><Name>H-IFC V1 Underground Acceptance</Name><Assembly>$escaped</Assembly><AddInId>e05158ec-cb70-492e-b245-e7ef73d60b4d</AddInId><FullClassName>UndergroundAcceptanceHost</FullClassName><VendorId>HIV1</VendorId><VendorDescription>Temporary controlled underground acceptance</VendorDescription></AddIn></RevitAddIns>
"@
[IO.File]::WriteAllText($addin,$xml,[Text.UTF8Encoding]::new($false))
$priorRun=$env:HIFC_V1_UNDERGROUND_TEST_RUN;$priorTemplate=$env:HIFC_V1_UNDERGROUND_TEMPLATE;$priorWorkspace=$env:HIFC_V1_WORKSPACE
try{
    $env:HIFC_V1_UNDERGROUND_TEST_RUN=$run;$env:HIFC_V1_UNDERGROUND_TEMPLATE=$templatePath;$env:HIFC_V1_WORKSPACE=$workspace
    $process=Start-Process -FilePath 'C:\Program Files\Autodesk\Revit 2020\Revit.exe' -WindowStyle Hidden -PassThru
}finally{$env:HIFC_V1_UNDERGROUND_TEST_RUN=$priorRun;$env:HIFC_V1_UNDERGROUND_TEMPLATE=$priorTemplate;$env:HIFC_V1_WORKSPACE=$priorWorkspace}
$result=@{pid=$process.Id;run=$run;template_path=$templatePath;template_sha256=(Get-FileHash -LiteralPath $templatePath -Algorithm SHA256).Hash.ToLowerInvariant();temporary_addin=$addin;host_sha256=(Get-FileHash -LiteralPath $hostAssembly -Algorithm SHA256).Hash.ToLowerInvariant();result_path=(Join-Path $run 'underground-host-result.json')}
$result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $run 'launch.json') -Encoding utf8
$result | ConvertTo-Json -Depth 4 -Compress
