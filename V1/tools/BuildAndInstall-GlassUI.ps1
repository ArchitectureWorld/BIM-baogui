[CmdletBinding()]
param([switch]$BuildOnly)
$ErrorActionPreference = 'Stop'
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
