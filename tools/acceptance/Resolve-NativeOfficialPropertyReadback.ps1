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
$manifest.SchemaVersion = [string](Get-RequiredValue $manifestRaw @('schemaVersion', 'schema_version'))
$manifest.ManifestVersion = [string](Get-RequiredValue $manifestRaw @('manifestVersion', 'manifest_version'))
$manifest.Identity = New-Identity (Get-RequiredValue $manifestRaw @('identity')) $identityType
$definitionListType = [System.Collections.Generic.List``1].MakeGenericType($definitionType)
$definitions = [System.Activator]::CreateInstance($definitionListType)
foreach ($raw in @(Get-RequiredValue $manifestRaw @('definitions'))) {
  $definition = [System.Activator]::CreateInstance($definitionType)
  $definition.PropertyId = [string](Get-RequiredValue $raw @('propertyId', 'property_id'))
  $definition.IfcEntity = [string](Get-RequiredValue $raw @('ifcEntity', 'ifc_entity'))
  $definition.IfcPropertySet = [string](Get-RequiredValue $raw @('ifcPropertySet', 'ifc_property_set'))
  $definition.IfcProperty = [string](Get-RequiredValue $raw @('ifcProperty', 'ifc_property'))
  $definition.DeclaredIfcType = [string](Get-RequiredValue $raw @('declaredIfcType', 'declared_ifc_type'))
  $unitProperty = $raw.PSObject.Properties['canonicalUnit']
  $definition.CanonicalUnit = if ($null -eq $unitProperty -or $null -eq $unitProperty.Value) { '' } else { [string]$unitProperty.Value }
  $definition.ParameterGuid = [string](Get-RequiredValue $raw @('parameterGuid', 'parameter_guid'))
  $definitions.Add($definition)
}
$manifest.Definitions = $definitions

$readbackListType = [System.Collections.Generic.List``1].MakeGenericType($readbackType)
$readbacks = [System.Activator]::CreateInstance($readbackListType)
foreach ($raw in $readbacksRaw) {
  $readback = [System.Activator]::CreateInstance($readbackType)
  $readback.PropertyId = [string](Get-RequiredValue $raw @('propertyId', 'property_id'))
  $readback.OwnerGlobalId = [string](Get-RequiredValue $raw @('ownerGlobalId', 'owner_global_id'))
  $readback.OwnerRevitUniqueId = [string](Get-RequiredValue $raw @('ownerRevitUniqueId', 'owner_revit_unique_id'))
  $readback.ParameterGuid = [string](Get-RequiredValue $raw @('parameterGuid', 'parameter_guid'))
  $readback.CanonicalValue = [string](Get-RequiredValue $raw @('canonicalValue', 'canonical_value'))
  $readback.SourceStage = [string](Get-RequiredValue $raw @('sourceStage', 'source_stage'))
  $readback.SourceResultHash = [string](Get-RequiredValue $raw @('sourceResultHash', 'source_result_hash'))
  $readbacks.Add($readback)
}

$strictIdentityRaw = Get-RequiredValue $strict @('identity', 'official_acceptance_identity')
$officialIdentityRaw = Get-RequiredValue $official @('identity', 'official_acceptance_identity')
$request = [System.Activator]::CreateInstance($requestType)
$request.Manifest = $manifest
$request.RevitReadbacks = $readbacks
$request.StrictValidationIdentity = New-Identity $strictIdentityRaw $identityType
$request.OfficialExportIdentity = New-Identity $officialIdentityRaw $identityType
$request.GoldenRvtPath = [string](Get-RequiredValue $strict @('goldenRvtPath', 'golden_rvt_path'))
$request.OfficialIfcPath = [string](Get-RequiredValue $official @('officialIfcPath', 'official_ifc_path'))

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
