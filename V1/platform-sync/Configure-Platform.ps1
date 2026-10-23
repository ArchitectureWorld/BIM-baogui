[CmdletBinding()]
param([string]$ConfigPath=(Join-Path $PSScriptRoot 'config.local.json'),[string]$NodePath)
$ErrorActionPreference='Stop'
if(-not $NodePath){
    $bundled=Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe'
    $NodePath=if(Test-Path -LiteralPath $bundled){$bundled}else{(Get-Command node -ErrorAction Stop).Source}
}
$entry=Join-Path $PSScriptRoot 'dist\src\cli.js'
if(-not(Test-Path -LiteralPath $entry)){throw 'Run npm ci and npm run build in platform-sync first.'}
Write-Host 'Platform sync preview: real platform is read-only. Paste the full platform entry URL; input is hidden.'
$secure=Read-Host 'Platform entry URL' -AsSecureString
$pointer=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try {
    $value=[Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    $value | & $NodePath $entry setup --config $ConfigPath --credential-stdin
    if($LASTEXITCODE -ne 0){throw 'Configuration failed. No platform changes were made.'}
} finally {
    $value=$null
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
}
