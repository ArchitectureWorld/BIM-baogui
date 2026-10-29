[CmdletBinding()]
param(
    [string]$Template='C:\ProgramData\Autodesk\RVT 2020\Templates\China\DefaultCHSCHS.rte',
    [string]$WindowFamily='C:\ProgramData\Autodesk\RVT 2020\Libraries\China\建筑\窗\普通窗\固定窗\固定窗.rfa',
    [string]$HostAssembly,
    [string]$PluginDirectory,
    [switch]$PrepareOnly
)
$ErrorActionPreference='Stop'
if (-not $PrepareOnly -and (Get-Process -Name Revit -ErrorAction SilentlyContinue)) { throw 'Close Revit normally before this isolated fixture run.' }
$workspace=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $HostAssembly) { $HostAssembly=Join-Path $PSScriptRoot 'bin\Release\net48\Hifc.Native.Tests.dll' }
if (-not $PluginDirectory) { $PluginDirectory=Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Autodesk\Revit\Addins\2020\Hifc.V1' }
$hostSource=(Resolve-Path -LiteralPath $HostAssembly).Path
$pluginRoot=(Resolve-Path -LiteralPath $PluginDirectory).Path
$templatePath=(Resolve-Path -LiteralPath $Template).Path
$familyPath=(Resolve-Path -LiteralPath $WindowFamily).Path
$pluginPath=Join-Path $pluginRoot 'Hifc.V1.Revit.dll'
$corePath=Join-Path $pluginRoot 'Hifc.Reference.Core.dll'
if ([Reflection.AssemblyName]::GetAssemblyName($hostSource).Version -ne [Reflection.AssemblyName]::GetAssemblyName($pluginPath).Version) { throw 'Host and plugin versions differ; build the matching fixture before launching.' }
$run=Join-Path $workspace ('validation\platform-0.4.8\native-'+(Get-Date -Format yyyyMMdd-HHmmss)+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path (Join-Path $run 'models'),(Join-Path $run 'host') -Force | Out-Null
$hostCopy=Join-Path $run 'host\Hifc.Native.Tests.dll'
Copy-Item -LiteralPath $hostSource -Destination $hostCopy
$addin=Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Autodesk\Revit\Addins\2020\Hifc.V1.PlatformAcceptance.addin'
$launch=[ordered]@{
    run=$run; template=$templatePath; window_family=$familyPath;
    template_sha256=(Get-FileHash -LiteralPath $templatePath -Algorithm SHA256).Hash.ToLowerInvariant();
    window_family_sha256=(Get-FileHash -LiteralPath $familyPath -Algorithm SHA256).Hash.ToLowerInvariant();
    plugin_path=$pluginPath; plugin_sha256=(Get-FileHash -LiteralPath $pluginPath -Algorithm SHA256).Hash.ToLowerInvariant();
    core_sha256=(Get-FileHash -LiteralPath $corePath -Algorithm SHA256).Hash.ToLowerInvariant();
    host_sha256=(Get-FileHash -LiteralPath $hostCopy -Algorithm SHA256).Hash.ToLowerInvariant();
    temporary_addin=$addin; result_path=(Join-Path $run 'platform-host-result.json');
    scope='Controlled fixture only; requires a separate IFC semantic audit; no customer or platform data changed.';
    prepared_only=[bool]$PrepareOnly
}
$launch | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $run 'launch.json') -Encoding utf8
if ($PrepareOnly) { $launch | ConvertTo-Json -Depth 5; return }
if (Test-Path -LiteralPath $addin) { throw 'Platform acceptance manifest already exists; inspect and retire its previous run before reuse.' }
$escaped=[Security.SecurityElement]::Escape($hostCopy)
$xml=@"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns><AddIn Type="Application"><Name>H-IFC Platform Acceptance</Name><Assembly>$escaped</Assembly><AddInId>86da880e-5a0b-421a-a1ea-27f9a76244b7</AddInId><FullClassName>PlatformAcceptanceHost</FullClassName><VendorId>HIV1</VendorId><VendorDescription>Temporary controlled platform fixture</VendorDescription></AddIn></RevitAddIns>
"@
[IO.File]::WriteAllText($addin,$xml,[Text.UTF8Encoding]::new($false))
$previousRun=$env:HIFC_V1_PLATFORM_TEST_RUN
$previousWorkspace=$env:HIFC_V1_WORKSPACE
try {
    $env:HIFC_V1_PLATFORM_TEST_RUN=$run
    # Keep management state, binding files and write evidence inside the fixture run.
    $env:HIFC_V1_WORKSPACE=$run
    $process=Start-Process -FilePath 'C:\Program Files\Autodesk\Revit 2020\Revit.exe' -WindowStyle Hidden -PassThru
} finally {
    $env:HIFC_V1_PLATFORM_TEST_RUN=$previousRun
    $env:HIFC_V1_WORKSPACE=$previousWorkspace
}
$launch.pid=$process.Id
$launch | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $run 'launch.json') -Encoding utf8
$launch | ConvertTo-Json -Depth 5
