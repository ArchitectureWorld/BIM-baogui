[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [string]$SourceGoldenRvtPath,

  [Parameter(Mandatory = $true)]
  [string]$AcceptanceRoot,

  [Parameter(Mandatory = $true)]
  [string]$CommitSha,

  [Parameter(Mandatory = $true)]
  [string]$RulePackageSha256,

  [Parameter(Mandatory = $true)]
  [string]$CandidatesJsonPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-NormalizedPath {
  param([Parameter(Mandatory = $true)][string]$Path)
  return [System.IO.Path]::GetFullPath($Path).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
}

function Test-IsWithinRoot {
  param(
    [Parameter(Mandatory = $true)][string]$Root,
    [Parameter(Mandatory = $true)][string]$Path
  )
  $prefix = $Root + [System.IO.Path]::DirectorySeparatorChar
  return $Path.StartsWith(
    $prefix,
    [System.StringComparison]::OrdinalIgnoreCase)
}

function Assert-NoReparsePoint {
  param([Parameter(Mandatory = $true)][string]$Path)
  $current = Get-NormalizedPath -Path $Path
  while (-not [string]::IsNullOrWhiteSpace($current)) {
    if (Test-Path -LiteralPath $current) {
      $item = Get-Item -LiteralPath $current -Force
      if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "PROBE_REPARSE_POINT_FORBIDDEN: $current"
      }
    }
    $parent = [System.IO.Path]::GetDirectoryName($current)
    if ([string]::Equals($parent, $current, [System.StringComparison]::OrdinalIgnoreCase)) {
      break
    }
    $current = $parent
  }
}

function Get-Sha256 {
  param([Parameter(Mandatory = $true)][string]$Path)
  $stream = [System.IO.File]::OpenRead($Path)
  try {
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
      return ([System.BitConverter]::ToString(
        $algorithm.ComputeHash($stream))).Replace('-', '').ToLowerInvariant()
    } finally {
      $algorithm.Dispose()
    }
  } finally {
    $stream.Dispose()
  }
}

if ($CommitSha -notmatch '^(?:[0-9a-fA-F]{40}|[0-9a-fA-F]{64})$') {
  throw 'PROBE_COMMIT_IDENTITY_INVALID'
}
if ($RulePackageSha256 -notmatch '^[0-9a-fA-F]{64}$') {
  throw 'PROBE_RULE_IDENTITY_INVALID'
}

$sourcePath = Get-NormalizedPath -Path $SourceGoldenRvtPath
$rootPath = Get-NormalizedPath -Path $AcceptanceRoot
$candidatePath = Get-NormalizedPath -Path $CandidatesJsonPath
if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
  throw 'PROBE_SOURCE_RVT_NOT_FOUND'
}
if (-not (Test-Path -LiteralPath $candidatePath -PathType Leaf)) {
  throw 'PROBE_CANDIDATES_NOT_FOUND'
}
Assert-NoReparsePoint -Path $sourcePath
Assert-NoReparsePoint -Path $candidatePath
Assert-NoReparsePoint -Path $rootPath

$repoRoot = Get-NormalizedPath -Path (Join-Path $PSScriptRoot '..\..')
$baseRulePath = Join-Path $repoRoot 'specs\hbr-rules\v1\source\hbr_rule_source.v1.json'
$overlayRulePath = Join-Path $repoRoot 'specs\hbr-rules\v1\source\hbr_rule_source.v0.4.3-overlay.json'
if (-not (Test-Path -LiteralPath $baseRulePath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $overlayRulePath -PathType Leaf)) {
  throw 'PROBE_FROZEN_RULE_SOURCE_NOT_FOUND'
}

$baseRule = Get-Content -LiteralPath $baseRulePath -Raw -Encoding UTF8 | ConvertFrom-Json
$overlayRule = Get-Content -LiteralPath $overlayRulePath -Raw -Encoding UTF8 | ConvertFrom-Json
$metricRows = @($overlayRule.nativeReporting.stage02BMetrics | Sort-Object sequence)
if ($metricRows.Count -ne 6) {
  throw 'PROBE_METRIC_CONTEXT_INVALID'
}
$baseById = @{}
foreach ($property in @($baseRule.properties)) {
  $baseById[[string]$property.propertyId] = $property
}
$overlayById = @{}
foreach ($property in @($overlayRule.properties)) {
  $overlayById[[string]$property.propertyId] = $property
}

$metrics = @()
foreach ($metric in $metricRows) {
  $propertyId = [string]$metric.propertyId
  $property = if ($overlayById.ContainsKey($propertyId)) {
    $overlayById[$propertyId]
  } elseif ($baseById.ContainsKey($propertyId)) {
    $baseById[$propertyId]
  } else {
    throw "PROBE_METRIC_PROPERTY_NOT_FOUND: $propertyId"
  }
  $baseProperty = if ($baseById.ContainsKey($propertyId)) {
    $baseById[$propertyId]
  } else {
    $property
  }
  $sourceOverride = [string]$baseProperty.officialPlugin.legacyProjection.sourceParameterOverride
  $exactSourceName = if ([string]::IsNullOrWhiteSpace($sourceOverride)) {
    [string]$property.ifc.property
  } else {
    $sourceOverride
  }
  $metrics += [ordered]@{
    propertyId = $propertyId
    sequence = [int]$metric.sequence
    ifcEntity = [string]$property.ifc.entity
    ifcPropertySet = [string]$property.ifc.propertySet
    ifcProperty = [string]$property.ifc.property
    exactOfficialSourceName = $exactSourceName
    declaredIfcType = [string]$property.ifc.declaredType
    canonicalUnit = if ($null -eq $property.ifc.canonicalUnit) { '' } else { [string]$property.ifc.canonicalUnit }
    storageType = [string]$property.revit.storageType
  }
}

$candidateInput = Get-Content -LiteralPath $candidatePath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($candidateInput.Count -eq 0) {
  throw 'PROBE_CANDIDATE_INVALID'
}
$metricIds = @($metrics | ForEach-Object { [string]$_.propertyId })
$candidateKeys = @{}
$candidates = @()
foreach ($candidate in $candidateInput) {
  $propertyId = [string]$candidate.propertyId
  $uniqueId = [string]$candidate.uniqueId
  $categoryBuiltInId = [string]$candidate.categoryBuiltInId
  $elementClass = [string]$candidate.elementClass
  if ([string]::IsNullOrWhiteSpace($propertyId) -or
      [string]::IsNullOrWhiteSpace($uniqueId) -or
      [string]::IsNullOrWhiteSpace($categoryBuiltInId) -or
      [string]::IsNullOrWhiteSpace($elementClass) -or
      $metricIds -notcontains $propertyId) {
    throw 'PROBE_CANDIDATE_INVALID'
  }
  $metricDefinition = @($metrics | Where-Object {
    $_.propertyId -eq $propertyId
  })[0]
  $isProjectInformation = $metricDefinition.ifcEntity -eq 'IfcProject'
  if (($isProjectInformation -and
       ($uniqueId -ne 'PROJECT_INFORMATION' -or
        $categoryBuiltInId -ne 'OST_ProjectInformation' -or
        $elementClass -ne 'Autodesk.Revit.DB.ProjectInfo')) -or
      (-not $isProjectInformation -and $uniqueId -eq 'PROJECT_INFORMATION')) {
    throw 'PROBE_PROJECT_INFORMATION_CANDIDATE_INVALID'
  }
  $key = $propertyId + "`n" + $uniqueId
  if ($candidateKeys.ContainsKey($key)) {
    throw 'PROBE_CANDIDATE_DUPLICATE'
  }
  $candidateKeys[$key] = $true
  $candidates += [ordered]@{
    propertyId = $propertyId
    uniqueId = $uniqueId
    categoryBuiltInId = $categoryBuiltInId
    elementClass = $elementClass
  }
}
foreach ($propertyId in $metricIds) {
  if (@($candidates | Where-Object { $_.propertyId -eq $propertyId }).Count -eq 0) {
    throw 'PROBE_CANDIDATE_SET_MISMATCH'
  }
}
foreach ($metric in $metrics) {
  if ($metric.ifcEntity -eq 'IfcProject' -and
      @($candidates | Where-Object { $_.propertyId -eq $metric.propertyId }).Count -ne 1) {
    throw 'PROBE_PROJECT_INFORMATION_CANDIDATE_INVALID'
  }
}
$candidates = @($candidates | Sort-Object uniqueId, propertyId)

[System.IO.Directory]::CreateDirectory($rootPath) | Out-Null
$runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + [Guid]::NewGuid().ToString('N')
$runRoot = Get-NormalizedPath -Path (Join-Path $rootPath $runId)
if (-not (Test-IsWithinRoot -Root $rootPath -Path $runRoot)) {
  throw 'PROBE_PATH_ESCAPE'
}
[System.IO.Directory]::CreateDirectory($runRoot) | Out-Null
Assert-NoReparsePoint -Path $runRoot

$sourceBaseName = [System.IO.Path]::GetFileNameWithoutExtension($sourcePath)
$probePath = Get-NormalizedPath -Path (Join-Path $runRoot ($sourceBaseName + '__HIFC_CARRIER_PROBE__.rvt'))
$contextPath = Get-NormalizedPath -Path (Join-Path $runRoot 'official-carrier-probe-context.json')
if ([string]::Equals($sourcePath, $probePath, [System.StringComparison]::OrdinalIgnoreCase) -or
    -not (Test-IsWithinRoot -Root $rootPath -Path $probePath) -or
    -not (Test-IsWithinRoot -Root $rootPath -Path $contextPath)) {
  throw 'PROBE_PATH_ESCAPE'
}
if ((Test-Path -LiteralPath $probePath) -or (Test-Path -LiteralPath $contextPath)) {
  throw 'PROBE_TARGET_ALREADY_EXISTS'
}

$sourceShaBefore = Get-Sha256 -Path $sourcePath
Copy-Item -LiteralPath $sourcePath -Destination $probePath
$sourceShaAfter = Get-Sha256 -Path $sourcePath
$probeSha = Get-Sha256 -Path $probePath
if ($sourceShaBefore -ne $sourceShaAfter -or $sourceShaBefore -ne $probeSha) {
  throw 'PROBE_COPY_SHA_MISMATCH'
}

$context = [ordered]@{
  schemaVersion = 'HBR_OFFICIAL_CARRIER_PROBE_V1'
  sourceGoldenRvtPath = $sourcePath
  sourceGoldenRvtSha256 = $sourceShaBefore
  probeCopyPath = $probePath
  probeCopyPreSeedSha256 = $probeSha
  acceptanceRoot = $rootPath
  acceptanceRunId = $runId
  nonce = [Guid]::NewGuid().ToString('N')
  commitSha = $CommitSha.ToLowerInvariant()
  rulePackageSha256 = $RulePackageSha256.ToLowerInvariant()
  metrics = $metrics
  candidates = $candidates
}
$contextJson = $context | ConvertTo-Json -Depth 12
$utf8Bom = New-Object System.Text.UTF8Encoding($true)
[System.IO.File]::WriteAllText($contextPath, $contextJson, $utf8Bom)

[ordered]@{
  contextPath = $contextPath
  probeCopyPath = $probePath
} | ConvertTo-Json -Compress
