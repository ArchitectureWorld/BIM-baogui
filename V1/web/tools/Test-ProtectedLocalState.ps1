[CmdletBinding()]
param([string]$Baseline='')
$ErrorActionPreference='Stop'
$webRoot=Split-Path -Parent $PSScriptRoot
if(-not $Baseline){$Baseline=Join-Path $webRoot '.local\baseline.json'}
if(-not(Test-Path -LiteralPath $Baseline)){throw 'No baseline exists; unchanged Local state cannot be asserted.'}
$baselineState=Get-Content -LiteralPath $Baseline -Raw | ConvertFrom-Json
$failures=[Collections.Generic.List[string]]::new()
foreach($file in @($baselineState.installedFiles)+@($baselineState.sourceModels)){
    if(-not(Test-Path -LiteralPath $file.path)){$failures.Add('Missing protected file: '+$file.path);continue}
    if((Get-FileHash -LiteralPath $file.path -Algorithm SHA256).Hash -ne $file.sha256){$failures.Add('Protected hash changed: '+$file.path)}
}
$repoRoot=[IO.Path]::GetFullPath((Join-Path $webRoot '..\..'))
$protected=@('V1/src','V1/tools','V1/reference','V1/Directory.Build.props','.github/workflows/hifc-v1.yml')
& git -C $repoRoot diff --quiet $baselineState.mainHead -- @protected
if($LASTEXITCODE -ne 0){$failures.Add('Existing Local source, reference, build or CI differs from the baseline.')}
$result=[ordered]@{checkedUtc=[DateTime]::UtcNow.ToString('O');installedFiles=@($baselineState.installedFiles).Count;sourceModels=@($baselineState.sourceModels).Count;existingLocalSourceUnchanged=($failures.Count -eq 0);failures=@($failures)}
$result|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $webRoot '.local\protected-state.json') -Encoding utf8
$result|ConvertTo-Json -Depth 5
if($failures.Count -gt 0){throw 'Protected Local state verification failed.'}
