function Get-HifcWebDotNet {
    [CmdletBinding()]
    param()
    $ErrorActionPreference='Stop'
    $sdkDirectory=Join-Path $env:USERPROFILE '.dotnet-hifc'
    $sdk=Join-Path $sdkDirectory 'dotnet.exe'
    if(Test-Path -LiteralPath $sdk){
        $versions=& $sdk --list-sdks
        if($LASTEXITCODE -eq 0 -and ($versions | Where-Object { $_ -match '^10\.0\.402\s' })){return $sdk}
    }
    $webRoot=Split-Path -Parent $PSScriptRoot
    $cache=Join-Path $webRoot '.local\tools'
    New-Item -ItemType Directory -Path $cache -Force | Out-Null
    $installer=Join-Path $cache 'dotnet-install.ps1'
    Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
    & $installer -Version '10.0.402' -InstallDir $sdkDirectory -NoPath | Out-Host
    if(-not(Test-Path -LiteralPath $sdk)){throw 'The dedicated Web SDK installation did not produce dotnet.exe.'}
    $version=& $sdk --version
    if($LASTEXITCODE -ne 0 -or $version -ne '10.0.402'){throw 'Dedicated Web SDK version verification failed.'}
    return $sdk
}
