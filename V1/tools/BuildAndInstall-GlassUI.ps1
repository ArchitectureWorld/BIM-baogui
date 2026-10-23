[CmdletBinding()]
param([switch]$BuildOnly)
$ErrorActionPreference = 'Stop'
$sourceProperties = [xml](Get-Content -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) 'Directory.Build.props') -Raw)
if (-not $BuildOnly -and [string]$sourceProperties.Project.PropertyGroup.Version -match '-') {
    throw 'This source is a platform-sync preview. Automatic build-and-install is disabled. Use BuildOnly for preview builds; Install-Revit2020.ps1 installs the retained stable 0.4.16 package separately.'
}
$revitRoot = 'C:\Program Files\Autodesk\Revit 2020'
foreach ($dll in @('RevitAPI.dll', 'RevitAPIUI.dll', 'RevitAPIIFC.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $revitRoot $dll))) {
        throw "Revit 2020 reference missing: $dll. Run this on the machine where Revit 2020 is installed."
    }
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet SDK not found. Install the .NET SDK and .NET Framework 4.8 targeting pack first.'
}
$build = Join-Path $PSScriptRoot 'Build.ps1'
Write-Host '[H-IFC] Building source and running core/WPF tests...' -ForegroundColor Cyan
& $build
if (-not $?) { throw 'Build failed. Nothing was installed.' }
if ($BuildOnly) {
    Write-Host '[H-IFC] BuildOnly specified; nothing was installed.' -ForegroundColor Yellow
    exit 0
}
Write-Host '[H-IFC] Installing to Revit 2020 user Addins folder...' -ForegroundColor Cyan
& (Join-Path $PSScriptRoot 'Install-Revit2020.ps1')
if (-not $?) { throw 'Installation failed.' }
Write-Host '[H-IFC] Done. Open Revit 2020 and start H-IFC V1 > Model Management.' -ForegroundColor Green
