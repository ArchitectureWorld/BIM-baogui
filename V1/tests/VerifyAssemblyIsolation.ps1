[CmdletBinding()]
param(
    [string]$PluginPath,
    [string]$OutputPath
)
$ErrorActionPreference = 'Stop'
# Run in Windows PowerShell (.NET Framework), the same CLR family as Revit 2020.
$addinRoot = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Autodesk\Revit\Addins\2020'
$otherPath = Join-Path $addinRoot 'H-IFC\Hifc.Revit.dll'
$other = [Reflection.Assembly]::LoadFrom($otherPath)
$actual = [Reflection.Assembly]::LoadFrom($PluginPath)
$expected = [Reflection.AssemblyName]::GetAssemblyName($PluginPath)
$result = [ordered]@{
    expected_identity = $expected.FullName
    other_identity = $other.FullName
    actual_identity = $actual.FullName
    actual_location = $actual.Location
    expected_location = [IO.Path]::GetFullPath($PluginPath)
    shared_assembly_instance = [object]::ReferenceEquals($other, $actual)
    allPassed = (-not [object]::ReferenceEquals($other, $actual)) -and ($actual.Location -eq [IO.Path]::GetFullPath($PluginPath))
}
$result | ConvertTo-Json | Set-Content -LiteralPath $OutputPath -Encoding UTF8
$result | ConvertTo-Json
if (-not $result.allPassed) { exit 1 }
