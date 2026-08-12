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
$mcpVersion = "0.3.2"
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

if ($Uninstall) {
  if (-not $PSCmdlet.ShouldProcess($addinRoot, "卸载 BIMBaoGui Revit 2020 原生插件及 MCP Server")) {
    return
  }
  if (Test-Path -LiteralPath $manifestPath) {
    Remove-Item -LiteralPath $manifestPath -Force
  }
  if (Test-Path -LiteralPath $productRoot) {
    Remove-Item -LiteralPath $productRoot -Recurse -Force
  }
  if (Test-Path -LiteralPath $mcpServerRoot) {
    Remove-Item -LiteralPath $mcpServerRoot -Recurse -Force
  }
  if (Test-Path -LiteralPath $mcpBaseRoot) {
    Get-ChildItem -LiteralPath $mcpBaseRoot -Directory -ErrorAction SilentlyContinue |
      Where-Object { $_.Name -match '^\d+\.\d+\.\d+$' } |
      ForEach-Object {
        Remove-Item -LiteralPath $_.FullName -Recurse -Force
      }
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

$sourceRootFull = [IO.Path]::GetFullPath($SourceRoot)
$sourceDllCandidates = @(
  (Join-Path $sourceRootFull "BIMBaoGui.RevitAddin\BIMBaoGui.RevitAddin.dll"),
  (Join-Path $sourceRootFull "BIMBaoGui.RevitAddin.dll")
)
$sourceDll = $sourceDllCandidates |
  Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
  Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($sourceDll)) {
  throw "安装包中未找到 BIMBaoGui.RevitAddin.dll。SourceRoot=$sourceRootFull"
}
$sourceDll = [IO.Path]::GetFullPath($sourceDll)
$sourceAddinDirectory = Split-Path -Parent $sourceDll
$sourcePdb = [IO.Path]::ChangeExtension($sourceDll, ".pdb")

$contractsCandidates = @(
  (Join-Path $sourceAddinDirectory "BIMBaoGui.McpContracts.dll"),
  (Join-Path $sourceRootFull "BIMBaoGui.McpContracts.dll"),
  (Join-Path $sourceRootFull "BIMBaoGui.RevitAddin\BIMBaoGui.McpContracts.dll")
)
$sourceContractsDll = $contractsCandidates |
  Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
  Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($sourceContractsDll)) {
  throw "安装包中未找到 BIMBaoGui.McpContracts.dll。SourceRoot=$sourceRootFull"
}
$sourceContractsDll = [IO.Path]::GetFullPath($sourceContractsDll)
$sourceContractsPdb = [IO.Path]::ChangeExtension($sourceContractsDll, ".pdb")

$mcpServerCandidates = @(
  (Join-Path $sourceRootFull "BIMBaoGui.McpServer\BIMBaoGui.McpServer.exe"),
  (Join-Path $sourceRootFull "BIMBaoGui.McpServer.exe")
)
$sourceMcpServerExe = $mcpServerCandidates |
  Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
  Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($sourceMcpServerExe)) {
  throw "安装包中未找到 BIMBaoGui.McpServer.exe。SourceRoot=$sourceRootFull"
}
$sourceMcpServerExe = [IO.Path]::GetFullPath($sourceMcpServerExe)
$sourceMcpServerPdb = [IO.Path]::ChangeExtension($sourceMcpServerExe, ".pdb")
$productVersion = [Reflection.AssemblyName]::GetAssemblyName($sourceDll).Version.ToString()

if (-not $PSCmdlet.ShouldProcess($productRoot, "安装 BIMBaoGui Revit 2020 原生插件 $productVersion 及 MCP Server $mcpVersion")) {
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
  $stagedMcpServerExe = Join-Path $mcpStagingRoot "BIMBaoGui.McpServer.exe"
  Copy-Item -LiteralPath $sourceDll -Destination $stagedDll -Force
  Copy-Item -LiteralPath $sourceContractsDll -Destination $stagedContractsDll -Force
  Copy-Item -LiteralPath $sourceMcpServerExe -Destination $stagedMcpServerExe -Force

  if (Test-Path -LiteralPath $sourcePdb -PathType Leaf) {
    Copy-Item -LiteralPath $sourcePdb -Destination (Join-Path $stagingRoot "BIMBaoGui.RevitAddin.pdb") -Force
  }
  if (Test-Path -LiteralPath $sourceContractsPdb -PathType Leaf) {
    Copy-Item -LiteralPath $sourceContractsPdb -Destination (Join-Path $stagingRoot "BIMBaoGui.McpContracts.pdb") -Force
  }
  if (Test-Path -LiteralPath $sourceMcpServerPdb -PathType Leaf) {
    Copy-Item -LiteralPath $sourceMcpServerPdb -Destination (Join-Path $mcpStagingRoot "BIMBaoGui.McpServer.pdb") -Force
  }

  $sourceHash = (Get-FileHash -LiteralPath $sourceDll -Algorithm SHA256).Hash.ToLowerInvariant()
  $installedHash = (Get-FileHash -LiteralPath $stagedDll -Algorithm SHA256).Hash.ToLowerInvariant()
  $contractsDllSha256 = (Get-FileHash -LiteralPath $sourceContractsDll -Algorithm SHA256).Hash.ToLowerInvariant()
  $stagedContractsHash = (Get-FileHash -LiteralPath $stagedContractsDll -Algorithm SHA256).Hash.ToLowerInvariant()
  $mcpServerExeSha256 = (Get-FileHash -LiteralPath $sourceMcpServerExe -Algorithm SHA256).Hash.ToLowerInvariant()
  $stagedMcpServerHash = (Get-FileHash -LiteralPath $stagedMcpServerExe -Algorithm SHA256).Hash.ToLowerInvariant()

  if (-not [string]::Equals($sourceHash, $installedHash, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Revit DLL 复制后 SHA-256 不一致，拒绝安装。sourceHash=$sourceHash installedHash=$installedHash"
  }
  if (-not [string]::Equals($contractsDllSha256, $stagedContractsHash, [StringComparison]::OrdinalIgnoreCase)) {
    throw "MCP Contracts DLL 复制后 SHA-256 不一致，拒绝安装。"
  }
  if (-not [string]::Equals($mcpServerExeSha256, $stagedMcpServerHash, [StringComparison]::OrdinalIgnoreCase)) {
    throw "MCP Server EXE 复制后 SHA-256 不一致，拒绝安装。"
  }

  if (Test-Path -LiteralPath $productRoot) {
    Remove-Item -LiteralPath $productRoot -Recurse -Force
  }
  if (Test-Path -LiteralPath $mcpServerRoot) {
    Remove-Item -LiteralPath $mcpServerRoot -Recurse -Force
  }
  if (Test-Path -LiteralPath $mcpBaseRoot) {
    Get-ChildItem -LiteralPath $mcpBaseRoot -Directory -ErrorAction SilentlyContinue |
      Where-Object { $_.Name -match '^\d+\.\d+\.\d+$' } |
      ForEach-Object {
        Remove-Item -LiteralPath $_.FullName -Recurse -Force
      }
  }
  Move-Item -LiteralPath $stagingRoot -Destination $productRoot
  Move-Item -LiteralPath $mcpStagingRoot -Destination $mcpServerRoot

  $installedDllPath = [IO.Path]::GetFullPath((Join-Path $productRoot "BIMBaoGui.RevitAddin.dll"))
  $installedContractsPath = [IO.Path]::GetFullPath((Join-Path $productRoot "BIMBaoGui.McpContracts.dll"))
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
    schemaVersion = "2.0.0"
    productName = $productName
    productVersion = $productVersion
    mcpProductName = $mcpProductName
    mcpProductVersion = $mcpVersion
    target = "Revit 2020"
    installedUtc = $installedUtc
    sourceRoot = $sourceRootFull
    sourceDll = $sourceDll
    sourceDllSha256 = $sourceHash
    installedDll = $installedDllPath
    installedDllSha256 = $installedHash
    installedContractsDll = $installedContractsPath
    contractsDllSha256 = $contractsDllSha256
    installedMcpServerExe = $installedMcpServerPath
    mcpServerExeSha256 = $mcpServerExeSha256
    manifestPath = [IO.Path]::GetFullPath($manifestPath)
    mcpConfigPath = [IO.Path]::GetFullPath($mcpConfigPath)
    bridgeDiscoveryRoot = [IO.Path]::GetFullPath($bridgeDiscoveryRoot)
  }
  $evidencePath = Join-Path $productRoot "install-evidence.json"
  [IO.File]::WriteAllText(
    $evidencePath,
    ($evidence | ConvertTo-Json -Depth 8),
    $utf8NoBom)
  [IO.File]::WriteAllText(
    (Join-Path $mcpServerRoot "install-evidence.json"),
    ($evidence | ConvertTo-Json -Depth 8),
    $utf8NoBom)

  foreach ($path in @(
    $installedDllPath,
    $installedContractsPath,
    $installedMcpServerPath,
    (Join-Path $productRoot "BIMBaoGui.RevitAddin.pdb"),
    (Join-Path $productRoot "BIMBaoGui.McpContracts.pdb"),
    (Join-Path $mcpServerRoot "BIMBaoGui.McpServer.pdb")
  )) {
    if (Test-Path -LiteralPath $path -PathType Leaf) {
      Unblock-File -LiteralPath $path -ErrorAction SilentlyContinue
    }
  }

  $finalHash = (Get-FileHash -LiteralPath $installedDllPath -Algorithm SHA256).Hash.ToLowerInvariant()
  $finalContractsHash = (Get-FileHash -LiteralPath $installedContractsPath -Algorithm SHA256).Hash.ToLowerInvariant()
  $finalMcpHash = (Get-FileHash -LiteralPath $installedMcpServerPath -Algorithm SHA256).Hash.ToLowerInvariant()
  if (-not [string]::Equals($installedHash, $finalHash, [StringComparison]::OrdinalIgnoreCase)) {
    throw "正式安装目录中的 Revit DLL SHA-256 与验证值不一致。"
  }
  if (-not [string]::Equals($contractsDllSha256, $finalContractsHash, [StringComparison]::OrdinalIgnoreCase)) {
    throw "正式安装目录中的 MCP Contracts DLL SHA-256 与验证值不一致。"
  }
  if (-not [string]::Equals($mcpServerExeSha256, $finalMcpHash, [StringComparison]::OrdinalIgnoreCase)) {
    throw "正式安装目录中的 MCP Server EXE SHA-256 与验证值不一致。"
  }

  Write-Host "BIMBaoGui Revit 2020 原生插件及 MCP Server 安装完成。"
  Write-Host "Manifest: $manifestPath"
  Write-Host "Revit Assembly: $installedDllPath"
  Write-Host "MCP Server: $installedMcpServerPath"
  Write-Host "MCP Config: $mcpConfigPath"
  Write-Host "Revit SHA-256: $finalHash"
  Write-Host "MCP SHA-256: $finalMcpHash"
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
