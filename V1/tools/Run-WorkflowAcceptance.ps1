[CmdletBinding()]
param([string]$HarnessName='Hifc.Native.Tests.R9')
$ErrorActionPreference='Stop'
if (Get-Process -Name Revit -ErrorAction SilentlyContinue) { throw 'A fresh host is required; existing Revit is left intact.' }
$workspace=Split-Path -Parent $PSScriptRoot
$run=Join-Path $workspace ('validation\workflow-' + (Get-Date -Format yyyyMMdd-HHmmss))
$copies=Join-Path $run 'models'
New-Item -ItemType Directory -Path $copies -Force | Out-Null
$sources=Join-Path (Split-Path -Parent $workspace) '测试文件\01\revit原文件'
$models=@('总图.0008_干净.rvt','厂房_干净.rvt','职工后勤楼_干净.rvt') | ForEach-Object {
    $source=Join-Path $sources $_; $copy=Join-Path $copies $_
    Copy-Item -LiteralPath $source -Destination $copy
    $hash=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    if ((Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -ne $hash) { throw 'Copy hash mismatch.' }
    @{ source=$source; copy=$copy; sha256=$hash }
}
$models | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $run 'source-models.json') -Encoding utf8
$assembly=Join-Path $workspace "tests\Hifc.Native.Tests\bin\Release\net48\$HarnessName.dll"
if (-not (Test-Path -LiteralPath $assembly)) { throw 'Build test harness first.' }
$manifest=Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Autodesk\Revit\Addins\2020\Hifc.V1.Acceptance.addin'
if (Test-Path -LiteralPath $manifest) { throw 'Previous test manifest still exists; inspect it before removing.' }
$xml=@"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns><AddIn Type="Application"><Name>H-IFC V1 Acceptance</Name><Assembly>$([System.Security.SecurityElement]::Escape($assembly))</Assembly><AddInId>44257616-18ad-481a-b6b8-6b7a1bfe1088</AddInId><FullClassName>HifcAcceptanceHost</FullClassName><VendorId>HIV1</VendorId><VendorDescription>Temporary model copy validation</VendorDescription></AddIn></RevitAddIns>
"@
[IO.File]::WriteAllText($manifest,$xml,[Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath $manifest -Destination (Join-Path $run 'test.addin')
$previous=$env:HIFC_V1_NATIVE_TEST_RUN
try {
    $env:HIFC_V1_NATIVE_TEST_RUN=$run
    $process=Start-Process -FilePath 'C:\Program Files\Autodesk\Revit 2020\Revit.exe' -WindowStyle Hidden -PassThru
} finally { $env:HIFC_V1_NATIVE_TEST_RUN=$previous }
@{ pid=$process.Id; run=$run; temporary_manifest=$manifest; harness=$assembly; scope='Model copies only, never saved. Native mutations roll back; modeless mutations discarded by closing copies.' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'launch.json') -Encoding utf8
Write-Output "Revit PID: $($process.Id)"
Write-Output "Acceptance run: $run"
