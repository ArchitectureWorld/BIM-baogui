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

$productName = "BIMBaoGui.RevitAddin"
$addinRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2020"
$productRoot = Join-Path $addinRoot "BIMBaoGui.RevitAddin"
$manifestPath = Join-Path $addinRoot "BIMBaoGui.RevitAddin.addin"

$runningRevit = @(Get-Process -Name "Revit" -ErrorAction SilentlyContinue)
if (-not $Force -and $runningRevit.Count -gt 0) {
  throw "检测到 Revit 正在运行。请先关闭 Revit，再安装或卸载；确需强制执行时显式使用 -Force。"
}

if ($Uninstall) {
  if (-not $PSCmdlet.ShouldProcess($addinRoot, "卸载 BIMBaoGui Revit 2020 原生插件")) {
    return
  }
  if (Test-Path -LiteralPath $manifestPath) {
    Remove-Item -LiteralPath $manifestPath -Force
  }
  if (Test-Path -LiteralPath $productRoot) {
    Remove-Item -LiteralPath $productRoot -Recurse -Force
  }
  Write-Host "BIMBaoGui Revit 2020 原生插件已卸载。"
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
$sourcePdb = [IO.Path]::ChangeExtension($sourceDll, ".pdb")
$productVersion = [Reflection.AssemblyName]::GetAssemblyName($sourceDll).Version.ToString()

if (-not $PSCmdlet.ShouldProcess($productRoot, "安装 BIMBaoGui Revit 2020 原生插件 $productVersion")) {
  return
}

New-Item -ItemType Directory -Force -Path $addinRoot | Out-Null
$stagingRoot = Join-Path $addinRoot (".{0}.{1}.installing" -f $productName, [Guid]::NewGuid().ToString("N"))
$manifestStagingPath = Join-Path $addinRoot (".{0}.{1}.addin.tmp" -f $productName, [Guid]::NewGuid().ToString("N"))
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

try {
  New-Item -ItemType Directory -Force -Path $stagingRoot | Out-Null
  $stagedDll = Join-Path $stagingRoot "BIMBaoGui.RevitAddin.dll"
  Copy-Item -LiteralPath $sourceDll -Destination $stagedDll -Force
  if (Test-Path -LiteralPath $sourcePdb -PathType Leaf) {
    Copy-Item -LiteralPath $sourcePdb -Destination (Join-Path $stagingRoot "BIMBaoGui.RevitAddin.pdb") -Force
  }

  $sourceHash = (Get-FileHash -LiteralPath $sourceDll -Algorithm SHA256).Hash.ToLowerInvariant()
  $installedHash = (Get-FileHash -LiteralPath $stagedDll -Algorithm SHA256).Hash.ToLowerInvariant()
  if (-not [string]::Equals($sourceHash, $installedHash, [StringComparison]::OrdinalIgnoreCase)) {
    throw "DLL 复制后 SHA-256 不一致，拒绝安装。sourceHash=$sourceHash installedHash=$installedHash"
  }

  $installedUtc = [DateTimeOffset]::UtcNow.ToString("O")
  $evidence = [ordered]@{
    schemaVersion = "1.0.0"
    productName = $productName
    productVersion = $productVersion
    target = "Revit 2020"
    installedUtc = $installedUtc
    sourceRoot = $sourceRootFull
    sourceDll = $sourceDll
    sourceDllSha256 = $sourceHash
    installedDll = [IO.Path]::GetFullPath((Join-Path $productRoot "BIMBaoGui.RevitAddin.dll"))
    installedDllSha256 = $installedHash
    manifestPath = [IO.Path]::GetFullPath($manifestPath)
  }
  $evidencePath = Join-Path $stagingRoot "install-evidence.json"
  [IO.File]::WriteAllText(
    $evidencePath,
    ($evidence | ConvertTo-Json -Depth 8),
    $utf8NoBom)

  if (Test-Path -LiteralPath $productRoot) {
    Remove-Item -LiteralPath $productRoot -Recurse -Force
  }
  Move-Item -LiteralPath $stagingRoot -Destination $productRoot

  $installedDllPath = [IO.Path]::GetFullPath((Join-Path $productRoot "BIMBaoGui.RevitAddin.dll"))
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

  Unblock-File -LiteralPath $installedDllPath -ErrorAction SilentlyContinue
  $installedPdbPath = Join-Path $productRoot "BIMBaoGui.RevitAddin.pdb"
  if (Test-Path -LiteralPath $installedPdbPath) {
    Unblock-File -LiteralPath $installedPdbPath -ErrorAction SilentlyContinue
  }

  $finalHash = (Get-FileHash -LiteralPath $installedDllPath -Algorithm SHA256).Hash.ToLowerInvariant()
  if (-not [string]::Equals($installedHash, $finalHash, [StringComparison]::OrdinalIgnoreCase)) {
    throw "正式安装目录中的 DLL SHA-256 与验证值不一致。"
  }

  Write-Host "BIMBaoGui Revit 2020 原生插件安装完成。"
  Write-Host "Manifest: $manifestPath"
  Write-Host "Assembly: $installedDllPath"
  Write-Host "SHA-256: $finalHash"
}
finally {
  if (Test-Path -LiteralPath $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
  }
  if (Test-Path -LiteralPath $manifestStagingPath) {
    Remove-Item -LiteralPath $manifestStagingPath -Force -ErrorAction SilentlyContinue
  }
}
