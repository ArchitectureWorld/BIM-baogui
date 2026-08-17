[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string]$ScanEvidencePath,
  [Parameter(Mandatory = $true)][string]$StrictValidationPath,
  [Parameter(Mandatory = $true)][string]$OfficialExportResultPath,
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

function Get-RequiredValue {
  param(
    [Parameter(Mandatory = $true)][object]$Object,
    [Parameter(Mandatory = $true)][string[]]$Names
  )
  foreach ($name in $Names) {
    $property = $Object.PSObject.Properties[$name]
    if ($null -ne $property -and $null -ne $property.Value) {
      return $property.Value
    }
  }
  throw ('FINAL_REQUIRED_VALUE_MISSING: ' + ($Names -join '|'))
}

function Get-OptionalValue {
  param(
    [Parameter(Mandatory = $true)][object]$Object,
    [Parameter(Mandatory = $true)][string[]]$Names
  )
  foreach ($name in $Names) {
    $property = $Object.PSObject.Properties[$name]
    if ($null -ne $property -and $null -ne $property.Value) {
      return $property.Value
    }
  }
  return $null
}

function New-Identity {
  param(
    [Parameter(Mandatory = $true)][object]$Raw,
    [Parameter(Mandatory = $true)][type]$IdentityType
  )
  $value = [System.Activator]::CreateInstance($IdentityType)
  $value.DocumentFingerprint = [string](Get-RequiredValue $Raw @('documentFingerprint', 'document_fingerprint'))
  $value.RulePackageSha256 = [string](Get-RequiredValue $Raw @('rulePackageSha256', 'rule_package_sha256'))
  $value.Stage01ResultHash = [string](Get-RequiredValue $Raw @('stage01ResultHash', 'stage01_result_hash'))
  $value.Stage02AResultHash = [string](Get-RequiredValue $Raw @('stage02AResultHash', 'stage02a_result_hash'))
  $value.Stage02BResultHash = [string](Get-RequiredValue $Raw @('stage02BResultHash', 'stage02b_result_hash'))
  $value.ManifestSha256 = [string](Get-RequiredValue $Raw @('manifestSha256', 'manifest_sha256'))
  $value.GoldenRvtSha256 = [string](Get-RequiredValue $Raw @('goldenRvtSha256', 'golden_rvt_sha256'))
  $value.OfficialIfcSha256 = [string](Get-RequiredValue $Raw @('officialIfcSha256', 'official_ifc_sha256'))
  return $value
}

function New-ReportIdentity {
  param(
    [Parameter(Mandatory = $true)][object]$Raw,
    [Parameter(Mandatory = $true)][type]$IdentityType,
    [Parameter(Mandatory = $true)][string]$GoldenRvtSha256,
    [Parameter(Mandatory = $true)][string]$OfficialIfcSha256
  )
  $workflow = Get-RequiredValue $Raw @('workflow_results')
  $rulePackage = Get-RequiredValue $Raw @('rule_package')
  $reportManifest = Get-RequiredValue $Raw @('official_acceptance_manifest')
  $stage01 = Get-RequiredValue $workflow @('stage01')
  $stage02A = Get-RequiredValue $workflow @('stage02a', 'stage02A')
  $stage02B = Get-RequiredValue $workflow @('stage02b', 'stage02B')
  $value = [System.Activator]::CreateInstance($IdentityType)
  $value.DocumentFingerprint = [string](Get-RequiredValue $Raw @(
    'document_fingerprint', 'documentFingerprint'))
  $value.RulePackageSha256 = [string](Get-RequiredValue $rulePackage @('sha256'))
  $value.Stage01ResultHash = [string](Get-RequiredValue $stage01 @(
    'result_hash', 'resultHash'))
  $value.Stage02AResultHash = [string](Get-RequiredValue $stage02A @(
    'result_hash', 'resultHash'))
  $value.Stage02BResultHash = [string](Get-RequiredValue $stage02B @(
    'result_hash', 'resultHash'))
  $value.ManifestSha256 = [string](Get-RequiredValue $reportManifest @(
    'sha256', 'manifestSha256', 'manifest_sha256'))
  $value.GoldenRvtSha256 = $GoldenRvtSha256
  $value.OfficialIfcSha256 = $OfficialIfcSha256
  return $value
}

foreach ($path in @(
  $ScanEvidencePath,
  $StrictValidationPath,
  $OfficialExportResultPath,
  $HifcCoreDllPath)) {
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
    throw "FINAL_INPUT_NOT_FOUND: $path"
  }
}
if ($HifcCoreDllSha256 -notmatch '^[0-9a-fA-F]{64}$' -or
    (Get-Sha256 -Path $HifcCoreDllPath) -ne $HifcCoreDllSha256.ToLowerInvariant()) {
  throw 'FINAL_HIFCCORE_DLL_SHA_MISMATCH'
}

$scan = Get-Content -LiteralPath $ScanEvidencePath -Raw -Encoding UTF8 | ConvertFrom-Json
$strict = Get-Content -LiteralPath $StrictValidationPath -Raw -Encoding UTF8 | ConvertFrom-Json
$official = Get-Content -LiteralPath $OfficialExportResultPath -Raw -Encoding UTF8 | ConvertFrom-Json

$goldenRvtPath = Get-OptionalValue $official @('goldenRvtPath', 'golden_rvt_path')
if ($null -eq $goldenRvtPath) {
  $goldenRvtPath = Get-RequiredValue $strict @('goldenRvtPath', 'golden_rvt_path')
}
$officialIfcPath = [string](Get-RequiredValue $official @(
  'officialIfcPath', 'official_ifc_path'))
$goldenRvtPath = [string]$goldenRvtPath
foreach ($path in @($goldenRvtPath, $officialIfcPath)) {
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
    throw "FINAL_ARTIFACT_NOT_FOUND: $path"
  }
}
$goldenRvtSha256 = Get-Sha256 -Path $goldenRvtPath
$officialIfcSha256 = Get-Sha256 -Path $officialIfcPath
$declaredGoldenSha = Get-OptionalValue $official @(
  'goldenRvtSha256', 'golden_rvt_sha256')
$declaredOfficialSha = Get-OptionalValue $official @(
  'officialIfcSha256', 'official_ifc_sha256')
if (($null -ne $declaredGoldenSha -and
    ([string]$declaredGoldenSha).ToLowerInvariant() -cne $goldenRvtSha256) -or
    ($null -ne $declaredOfficialSha -and
    ([string]$declaredOfficialSha).ToLowerInvariant() -cne $officialIfcSha256)) {
  throw 'FINAL_ARTIFACT_SHA_MISMATCH'
}

# These two authoritative collections are accepted only from Golden scan evidence.
$manifestRaw = Get-RequiredValue $scan @('official_acceptance_manifest')
$readbacksRaw = @(Get-RequiredValue $scan @('official_acceptance_revit_readbacks'))
$assembly = [System.Reflection.Assembly]::LoadFrom(
  [System.IO.Path]::GetFullPath($HifcCoreDllPath))
$identityType = $assembly.GetType('BIMBaoGui.HifcCore.OfficialAcceptanceIdentity', $true)
$definitionType = $assembly.GetType('BIMBaoGui.HifcCore.OfficialAcceptancePropertyDefinition', $true)
$manifestType = $assembly.GetType('BIMBaoGui.HifcCore.OfficialAcceptanceManifest', $true)
$readbackType = $assembly.GetType('BIMBaoGui.HifcCore.OfficialAcceptanceRevitReadback', $true)
$requestType = $assembly.GetType('BIMBaoGui.HifcCore.OfficialPropertyReadbackRequest', $true)
$inspectorType = $assembly.GetType('BIMBaoGui.HifcCore.OfficialCarrierProbeInspector', $true)

$manifest = [System.Activator]::CreateInstance($manifestType)
$manifest.SchemaVersion = 'HBR_OFFICIAL_ACCEPTANCE_MANIFEST_V1'
$manifest.ManifestVersion = [string](Get-RequiredValue $manifestRaw @(
  'schema_version', 'manifestVersion', 'manifest_version'))
$definitionListType = [System.Collections.Generic.List``1].MakeGenericType($definitionType)
$definitions = [System.Activator]::CreateInstance($definitionListType)
$definitionByPropertyId = [System.Collections.Generic.Dictionary[string,object]]::new(
  [System.StringComparer]::Ordinal)
foreach ($raw in @(Get-RequiredValue $manifestRaw @('properties', 'definitions'))) {
  $definition = [System.Activator]::CreateInstance($definitionType)
  $definition.PropertyId = [string](Get-RequiredValue $raw @('propertyId', 'property_id'))
  $definition.Identity = [string](Get-RequiredValue $raw @('identity'))
  $identityParts = $definition.Identity.Split('|')
  if ($identityParts.Count -ne 3) { throw 'FINAL_MANIFEST_IDENTITY_INVALID' }
  $ifcEntity = Get-OptionalValue $raw @('ifcEntity', 'ifc_entity')
  $ifcPropertySet = Get-OptionalValue $raw @(
    'ifcPropertySet', 'ifc_property_set')
  $ifcProperty = Get-OptionalValue $raw @('ifcProperty', 'ifc_property')
  $definition.IfcEntity = if ($null -eq $ifcEntity) {
    $identityParts[0]
  } else { [string]$ifcEntity }
  $definition.IfcPropertySet = if ($null -eq $ifcPropertySet) {
    $identityParts[1]
  } else { [string]$ifcPropertySet }
  $definition.IfcProperty = if ($null -eq $ifcProperty) {
    $identityParts[2]
  } else { [string]$ifcProperty }
  $definition.DeclaredIfcType = [string](Get-RequiredValue $raw @('declaredIfcType', 'declared_ifc_type'))
  $canonicalUnit = Get-OptionalValue $raw @(
    'canonicalUnit', 'canonical_unit')
  $definition.CanonicalUnit = if ($null -eq $canonicalUnit) {
    ''
  } else { [string]$canonicalUnit }
  $definition.ParameterGuid = [string](Get-RequiredValue $raw @('parameterGuid', 'parameter_guid'))
  $definition.BindingScope = [string](Get-RequiredValue $raw @('bindingScope', 'binding_scope'))
  $definition.SourceStage = ([string](Get-RequiredValue $raw @(
    'sourceStage', 'source_stage'))).ToUpperInvariant()
  $definitions.Add($definition)
  $definitionByPropertyId.Add($definition.PropertyId, $definition)
}
$manifest.Definitions = $definitions
$manifestIdentityRaw = Get-OptionalValue $manifestRaw @('identity')
$manifest.Identity = if ($null -ne $manifestIdentityRaw) {
  New-Identity $manifestIdentityRaw $identityType
} else {
  New-ReportIdentity $scan $identityType $goldenRvtSha256 $officialIfcSha256
}

$readbackListType = [System.Collections.Generic.List``1].MakeGenericType($readbackType)
$readbacks = [System.Activator]::CreateInstance($readbackListType)
$seenReadbackPropertyIds = [System.Collections.Generic.HashSet[string]]::new(
  [System.StringComparer]::Ordinal)
foreach ($group in $readbacksRaw) {
  $propertyId = [string](Get-RequiredValue $group @('propertyId', 'property_id'))
  if (-not $seenReadbackPropertyIds.Add($propertyId)) {
    throw 'FINAL_REVIT_READBACK_GROUP_DUPLICATE'
  }
  if (-not $definitionByPropertyId.TryGetValue($propertyId, [ref]$definition)) {
    throw 'FINAL_REVIT_READBACK_PROPERTY_UNKNOWN'
  }
  $sourceStage = ([string](Get-RequiredValue $group @(
    'sourceStage', 'source_stage'))).ToUpperInvariant()
  $sourceResultHash = [string](Get-RequiredValue $group @(
    'sourceResultHash', 'source_result_hash'))
  foreach ($raw in @(Get-RequiredValue $group @('values'))) {
    $readback = [System.Activator]::CreateInstance($readbackType)
    $readback.PropertyId = $propertyId
    $readback.OwnerGlobalId = [string](Get-RequiredValue $raw @(
      'expectedIfcGlobalId', 'expected_ifc_global_id'))
    $readback.OwnerRevitUniqueId = [string](Get-RequiredValue $raw @(
      'revitUniqueId', 'revit_unique_id'))
    $readback.ParameterGuid = $definition.ParameterGuid
    $readback.CanonicalValue = [string](Get-RequiredValue $raw @(
      'canonicalValue', 'canonical_value'))
    $readback.SourceStage = $sourceStage
    $readback.SourceResultHash = $sourceResultHash
    $readbacks.Add($readback)
  }
}

$strictIdentityRaw = Get-OptionalValue $strict @(
  'identity', 'official_acceptance_identity')
$officialIdentityRaw = Get-OptionalValue $official @(
  'identity', 'official_acceptance_identity')
$request = [System.Activator]::CreateInstance($requestType)
$request.Manifest = $manifest
$request.RevitReadbacks = $readbacks
$request.StrictValidationIdentity = if ($null -ne $strictIdentityRaw) {
  New-Identity $strictIdentityRaw $identityType
} else {
  New-ReportIdentity $strict $identityType $goldenRvtSha256 $officialIfcSha256
}
$request.OfficialExportIdentity = if ($null -ne $officialIdentityRaw) {
  New-Identity $officialIdentityRaw $identityType
} else {
  New-ReportIdentity $scan $identityType $goldenRvtSha256 $officialIfcSha256
}
$request.GoldenRvtPath = $goldenRvtPath
$request.OfficialIfcPath = $officialIfcPath

$method = $inspectorType.GetMethod('ResolveFinalReadback', @($requestType))
if ($null -eq $method) { throw 'FINAL_READBACK_API_MISSING' }
$result = $method.Invoke($null, @($request))
if (-not $result.Success) { throw ([string]$result.ErrorCode) }

$payload = [ordered]@{
  schemaVersion = 'HBR_OFFICIAL_PROPERTY_READBACK_RESULT_V1'
  success = $true
  official_acceptance_manifest = $manifestRaw
  propertyReadbacks = @($result.Records)
  inputs = [ordered]@{
    scanEvidencePath = [System.IO.Path]::GetFullPath($ScanEvidencePath)
    scanEvidenceSha256 = Get-Sha256 -Path $ScanEvidencePath
    strictValidationPath = [System.IO.Path]::GetFullPath($StrictValidationPath)
    strictValidationSha256 = Get-Sha256 -Path $StrictValidationPath
    officialExportResultPath = [System.IO.Path]::GetFullPath($OfficialExportResultPath)
    officialExportResultSha256 = Get-Sha256 -Path $OfficialExportResultPath
    hifcCoreDllPath = [System.IO.Path]::GetFullPath($HifcCoreDllPath)
    hifcCoreDllSha256 = $HifcCoreDllSha256.ToLowerInvariant()
  }
}
$outputFullPath = [System.IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $outputFullPath) {
  throw 'FINAL_READBACK_RESULT_ALREADY_EXISTS'
}
[System.IO.Directory]::CreateDirectory(
  [System.IO.Path]::GetDirectoryName($outputFullPath)) | Out-Null
$json = $payload | ConvertTo-Json -Depth 64
$encoding = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($outputFullPath, $json, $encoding)
$payload | ConvertTo-Json -Depth 64 -Compress
