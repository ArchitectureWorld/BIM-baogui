[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$PackageDirectory,
    [Parameter(Mandatory=$true)][string]$SourceModel,
    [Parameter(Mandatory=$true)][string]$RunDirectory
)
$ErrorActionPreference='Stop'
if(Get-Process Revit -ErrorAction SilentlyContinue){throw 'Close Revit normally before isolated export testing.'}
$workspace=Split-Path -Parent $PSScriptRoot
$package=(Resolve-Path -LiteralPath $PackageDirectory).Path
$source=(Resolve-Path -LiteralPath $SourceModel).Path
$run=[IO.Path]::GetFullPath($RunDirectory)
if(Test-Path -LiteralPath $run){throw 'Choose a fresh run directory.'}
$addinRoot=Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Autodesk\Revit\Addins\2020'
$formal=Join-Path $addinRoot 'Hifc.V1.addin'
$disabled=$formal+'.export-delivery-test.disabled'
$temporary=Join-Path $addinRoot 'Hifc.V1.ExportDeliveryAcceptance.addin'
if((Test-Path -LiteralPath $disabled)-or(Test-Path -LiteralPath $temporary)){throw 'Prior test manifest exists; inspect and restore it first.'}
New-Item -ItemType Directory -Path (Join-Path $run 'models') -Force | Out-Null
$copy=Join-Path $run 'models\3D_验收项目_综合楼.rvt'
Copy-Item -LiteralPath $source -Destination $copy
$hostAssembly=Join-Path $run 'Hifc.Native.Tests.dll'
Copy-Item -LiteralPath (Join-Path $workspace 'tests\Hifc.Native.Tests\bin\Release\net48\Hifc.Native.Tests.dll') -Destination $hostAssembly
# Keep the test host's Reference dependency identical to the frozen candidate.
Copy-Item -LiteralPath (Join-Path $package 'Hifc.Reference.Core.dll') -Destination (Join-Path $run 'Hifc.Reference.Core.dll')
@{package=$package;models=@(@{key='building';source=$source;copy=$copy;source_sha256=(Get-FileHash -LiteralPath $source).Hash.ToLowerInvariant();kind='单体建筑';project_name='验收项目';building_name='综合楼';export=$true})} |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $run 'input.json') -Encoding utf8
$escape={param($s)[Security.SecurityElement]::Escape($s)}
$pluginPath=& $escape (Join-Path $package 'Hifc.V1.Revit.dll')
$hostPath=& $escape $hostAssembly
$xml=@"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
<AddIn Type="Application"><Name>H-IFC V1 Candidate</Name><Assembly>$pluginPath</Assembly><AddInId>e3075f38-779a-4b07-9423-dd7c737b7139</AddInId><FullClassName>Hifc.Revit.App</FullClassName><VendorId>HIV1</VendorId><VendorDescription>Isolated export candidate</VendorDescription></AddIn>
<AddIn Type="Application"><Name>H-IFC Export Delivery Acceptance</Name><Assembly>$hostPath</Assembly><AddInId>303b4064-7249-469a-b971-a9c6e3d0d007</AddInId><FullClassName>ExportNamingAcceptanceHost</FullClassName><VendorId>HIV1</VendorId><VendorDescription>Disposable model-copy export tests</VendorDescription></AddIn>
</RevitAddIns>
"@
$formalHash=(Get-FileHash -LiteralPath $formal).Hash
$launch=@{run=$run;formal_manifest=$formal;disabled_manifest=$disabled;temporary_manifest=$temporary;formal_manifest_sha256=$formalHash;package=$package;scope='Generated fixture copy only; candidate not installed; no platform writes.'}
$priorRun=$env:HIFC_V1_EXPORT_NAMING_TEST_RUN;$priorWorkspace=$env:HIFC_V1_WORKSPACE
try {
    Move-Item -LiteralPath $formal -Destination $disabled
    [IO.File]::WriteAllText($temporary,$xml,[Text.UTF8Encoding]::new($false))
    $env:HIFC_V1_EXPORT_NAMING_TEST_RUN=$run;$env:HIFC_V1_WORKSPACE=$workspace
    $process=Start-Process -FilePath 'C:\Program Files\Autodesk\Revit 2020\Revit.exe' -WindowStyle Hidden -PassThru
    $launch.pid=$process.Id
}catch {
    if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary}
    if(Test-Path -LiteralPath $disabled){Move-Item -LiteralPath $disabled -Destination $formal}
    throw
}finally{$env:HIFC_V1_EXPORT_NAMING_TEST_RUN=$priorRun;$env:HIFC_V1_WORKSPACE=$priorWorkspace}
$launch | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $run 'launch.json') -Encoding utf8
$launch | ConvertTo-Json -Depth 5
# Revit exits normally on success. After verifying exit, restore the exact
# formal manifest and remove only this run's temporary manifest; never kill Revit.
