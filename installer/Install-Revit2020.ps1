[CmdletBinding(SupportsShouldProcess = $true)]
param(
  [switch]$Uninstall,
  [switch]$Force,
  [string]$SourceRoot = $PSScriptRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($env:APPDATA)) {
  throw "APPDATA 环境变量不可用，无法定位 Revit 用户级 Addins 目录。"
}
if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
  throw "LOCALAPPDATA 环境变量不可用，无法定位 BIMBaoGui MCP Server 目录。"
}

$productName = "BIMBaoGui.RevitAddin"
$mcpProductName = "BIMBaoGui.McpServer"
$mcpVersion = "0.4.3"
$legacyMcpVersions = @('0.4.0', '0.4.1', '0.4.2')
$addinRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2020"
$productRoot = Join-Path $addinRoot "BIMBaoGui.RevitAddin"
$manifestPath = Join-Path $addinRoot "BIMBaoGui.RevitAddin.addin"
$mcpBaseRoot = Join-Path $env:LOCALAPPDATA "BIMBaoGui\McpServer"
$mcpServerRoot = Join-Path $mcpBaseRoot $mcpVersion
$mcpConfigPath = Join-Path $mcpBaseRoot "mcp-server-config.json"
$bridgeDiscoveryRoot = Join-Path $env:LOCALAPPDATA "BIMBaoGui\Revit2020\bridges"

$runningRevit = @(Get-Process -Name "Revit" -ErrorAction SilentlyContinue)
if (-not $Force -and $runningRevit.Count -gt 0) {
  throw "检测到 Revit 正在运行。请先关闭 Revit，再安装或卸载；确需强制执行时显式使用 -Force。"
}

function Remove-ControlledPathWithRetry {
  param(
    [Parameter(Mandatory = $true)]
    [string]$Path,
    [switch]$Recurse,
    [int]$TimeoutMilliseconds = 10000
  )

  $deadline = [DateTime]::UtcNow.AddMilliseconds($TimeoutMilliseconds)
  while (Test-Path -LiteralPath $Path) {
    try {
      if ($Recurse) {
        Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
      }
      else {
        Remove-Item -LiteralPath $Path -Force -ErrorAction Stop
      }
    }
    catch {
      if ([DateTime]::UtcNow -ge $deadline) {
        throw
      }
    }
    if (-not (Test-Path -LiteralPath $Path)) {
      return
    }
    if ([DateTime]::UtcNow -ge $deadline) {
      throw "等待受控路径解除占用超时：$Path"
    }
    Start-Sleep -Milliseconds 50
  }
}

if ($Uninstall) {
  if (-not $PSCmdlet.ShouldProcess(
    $addinRoot,
    "卸载 BIMBaoGui Revit 2020 原生插件及 MCP Server")) {
    return
  }
  if (Test-Path -LiteralPath $manifestPath) {
    Remove-Item -LiteralPath $manifestPath -Force
  }
  if (Test-Path -LiteralPath $productRoot) {
    Remove-Item -LiteralPath $productRoot -Recurse -Force
  }
  if (Test-Path -LiteralPath $mcpServerRoot) {
    Remove-ControlledPathWithRetry -Path $mcpServerRoot -Recurse
  }
  if (Test-Path -LiteralPath $mcpConfigPath) {
    Remove-Item -LiteralPath $mcpConfigPath -Force
  }
  if (Test-Path -LiteralPath $bridgeDiscoveryRoot) {
    Get-ChildItem -LiteralPath $bridgeDiscoveryRoot -File -ErrorAction SilentlyContinue |
      Where-Object { $_.Name -like "*.json" -or $_.Name -like "*.json.tmp.*" } |
      Remove-Item -Force -ErrorAction SilentlyContinue
  }
  if (Test-Path -LiteralPath $mcpBaseRoot) {
    $remaining = @(Get-ChildItem -LiteralPath $mcpBaseRoot -Force -ErrorAction SilentlyContinue)
    if ($remaining.Count -eq 0) {
      Remove-Item -LiteralPath $mcpBaseRoot -Force -ErrorAction SilentlyContinue
    }
  }
  Write-Host "BIMBaoGui Revit 2020 原生插件及 MCP Server 已卸载。"
  return
}

function Find-RequiredFile {
  param(
    [string[]]$Candidates,
    [string]$DisplayName
  )
  $match = $Candidates |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
    Select-Object -First 1
  if ([string]::IsNullOrWhiteSpace($match)) {
    throw "安装包中未找到 $DisplayName。SourceRoot=$sourceRootFull"
  }
  return [IO.Path]::GetFullPath($match)
}

function Get-LowerSha256 {
  param([string]$Path)
  return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-SameHash {
  param(
    [string]$Expected,
    [string]$Actual,
    [string]$Label
  )
  if (-not [string]::Equals(
    $Expected,
    $Actual,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw "$Label 复制后 SHA-256 不一致，拒绝安装。expected=$Expected actual=$Actual"
  }
}

function Get-RevitAssemblyIdentity {
  param([string]$Path)
  $fullPath = [IO.Path]::GetFullPath($Path)
  $assembly = [Reflection.Assembly]::LoadFile($fullPath)
  $metadata = @{}
  $informationalVersion = ""
  foreach ($attribute in $assembly.GetCustomAttributesData()) {
    if ($attribute.AttributeType.FullName -eq
      "System.Reflection.AssemblyMetadataAttribute") {
      $metadata[[string]$attribute.ConstructorArguments[0].Value] =
        [string]$attribute.ConstructorArguments[1].Value
      continue
    }
    if ($attribute.AttributeType.FullName -eq
      "System.Reflection.AssemblyInformationalVersionAttribute") {
      $informationalVersion =
        [string]$attribute.ConstructorArguments[0].Value
    }
  }
  $assemblyVersion = $assembly.GetName().Version.ToString()
  $productVersion = if ([string]::IsNullOrWhiteSpace($informationalVersion)) {
    $assemblyVersion
  } else {
    $informationalVersion.Trim()
  }
  $metadataSeparator = $productVersion.IndexOf("+")
  if ($metadataSeparator -ge 0) {
    $productVersion = $productVersion.Substring(0, $metadataSeparator)
  }
  $buildNumber = if ($metadata.ContainsKey("HBR.BuildNumber") -and
    -not [string]::IsNullOrWhiteSpace($metadata["HBR.BuildNumber"])) {
    [string]$metadata["HBR.BuildNumber"]
  } else {
    "local"
  }
  $commitSha = if ($metadata.ContainsKey("HBR.CommitSha") -and
    -not [string]::IsNullOrWhiteSpace($metadata["HBR.CommitSha"])) {
    [string]$metadata["HBR.CommitSha"]
  } else {
    "unknown"
  }
  return [PSCustomObject]@{
    ProductVersion = $productVersion
    AssemblyVersion = $assemblyVersion
    InformationalVersion = $informationalVersion
    BuildNumber = $buildNumber.Trim()
    CommitSha = $commitSha.Trim()
  }
}

$sourceRootFull = [IO.Path]::GetFullPath($SourceRoot)
$sourceDll = Find-RequiredFile @(
  (Join-Path $sourceRootFull "BIMBaoGui.RevitAddin\BIMBaoGui.RevitAddin.dll"),
  (Join-Path $sourceRootFull "BIMBaoGui.RevitAddin.dll")
) "BIMBaoGui.RevitAddin.dll"
$sourceAddinDirectory = Split-Path -Parent $sourceDll
$sourcePdb = [IO.Path]::ChangeExtension($sourceDll, ".pdb")

$sourceContractsDll = Find-RequiredFile @(
  (Join-Path $sourceAddinDirectory "BIMBaoGui.McpContracts.dll"),
  (Join-Path $sourceRootFull "BIMBaoGui.McpContracts.dll"),
  (Join-Path $sourceRootFull "BIMBaoGui.RevitAddin\BIMBaoGui.McpContracts.dll")
) "BIMBaoGui.McpContracts.dll"
$sourceContractsPdb = [IO.Path]::ChangeExtension($sourceContractsDll, ".pdb")

$sourceHifcCoreDll = Find-RequiredFile @(
  (Join-Path $sourceAddinDirectory "BIMBaoGui.HifcCore.dll"),
  (Join-Path $sourceRootFull "BIMBaoGui.HifcCore.dll"),
  (Join-Path $sourceRootFull "BIMBaoGui.RevitAddin\BIMBaoGui.HifcCore.dll")
) "BIMBaoGui.HifcCore.dll"
$sourceHifcCorePdb = [IO.Path]::ChangeExtension($sourceHifcCoreDll, ".pdb")

$sourceMcpServerExe = Find-RequiredFile @(
  (Join-Path $sourceRootFull "BIMBaoGui.McpServer\BIMBaoGui.McpServer.exe"),
  (Join-Path $sourceRootFull "BIMBaoGui.McpServer.exe")
) "BIMBaoGui.McpServer.exe"
$sourceMcpServerPdb = [IO.Path]::ChangeExtension($sourceMcpServerExe, ".pdb")
$productIdentity = Get-RevitAssemblyIdentity -Path $sourceDll
$productVersion = $productIdentity.ProductVersion

if (-not $PSCmdlet.ShouldProcess(
  $productRoot,
  "安装 BIMBaoGui Revit 2020 原生插件 $productVersion 及 MCP Server $mcpVersion")) {
  return
}

New-Item -ItemType Directory -Force -Path $addinRoot | Out-Null
New-Item -ItemType Directory -Force -Path $mcpBaseRoot | Out-Null
$stagingRoot = Join-Path $addinRoot (".{0}.{1}.installing" -f $productName, [Guid]::NewGuid().ToString("N"))
$mcpStagingRoot = Join-Path $mcpBaseRoot (".{0}.{1}.installing" -f $mcpProductName, [Guid]::NewGuid().ToString("N"))
$manifestStagingPath = Join-Path $addinRoot (".{0}.{1}.addin.tmp" -f $productName, [Guid]::NewGuid().ToString("N"))
$configStagingPath = Join-Path $mcpBaseRoot (".mcp-server-config.{0}.json.tmp" -f [Guid]::NewGuid().ToString("N"))
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

try {
  New-Item -ItemType Directory -Force -Path $stagingRoot | Out-Null
  New-Item -ItemType Directory -Force -Path $mcpStagingRoot | Out-Null

  $stagedDll = Join-Path $stagingRoot "BIMBaoGui.RevitAddin.dll"
  $stagedContractsDll = Join-Path $stagingRoot "BIMBaoGui.McpContracts.dll"
  $stagedHifcCoreDll = Join-Path $stagingRoot "BIMBaoGui.HifcCore.dll"
  $stagedMcpServerExe = Join-Path $mcpStagingRoot "BIMBaoGui.McpServer.exe"
  Copy-Item -LiteralPath $sourceDll -Destination $stagedDll -Force
  Copy-Item -LiteralPath $sourceContractsDll -Destination $stagedContractsDll -Force
  Copy-Item -LiteralPath $sourceHifcCoreDll -Destination $stagedHifcCoreDll -Force
  Copy-Item -LiteralPath $sourceMcpServerExe -Destination $stagedMcpServerExe -Force

  foreach ($copy in @(
    @($sourcePdb, (Join-Path $stagingRoot "BIMBaoGui.RevitAddin.pdb")),
    @($sourceContractsPdb, (Join-Path $stagingRoot "BIMBaoGui.McpContracts.pdb")),
    @($sourceHifcCorePdb, (Join-Path $stagingRoot "BIMBaoGui.HifcCore.pdb")),
    @($sourceMcpServerPdb, (Join-Path $mcpStagingRoot "BIMBaoGui.McpServer.pdb"))
  )) {
    if (Test-Path -LiteralPath $copy[0] -PathType Leaf) {
      Copy-Item -LiteralPath $copy[0] -Destination $copy[1] -Force
    }
  }

  $sourceHash = Get-LowerSha256 $sourceDll
  $contractsDllSha256 = Get-LowerSha256 $sourceContractsDll
  $hifcCoreDllSha256 = Get-LowerSha256 $sourceHifcCoreDll
  $mcpServerExeSha256 = Get-LowerSha256 $sourceMcpServerExe
  Assert-SameHash $sourceHash (Get-LowerSha256 $stagedDll) "Revit DLL"
  Assert-SameHash $contractsDllSha256 (Get-LowerSha256 $stagedContractsDll) "MCP Contracts DLL"
  Assert-SameHash $hifcCoreDllSha256 (Get-LowerSha256 $stagedHifcCoreDll) "H-IFC Core DLL"
  Assert-SameHash $mcpServerExeSha256 (Get-LowerSha256 $stagedMcpServerExe) "MCP Server EXE"

  if (Test-Path -LiteralPath $productRoot) {
    Remove-Item -LiteralPath $productRoot -Recurse -Force
  }
  if (Test-Path -LiteralPath $mcpServerRoot) {
    Remove-Item -LiteralPath $mcpServerRoot -Recurse -Force
  }
  foreach ($legacyMcpVersion in $legacyMcpVersions) {
    $legacyMcpRoot = Join-Path $mcpBaseRoot $legacyMcpVersion
    if (Test-Path -LiteralPath $legacyMcpRoot) {
      Remove-ControlledPathWithRetry -Path $legacyMcpRoot -Recurse
    }
  }
  Move-Item -LiteralPath $stagingRoot -Destination $productRoot
  Move-Item -LiteralPath $mcpStagingRoot -Destination $mcpServerRoot

  $installedDllPath = [IO.Path]::GetFullPath((Join-Path $productRoot "BIMBaoGui.RevitAddin.dll"))
  $installedContractsPath = [IO.Path]::GetFullPath((Join-Path $productRoot "BIMBaoGui.McpContracts.dll"))
  $installedHifcCorePath = [IO.Path]::GetFullPath((Join-Path $productRoot "BIMBaoGui.HifcCore.dll"))
  $installedMcpServerPath = [IO.Path]::GetFullPath((Join-Path $mcpServerRoot "BIMBaoGui.McpServer.exe"))
  $escapedAssemblyPath = [System.Security.SecurityElement]::Escape($installedDllPath)
  $manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>BIMBaoGui Revit Add-in</Name>
    <Assembly>$escapedAssemblyPath</Assembly>
    <AddInId>6F3EE836-2A54-43C1-8B90-C9D291E9A8F1</AddInId>
    <FullClassName>BIMBaoGui.RevitAddin.App</FullClassName>
    <VendorId>ARWD</VendorId>
    <VendorDescription>ArchitectureWorld BIM planning approval tools</VendorDescription>
  </AddIn>
</RevitAddIns>
"@
  [IO.File]::WriteAllText($manifestStagingPath, $manifest, $utf8NoBom)
  if (Test-Path -LiteralPath $manifestPath) {
    Remove-Item -LiteralPath $manifestPath -Force
  }
  Move-Item -LiteralPath $manifestStagingPath -Destination $manifestPath

  $mcpClientConfig = [ordered]@{
    mcpServers = [ordered]@{
      "bimbaogui-revit" = [ordered]@{
        command = $installedMcpServerPath
        args = @()
      }
    }
  }
  [IO.File]::WriteAllText(
    $configStagingPath,
    ($mcpClientConfig | ConvertTo-Json -Depth 8),
    $utf8NoBom)
  if (Test-Path -LiteralPath $mcpConfigPath) {
    Remove-Item -LiteralPath $mcpConfigPath -Force
  }
  Move-Item -LiteralPath $configStagingPath -Destination $mcpConfigPath

  $installedUtc = [DateTimeOffset]::UtcNow.ToString("O")
  $evidence = [ordered]@{
    schemaVersion = "4.0.0"
    productName = $productName
    productVersion = $productVersion
    assemblyVersion = $productIdentity.AssemblyVersion
    assemblyInformationalVersion = $productIdentity.InformationalVersion
    buildNumber = $productIdentity.BuildNumber
    commitSha = $productIdentity.CommitSha
    mcpProductName = $mcpProductName
    mcpProductVersion = $mcpVersion
    target = "Revit 2020"
    installedUtc = $installedUtc
    sourceRoot = $sourceRootFull
    sourceDll = $sourceDll
    sourceDllSha256 = $sourceHash
    installedDll = $installedDllPath
    installedDllSha256 = Get-LowerSha256 $installedDllPath
    sourceContractsDll = $sourceContractsDll
    sourceContractsDllSha256 = $contractsDllSha256
    installedContractsDll = $installedContractsPath
    installedContractsDllSha256 = Get-LowerSha256 $installedContractsPath
    contractsDllSha256 = $contractsDllSha256
    sourceHifcCoreDll = $sourceHifcCoreDll
    sourceHifcCoreDllSha256 = $hifcCoreDllSha256
    installedHifcCoreDll = $installedHifcCorePath
    installedHifcCoreDllSha256 = Get-LowerSha256 $installedHifcCorePath
    hifcCoreDllSha256 = $hifcCoreDllSha256
    sourceMcpServerExe = $sourceMcpServerExe
    sourceMcpServerExeSha256 = $mcpServerExeSha256
    installedMcpServerExe = $installedMcpServerPath
    installedMcpServerExeSha256 = Get-LowerSha256 $installedMcpServerPath
    mcpServerExeSha256 = $mcpServerExeSha256
    manifestPath = [IO.Path]::GetFullPath($manifestPath)
    mcpConfigPath = [IO.Path]::GetFullPath($mcpConfigPath)
    bridgeDiscoveryRoot = [IO.Path]::GetFullPath($bridgeDiscoveryRoot)
  }
  $evidenceJson = $evidence | ConvertTo-Json -Depth 8
  $evidencePath = Join-Path $productRoot "install-evidence.json"
  [IO.File]::WriteAllText($evidencePath, $evidenceJson, $utf8NoBom)
  [IO.File]::WriteAllText(
    (Join-Path $mcpServerRoot "install-evidence.json"),
    $evidenceJson,
    $utf8NoBom)

  foreach ($path in @(
    $installedDllPath,
    $installedContractsPath,
    $installedHifcCorePath,
    $installedMcpServerPath,
    (Join-Path $productRoot "BIMBaoGui.RevitAddin.pdb"),
    (Join-Path $productRoot "BIMBaoGui.McpContracts.pdb"),
    (Join-Path $productRoot "BIMBaoGui.HifcCore.pdb"),
    (Join-Path $mcpServerRoot "BIMBaoGui.McpServer.pdb")
  )) {
    if (Test-Path -LiteralPath $path -PathType Leaf) {
      Unblock-File -LiteralPath $path -ErrorAction SilentlyContinue
    }
  }

  Assert-SameHash $sourceHash (Get-LowerSha256 $installedDllPath) "正式 Revit DLL"
  Assert-SameHash $contractsDllSha256 (Get-LowerSha256 $installedContractsPath) "正式 MCP Contracts DLL"
  Assert-SameHash $hifcCoreDllSha256 (Get-LowerSha256 $installedHifcCorePath) "正式 H-IFC Core DLL"
  Assert-SameHash $mcpServerExeSha256 (Get-LowerSha256 $installedMcpServerPath) "正式 MCP Server EXE"

  Write-Host "BIMBaoGui Revit 2020 原生插件及 MCP Server 安装完成。"
  Write-Host "Manifest: $manifestPath"
  Write-Host "Revit Assembly: $installedDllPath"
  Write-Host "H-IFC Core: $installedHifcCorePath"
  Write-Host "MCP Server: $installedMcpServerPath"
  Write-Host "MCP Config: $mcpConfigPath"
  Write-Host "Revit SHA-256: $sourceHash"
  Write-Host "H-IFC Core SHA-256: $hifcCoreDllSha256"
  Write-Host "MCP SHA-256: $mcpServerExeSha256"
}
finally {
  if (Test-Path -LiteralPath $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
  }
  if (Test-Path -LiteralPath $mcpStagingRoot) {
    Remove-Item -LiteralPath $mcpStagingRoot -Recurse -Force -ErrorAction SilentlyContinue
  }
  if (Test-Path -LiteralPath $manifestStagingPath) {
    Remove-Item -LiteralPath $manifestStagingPath -Force -ErrorAction SilentlyContinue
  }
  if (Test-Path -LiteralPath $configStagingPath) {
    Remove-Item -LiteralPath $configStagingPath -Force -ErrorAction SilentlyContinue
  }
}
