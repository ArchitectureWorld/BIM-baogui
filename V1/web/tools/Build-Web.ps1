[CmdletBinding()]
param([switch]$SkipNative)
$ErrorActionPreference='Stop'
$webRoot=Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Get-WebDotNet.ps1')
$sdk=Get-HifcWebDotNet
$logs=Join-Path $webRoot '.local\build'
New-Item -ItemType Directory -Path $logs -Force | Out-Null
function Invoke-WebCheck([string]$Name,[string[]]$Arguments){
    $log=Join-Path $logs ($Name+'.log')
    & $sdk @Arguments > $log 2>&1
    if($LASTEXITCODE -ne 0){Get-Content -LiteralPath $log -Tail 30;throw ($Name+' failed; see '+$log)}
    Write-Output ($Name+': '+(Get-Content -LiteralPath $log -Tail 1))
}
Push-Location $webRoot
try {
    Invoke-WebCheck 'web-tests' @('run','--project','tests/Hifc.Web.Tests/Hifc.Web.Tests.csproj','-c','Release')
    Invoke-WebCheck 'existing-rule-regression' @('run','--project','tests/Hifc.Web.RuleRegression/Hifc.Web.RuleRegression.csproj','-c','Release')
    $nativeBuilt=$false
    if(-not $SkipNative -and (Test-Path -LiteralPath 'C:\Program Files\Autodesk\Revit 2020\RevitAPI.dll')){
        Invoke-WebCheck 'native-test-build' @('build','tests/Hifc.Web.NativeTests/Hifc.Web.NativeTests.csproj','-c','Release','--nologo','-v','minimal')
        $nativeBuilt=$true
    }
    if(Test-Path -LiteralPath (Join-Path $webRoot '.local\baseline.json')){& (Join-Path $PSScriptRoot 'Test-ProtectedLocalState.ps1')}
    & git diff --check
    if($LASTEXITCODE -ne 0){throw 'Whitespace validation failed.'}
    @{verifiedUtc=[DateTime]::UtcNow.ToString('O');sdk='10.0.402';webTests='passed';existingRuleRegression='passed';nativeHarnessBuilt=$nativeBuilt;nativeHostVerified=$false;linuxEndToEndVerified=$false;status='development; experience acceptance pending'}|
        ConvertTo-Json|Set-Content -LiteralPath (Join-Path $logs 'result.json') -Encoding utf8
} finally {Pop-Location}
