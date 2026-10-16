[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$SourceRvt)
$ErrorActionPreference='Stop'
if(Get-Process -Name Revit -ErrorAction SilentlyContinue){throw 'Selection acceptance requires a fresh host; running hosts are not stopped.'}
$workspace=Split-Path -Parent $PSScriptRoot
$source=(Resolve-Path -LiteralPath $SourceRvt).Path
$run=Join-Path $workspace ('validation\selection-native-'+(Get-Date -Format yyyyMMdd-HHmmss)+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
$models=Join-Path $run 'models'
New-Item -ItemType Directory -Path $models -Force | Out-Null
$sourceHash=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
foreach($name in @('selection-test.rvt','idle-carrier.rvt')){
    $copy=Join-Path $models $name
    Copy-Item -LiteralPath $source -Destination $copy
    if((Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash.ToLowerInvariant() -ne $sourceHash){throw 'Acceptance copy hash mismatch.'}
}
$hostDirectory=Join-Path $run 'host'
New-Item -ItemType Directory -Path $hostDirectory -Force | Out-Null
$hostAssembly=Join-Path $hostDirectory 'Hifc.Native.Tests.dll'
Copy-Item -LiteralPath (Join-Path $workspace 'tests\Hifc.Native.Tests\bin\Release\net48\Hifc.Native.Tests.dll') -Destination $hostAssembly
$addin=Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Autodesk\Revit\Addins\2020\Hifc.V1.SelectionAcceptance.addin'
if(Test-Path -LiteralPath $addin){throw 'A selection acceptance manifest already exists; inspect it before reusing.'}
$escaped=[System.Security.SecurityElement]::Escape($hostAssembly)
$xml=@"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns><AddIn Type="Application"><Name>H-IFC V1 Selection Acceptance</Name><Assembly>$escaped</Assembly><AddInId>7a46ffdc-118a-4420-a6cb-82d19ccf5e21</AddInId><FullClassName>SelectionAssignmentHost</FullClassName><VendorId>HIV1</VendorId><VendorDescription>Temporary selection assignment acceptance</VendorDescription></AddIn></RevitAddIns>
"@
[System.IO.File]::WriteAllText($addin,$xml,[System.Text.UTF8Encoding]::new($false))
$priorRun=$env:HIFC_V1_SELECTION_TEST_RUN;$priorWorkspace=$env:HIFC_V1_WORKSPACE
try{
    $env:HIFC_V1_SELECTION_TEST_RUN=$run;$env:HIFC_V1_WORKSPACE=$workspace
    $process=Start-Process -FilePath 'C:\Program Files\Autodesk\Revit 2020\Revit.exe' -WindowStyle Hidden -PassThru
}finally{$env:HIFC_V1_SELECTION_TEST_RUN=$priorRun;$env:HIFC_V1_WORKSPACE=$priorWorkspace}
$manifest=@{pid=$process.Id;run=$run;source_path=$source;source_sha256=$sourceHash;temporary_addin=$addin;host_assembly=$hostAssembly;host_sha256=(Get-FileHash -LiteralPath $hostAssembly -Algorithm SHA256).Hash.ToLowerInvariant();result_path=(Join-Path $run 'selection-host-result.json')}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $run 'launch.json') -Encoding UTF8
$manifest | ConvertTo-Json -Depth 4 -Compress
