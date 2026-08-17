[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string]$SeedManifestPath,
  [Parameter(Mandatory = $true)][string]$ProbeIfcPath,
  [Parameter(Mandatory = $true)][string]$RevitJournalPath,
  [Parameter(Mandatory = $true)][string]$HifcToolManifestPath,
  [Parameter(Mandatory = $true)][string]$HifcToolDllPath,
  [Parameter(Mandatory = $true)][string]$HifcToolDllSha256,
  [Parameter(Mandatory = $true)][string]$HifcCoreDllPath,
  [Parameter(Mandatory = $true)][string]$HifcCoreDllSha256,
  [Parameter(Mandatory = $true)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-Sha256 {
  param([Parameter(Mandatory = $true)][string]$Path)
  $stream = [System.IO.File]::OpenRead([System.IO.Path]::GetFullPath($Path))
  try {
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
      return ([System.BitConverter]::ToString(
        $algorithm.ComputeHash($stream))).Replace('-', '').ToLowerInvariant()
    } finally { $algorithm.Dispose() }
  } finally { $stream.Dispose() }
}

function Assert-LockedFile {
  param(
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true)][string]$ExpectedSha256,
    [Parameter(Mandatory = $true)][string]$Code
  )
  if (-not (Test-Path -LiteralPath $Path -PathType Leaf) -or
      $ExpectedSha256 -notmatch '^[0-9a-fA-F]{64}$' -or
      (Get-Sha256 -Path $Path) -ne $ExpectedSha256.ToLowerInvariant()) {
    throw $Code
  }
}

foreach ($path in @(
  $SeedManifestPath,
  $ProbeIfcPath,
  $RevitJournalPath,
  $HifcToolManifestPath)) {
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
    throw "PROBE_INPUT_NOT_FOUND: $path"
  }
}
Assert-LockedFile -Path $HifcToolDllPath -ExpectedSha256 $HifcToolDllSha256 -Code 'PROBE_HIFCTOOL_DLL_SHA_MISMATCH'
Assert-LockedFile -Path $HifcCoreDllPath -ExpectedSha256 $HifcCoreDllSha256 -Code 'PROBE_HIFCCORE_DLL_SHA_MISMATCH'

$seedRaw = Get-Content -LiteralPath $SeedManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$assembly = [System.Reflection.Assembly]::LoadFrom(
  [System.IO.Path]::GetFullPath($HifcCoreDllPath))
$manifestType = $assembly.GetType(
  'BIMBaoGui.HifcCore.OfficialCarrierProbeSeedManifest', $true)
$itemType = $assembly.GetType(
  'BIMBaoGui.HifcCore.OfficialCarrierProbeSeedItem', $true)
$inspectorType = $assembly.GetType(
  'BIMBaoGui.HifcCore.OfficialCarrierProbeInspector', $true)
$manifest = [System.Activator]::CreateInstance($manifestType)
$manifest.SchemaVersion = [string]$seedRaw.schemaVersion
$manifest.ContextSha256 = [string]$seedRaw.contextSha256
$manifest.ProbeRvtSha256 = [string]$seedRaw.probeRvtSha256
$listType = [System.Collections.Generic.List``1].MakeGenericType($itemType)
$items = [System.Activator]::CreateInstance($listType)
foreach ($raw in @($seedRaw.items)) {
  $item = [System.Activator]::CreateInstance($itemType)
  $item.PropertyId = [string]$raw.propertyId
  $item.IfcEntity = [string]$raw.ifcEntity
  $item.IfcPropertySet = [string]$raw.ifcPropertySet
  $item.IfcProperty = [string]$raw.ifcProperty
  $item.ExactSourceName = [string]$raw.exactSourceName
  $item.DeclaredIfcType = [string]$raw.declaredIfcType
  $item.CanonicalUnit = [string]$raw.canonicalUnit
  $item.CandidateUniqueId = [string]$raw.candidateUniqueId
  $item.CandidateCategoryBuiltInId = [string]$raw.candidateCategoryBuiltInId
  $item.CandidateElementClass = [string]$raw.candidateElementClass
  $item.ParameterGuid = [string]$raw.parameterGuid
  $item.Sentinel = [string]$raw.sentinel
  $item.Readback = [string]$raw.readback
  $items.Add($item)
}
$manifest.Items = $items
$method = $inspectorType.GetMethod('InspectFile', @([string], $manifestType))
if ($null -eq $method) { throw 'PROBE_INSPECTOR_API_MISSING' }
$inspection = $method.Invoke($null, @(
  [System.IO.Path]::GetFullPath($ProbeIfcPath),
  $manifest))
if (-not $inspection.Success) {
  throw ([string]$inspection.ErrorCode)
}

$probeRvtPath = [string]$seedRaw.probeRvtPath
if (-not (Test-Path -LiteralPath $probeRvtPath -PathType Leaf) -or
    (Get-Sha256 -Path $probeRvtPath) -ne [string]$seedRaw.probeRvtSha256) {
  throw 'PROBE_RVT_SHA_MISMATCH'
}
$toolManifest = Get-Content -LiteralPath $HifcToolManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$toolVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo(
  [System.IO.Path]::GetFullPath($HifcToolDllPath)).FileVersion
$payload = [ordered]@{
  schemaVersion = 'HBR_OFFICIAL_CARRIER_PROBE_RESULT_V1'
  success = $true
  seedManifestPath = [System.IO.Path]::GetFullPath($SeedManifestPath)
  seedManifestSha256 = Get-Sha256 -Path $SeedManifestPath
  contextSha256 = [string]$seedRaw.contextSha256
  probeRvtPath = [System.IO.Path]::GetFullPath($probeRvtPath)
  probeRvtSha256 = [string]$seedRaw.probeRvtSha256
  probeIfcPath = [System.IO.Path]::GetFullPath($ProbeIfcPath)
  probeIfcSha256 = Get-Sha256 -Path $ProbeIfcPath
  revitJournalPath = [System.IO.Path]::GetFullPath($RevitJournalPath)
  revitJournalSha256 = Get-Sha256 -Path $RevitJournalPath
  hifcTool = [ordered]@{
    manifestPath = [System.IO.Path]::GetFullPath($HifcToolManifestPath)
    manifestSha256 = Get-Sha256 -Path $HifcToolManifestPath
    dllPath = [System.IO.Path]::GetFullPath($HifcToolDllPath)
    dllSha256 = $HifcToolDllSha256.ToLowerInvariant()
    fileVersion = $toolVersion
    manifest = $toolManifest
  }
  hifcCore = [ordered]@{
    dllPath = [System.IO.Path]::GetFullPath($HifcCoreDllPath)
    dllSha256 = $HifcCoreDllSha256.ToLowerInvariant()
  }
  items = @($inspection.Items)
}
$outputFullPath = [System.IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $outputFullPath) {
  throw 'PROBE_RESULT_ALREADY_EXISTS'
}
[System.IO.Directory]::CreateDirectory(
  [System.IO.Path]::GetDirectoryName($outputFullPath)) | Out-Null
$json = $payload | ConvertTo-Json -Depth 32
$encoding = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($outputFullPath, $json, $encoding)
$payload | ConvertTo-Json -Depth 32 -Compress
