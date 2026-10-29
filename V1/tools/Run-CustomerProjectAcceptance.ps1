[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$SourceDirectory,
    [string[]]$ModelNames=@(),
    [string[]]$ManualBuildingModels=@(),
    [switch]$InventoryOnly,
    [switch]$ResumeOnInput,
    [switch]$PlanningValidation,
    [string]$RuleProfileId='',
    [string]$WorkbookInputManifest=''
)
$ErrorActionPreference='Stop'
if(Get-Process -Name Revit -ErrorAction SilentlyContinue){throw 'Close Revit normally before isolated model-copy testing.'}
$workspace=Split-Path -Parent $PSScriptRoot
$sourceRoot=(Resolve-Path -LiteralPath $SourceDirectory).Path
$sources=@(Get-ChildItem -LiteralPath $sourceRoot -File -Filter '*.rvt')
if($ModelNames.Count -gt 0){
    if(@($ModelNames | Select-Object -Unique).Count -ne $ModelNames.Count){throw 'Model names must be unique.'}
    foreach($name in $ModelNames){if($sources.BaseName -notcontains $name){throw "Requested model was not found: $name"}}
    $sources=@($sources | Where-Object {$ModelNames -contains $_.BaseName})
}elseif($sources.Count -ne 2){throw 'Specify ModelNames when the directory does not contain exactly the original two RVT files.'}
if($ResumeOnInput -and -not $InventoryOnly){throw 'ResumeOnInput is only for a read-only inventory pass.'}
foreach($name in $ManualBuildingModels){
    if($sources.BaseName -notcontains $name){throw "Manual building model was not found: $name"}
}
$addin=Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Autodesk\Revit\Addins\2020\Hifc.V1.CustomerAcceptance.addin'
if(Test-Path -LiteralPath $addin){throw 'Customer acceptance manifest already exists; inspect it first.'}
$run=Join-Path $workspace ('validation\customer-projects-'+(Get-Date -Format yyyyMMdd-HHmmss))
New-Item -ItemType Directory -Path (Join-Path $run 'models') -Force | Out-Null
$inputs=@($sources | ForEach-Object {
    $source=$_.FullName
    $hash=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
    $copy=Join-Path $run ('models\'+$_.Name)
    Copy-Item -LiteralPath $source -Destination $copy
    if((Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash){throw 'Model copy hash mismatch'}
    @{name=$_.BaseName;source=$source;model=$copy;sha256=$hash;
      requested_kind=$(if($ManualBuildingModels -contains $_.BaseName){'单体建筑'}else{'自动识别'})}
})
$hostDirectory=Join-Path $run 'host'
New-Item -ItemType Directory -Path $hostDirectory -Force | Out-Null
$assembly=Join-Path $hostDirectory 'Hifc.Native.Tests.dll'
Copy-Item -LiteralPath (Join-Path $workspace 'tests\Hifc.Native.Tests\bin\Release\net48\Hifc.Native.Tests.dll') -Destination $assembly
$escaped=[Security.SecurityElement]::Escape($assembly)
$xml=@"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns><AddIn Type="Application"><Name>H-IFC V1 Acceptance</Name><Assembly>$escaped</Assembly><AddInId>216aa37b-a0f2-4cc8-95cc-45e790fcf404</AddInId><FullClassName>CustomerProjectAcceptanceHost</FullClassName><VendorId>HIV1</VendorId><VendorDescription>Temporary current customer model-copy tests</VendorDescription></AddIn></RevitAddIns>
"@
[IO.File]::WriteAllText($addin,$xml,[Text.UTF8Encoding]::new($false))
$result=@{run=$run;inputs=$inputs;temporary_addin=$addin;phase='报规';planning_validation=[bool]$PlanningValidation;RuleProfileId=$RuleProfileId;host_sha256=(Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash.ToLowerInvariant()}
$result.inventory_only=[bool]$InventoryOnly
$result.resume_on_input=[bool]$ResumeOnInput
if($WorkbookInputManifest){
    $manifest=(Resolve-Path -LiteralPath $WorkbookInputManifest).Path
    $manifestCopy=Join-Path $run 'workbook-inputs.json'
    Copy-Item -LiteralPath $manifest -Destination $manifestCopy
    $result.workbook_input_manifest=$manifestCopy
    $result.workbook_input_sha256=(Get-FileHash -LiteralPath $manifestCopy -Algorithm SHA256).Hash.ToLowerInvariant()
}
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $run 'launch.json') -Encoding utf8
$prior=$env:HIFC_V1_CUSTOMER_TEST_RUN;$priorWorkspace=$env:HIFC_V1_WORKSPACE
try{
    $env:HIFC_V1_CUSTOMER_TEST_RUN=$run;$env:HIFC_V1_WORKSPACE=$workspace
    $process=Start-Process -FilePath 'C:\Program Files\Autodesk\Revit 2020\Revit.exe' -WindowStyle Hidden -PassThru
}finally{$env:HIFC_V1_CUSTOMER_TEST_RUN=$prior;$env:HIFC_V1_WORKSPACE=$priorWorkspace}
$result.pid=$process.Id
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $run 'launch.json') -Encoding utf8
$result | ConvertTo-Json -Depth 5 -Compress
