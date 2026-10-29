[CmdletBinding()]
param([switch]$Smoke,[string]$NodePath,[string]$EvidenceDirectory)
$ErrorActionPreference='Stop'
$syncRoot=$PSScriptRoot
$v1Root=Split-Path -Parent $syncRoot
if(-not $NodePath){
    $bundled=Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe'
    $NodePath=if(Test-Path -LiteralPath $bundled){$bundled}else{(Get-Command node -ErrorAction Stop).Source}
}
if(-not $EvidenceDirectory){$EvidenceDirectory=Join-Path $syncRoot ('.runtime\demo-'+(Get-Date -Format yyyyMMdd-HHmmss))}
$EvidenceDirectory=[IO.Path]::GetFullPath($EvidenceDirectory)
$previewProject=Join-Path $v1Root 'tools\Hifc.PlatformSync.Preview\Hifc.PlatformSync.Preview.csproj'
Push-Location -LiteralPath $syncRoot
try {
    if(-not(Test-Path -LiteralPath 'node_modules\typescript\bin\tsc')){
        & npm.cmd ci --ignore-scripts --no-audit --no-fund
        if($LASTEXITCODE -ne 0){throw 'Local worker dependency restore failed.'}
    }
    & $NodePath 'node_modules\typescript\bin\tsc' -p .
    if($LASTEXITCODE -ne 0){throw 'Local worker build failed.'}
    & dotnet build $previewProject -c Release --nologo -v minimal
    if($LASTEXITCODE -ne 0){throw 'Standalone preview build failed.'}
    $preview=Join-Path (Split-Path -Parent $previewProject) 'bin\Release\net48\Hifc.PlatformSync.Preview.exe'
    $arguments=@('dist\src\demo.js','--preview',$preview,'--evidence',$EvidenceDirectory)
    if($Smoke){$arguments+='--smoke'}
    & $NodePath @arguments
    if($LASTEXITCODE -ne 0){throw 'Local preview did not complete successfully.'}
} finally {Pop-Location}
