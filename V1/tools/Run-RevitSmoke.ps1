[CmdletBinding()]
param([int]$TimeoutSeconds=480)
$ErrorActionPreference = 'Stop'
if (Get-Process -Name Revit -ErrorAction SilentlyContinue) { throw 'Smoke needs a fresh Revit process; existing processes are left intact.' }
$workspace = Split-Path -Parent $PSScriptRoot
$run = Join-Path $workspace ('validation\revit-' + (Get-Date -Format yyyyMMdd-HHmmss) + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $run -Force | Out-Null
$report = Join-Path $run 'host-smoke.json'
$oldOutput=$env:HIFC_V1_SMOKE_OUTPUT; $oldExit=$env:HIFC_V1_SMOKE_EXIT; $oldWorkspace=$env:HIFC_V1_WORKSPACE
try {
    $env:HIFC_V1_SMOKE_OUTPUT=$report; $env:HIFC_V1_SMOKE_EXIT='1'; $env:HIFC_V1_WORKSPACE=$workspace
    $revitProcess=Start-Process -FilePath 'C:\Program Files\Autodesk\Revit 2020\Revit.exe' -WindowStyle Hidden -PassThru
}
finally {
    $env:HIFC_V1_SMOKE_OUTPUT=$oldOutput; $env:HIFC_V1_SMOKE_EXIT=$oldExit; $env:HIFC_V1_WORKSPACE=$oldWorkspace
}
@{ pid=$revitProcess.Id; report=$report; scope='Fresh unsaved background project only' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'launch.json') -Encoding utf8
Write-Output "Revit PID: $($revitProcess.Id)"
Write-Output "Report: $report"
$timer=[Diagnostics.Stopwatch]::StartNew()
while ($timer.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
    if (Test-Path -LiteralPath $report) { break }
    if ($revitProcess.WaitForExit(1000)) { break }
}
if (-not (Test-Path -LiteralPath $report) -and -not $revitProcess.HasExited) { throw "Smoke timed out. Revit is left running for diagnosis; no model or process was forcibly closed. Report: $report" }
if (-not (Test-Path -LiteralPath $report)) { throw "Revit exited without smoke report: $report" }
$result=Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
if (-not $result.allPassed) { Write-Output ($result | ConvertTo-Json -Depth 10); throw "Host smoke failed: $report" }
Write-Output (@{ allPassed=$result.allPassed; assembly_sha256=$result.assembly_sha256; source_snapshot_before=$result.export.source_snapshot_before; source_snapshot_after=$result.export.source_snapshot_after; temporary_parameters_rolled_back=$result.temporary_parameters_rolled_back } | ConvertTo-Json)
Write-Output "Host smoke complete: $run"
if (-not $revitProcess.HasExited) { Write-Output "Revit PID $($revitProcess.Id) is still running; no process was forcibly closed." }
