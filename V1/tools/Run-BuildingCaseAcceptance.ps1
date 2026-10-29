[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$SourceDirectory,[ValidateSet('baseline','patched')][string]$Mode='baseline',[string]$Expectations)
$ErrorActionPreference='Stop'
if(Get-Process -Name Revit -ErrorAction SilentlyContinue){throw 'Close Revit normally before this isolated run.'}
$workspace=Split-Path -Parent $PSScriptRoot
$sourceRoot=(Resolve-Path -LiteralPath $SourceDirectory).Path
$run=Join-Path $workspace ('validation\building-case02-'+$Mode+'-'+(Get-Date -Format yyyyMMdd-HHmmss))
New-Item -ItemType Directory -Path (Join-Path $run 'models') -Force | Out-Null
$inputs=@(Get-ChildItem -LiteralPath $sourceRoot -File -Filter '*.rvt' | ForEach-Object {
    $source=$_.FullName
    $hash=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
    $copy=Join-Path $run ('models\'+$_.Name)
    Copy-Item -LiteralPath $source -Destination $copy
    if((Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash){throw 'Copy hash mismatch'}
    @{name=$_.BaseName;source=$source;model=$copy;sha256=$hash}
})
if($inputs.Count -ne 2){throw 'Expected exactly two case 02 source models'}
if($Mode -eq 'patched'){
    if(-not $Expectations -or -not (Test-Path -LiteralPath $Expectations)){throw 'Patched run requires independent case expectations'}
    Copy-Item -LiteralPath $Expectations -Destination (Join-Path $run 'expectations.json')
}
$hostDirectory=Join-Path $run 'host'
New-Item -ItemType Directory -Path $hostDirectory -Force | Out-Null
$assembly=Join-Path $hostDirectory 'Hifc.Native.Tests.dll'
Copy-Item -LiteralPath (Join-Path $workspace 'tests\Hifc.Native.Tests\bin\Release\net48\Hifc.Native.Tests.dll') -Destination $assembly
$addin=Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Autodesk\Revit\Addins\2020\Hifc.V1.BuildingAcceptance.addin'
if(Test-Path -LiteralPath $addin){throw 'Temporary building acceptance manifest already exists; inspect first'}
$escaped=[Security.SecurityElement]::Escape($assembly)
$xml=@"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns><AddIn Type="Application"><Name>H-IFC V1 Acceptance</Name><Assembly>$escaped</Assembly><AddInId>216aa37b-a0f2-4cc8-95cc-45e790fcf404</AddInId><FullClassName>BuildingCaseAcceptanceHost</FullClassName><VendorId>HIV1</VendorId><VendorDescription>Temporary case 02 model-copy acceptance</VendorDescription></AddIn></RevitAddIns>
"@
[IO.File]::WriteAllText($addin,$xml,[Text.UTF8Encoding]::new($false))
$result=@{run=$run;inputs=$inputs;mode=$Mode;temporary_addin=$addin}
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $run 'launch.json') -Encoding utf8
$prior=$env:HIFC_V1_BUILDING_TEST_RUN;$priorWorkspace=$env:HIFC_V1_WORKSPACE
try{
    $env:HIFC_V1_BUILDING_TEST_RUN=$run;$env:HIFC_V1_WORKSPACE=$workspace
    $process=Start-Process -FilePath 'C:\Program Files\Autodesk\Revit 2020\Revit.exe' -WindowStyle Hidden -PassThru
}finally{$env:HIFC_V1_BUILDING_TEST_RUN=$prior;$env:HIFC_V1_WORKSPACE=$priorWorkspace}
$result.pid=$process.Id
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $run 'launch.json') -Encoding utf8
$result | ConvertTo-Json -Depth 5 -Compress
