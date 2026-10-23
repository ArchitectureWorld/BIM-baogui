[CmdletBinding()]
param(
    [string]$Template='C:\ProgramData\Autodesk\RVT 2020\Templates\China\DefaultCHSCHS.rte',
    [string]$PluginDirectory,
    [string]$RestoreRun
)
$ErrorActionPreference='Stop'
$addinRoot=Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Autodesk\Revit\Addins\2020'
$formal=Join-Path $addinRoot 'Hifc.V1.addin'
$disabled=Join-Path $addinRoot 'Hifc.V1.addin.property-data-test.disabled'
$temporary=Join-Path $addinRoot 'Hifc.V1.PropertyDataAcceptance.addin'
if($RestoreRun) {
    $record=Get-Content -LiteralPath (Join-Path $RestoreRun 'launch.json') -Raw | ConvertFrom-Json
    if($record.formal_manifest -ne $formal -or $record.disabled_manifest -ne $disabled -or $record.temporary_manifest -ne $temporary){throw 'Unexpected manifest paths.'}
    if(Test-Path -LiteralPath $disabled) {
        if((Test-Path -LiteralPath $formal) -or (Get-FileHash -LiteralPath $disabled -Algorithm SHA256).Hash -ne $record.formal_manifest_sha256){throw 'Formal manifest changed; restore requires inspection.'}
        Move-Item -LiteralPath $disabled -Destination $formal
    }
    if(Test-Path -LiteralPath $temporary) {
        if((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ne $record.temporary_manifest_sha256){throw 'Temporary manifest changed; refusing removal.'}
        Remove-Item -LiteralPath $temporary
    }
    if((Get-FileHash -LiteralPath $formal -Algorithm SHA256).Hash -ne $record.formal_manifest_sha256){throw 'Formal manifest restore hash differs.'}
    Write-Output 'Formal manifest restored; installed DLLs were not changed.'
    return
}
if(Get-Process Revit -ErrorAction SilentlyContinue){throw 'Close Revit normally before the isolated candidate run.'}
if((Test-Path -LiteralPath $disabled) -or (Test-Path -LiteralPath $temporary)){throw 'A previous temporary manifest remains; inspect/restore that run first.'}
$workspace=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$props=[xml](Get-Content -LiteralPath (Join-Path $workspace 'Directory.Build.props') -Raw)
if(!$PluginDirectory){$PluginDirectory=Join-Path $workspace ('dist\Hifc.V1.Revit2020-'+$props.Project.PropertyGroup.Version)}
$package=(Resolve-Path -LiteralPath $PluginDirectory).Path
$hostAssembly=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot 'bin\Release\net48\Hifc.Native.Tests.dll')).Path
$plugin=Join-Path $package 'Hifc.V1.Revit.dll'
if([Reflection.AssemblyName]::GetAssemblyName($plugin).Version -ne [Reflection.AssemblyName]::GetAssemblyName($hostAssembly).Version){throw 'Candidate and host versions differ.'}
$run=Join-Path $workspace ('validation\property-data-native-'+(Get-Date -Format yyyyMMdd-HHmmss))
New-Item -ItemType Directory -Path (Join-Path $run 'models'),(Join-Path $run 'host') -Force | Out-Null
$hostCopy=Join-Path $run 'host\Hifc.Native.Tests.dll';Copy-Item -LiteralPath $hostAssembly -Destination $hostCopy
@{package=$package;template=(Resolve-Path -LiteralPath $Template).Path}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $run 'input.json') -Encoding utf8
$xml=@"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
 <AddIn Type="Application"><Name>H-IFC V1 Candidate</Name><Assembly>$([Security.SecurityElement]::Escape($plugin))</Assembly><AddInId>53cc4404-b88c-44d3-9035-12f0a0d13869</AddInId><FullClassName>Hifc.Revit.App</FullClassName><VendorId>HIV1</VendorId></AddIn>
 <AddIn Type="Application"><Name>H-IFC Property Data Acceptance</Name><Assembly>$([Security.SecurityElement]::Escape($hostCopy))</Assembly><AddInId>f4bdb7fa-b665-4ddd-9d7f-837d1c4adf52</AddInId><FullClassName>PropertyDataAcceptanceHost</FullClassName><VendorId>HIV1</VendorId></AddIn>
</RevitAddIns>
"@
$record=[ordered]@{run=$run;formal_manifest=$formal;disabled_manifest=$disabled;temporary_manifest=$temporary;formal_manifest_sha256=(Get-FileHash -LiteralPath $formal -Algorithm SHA256).Hash;plugin_sha256=(Get-FileHash -LiteralPath $plugin -Algorithm SHA256).Hash;core_sha256=(Get-FileHash -LiteralPath (Join-Path $package 'Hifc.Reference.Core.dll') -Algorithm SHA256).Hash;host_sha256=(Get-FileHash -LiteralPath $hostCopy -Algorithm SHA256).Hash;scope='Generated fixture only; no customer model/platform writes; candidate not installed.'}
# Individual manifest move, with both absolute targets checked inside the named add-in folder.
foreach($target in @($formal,$disabled,$temporary)){if(![IO.Path]::GetFullPath($target).StartsWith([IO.Path]::GetFullPath($addinRoot)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Manifest path escaped add-in folder.'}}
Move-Item -LiteralPath $formal -Destination $disabled
try {
    [IO.File]::WriteAllText($temporary,$xml,[Text.UTF8Encoding]::new($false))
    $record.temporary_manifest_sha256=(Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash
    $record|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $run 'launch.json') -Encoding utf8
    $previousRun=$env:HIFC_PROPERTY_DATA_RUN;$previousWorkspace=$env:HIFC_V1_WORKSPACE
    try{$env:HIFC_PROPERTY_DATA_RUN=$run;$env:HIFC_V1_WORKSPACE=$run;$process=Start-Process 'C:\Program Files\Autodesk\Revit 2020\Revit.exe' -WindowStyle Normal -PassThru}
    finally{$env:HIFC_PROPERTY_DATA_RUN=$previousRun;$env:HIFC_V1_WORKSPACE=$previousWorkspace}
    $record.pid=$process.Id;$record|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $run 'launch.json') -Encoding utf8
    $record|ConvertTo-Json -Depth 5
}catch {
    if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary}
    if(!(Test-Path -LiteralPath $formal)){Move-Item -LiteralPath $disabled -Destination $formal}
    throw
}
