[CmdletBinding()]
param([string]$ReleaseId=('0.5.0-dev-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')))
$ErrorActionPreference='Stop'
$webRoot=Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Get-WebDotNet.ps1')
$sdk=Get-HifcWebDotNet
if($ReleaseId -notmatch '^0\.5\.0-dev-[0-9]{8}T[0-9]{6}Z$'){throw 'Invalid client release id.'}
$output=Join-Path $webRoot ('.local/client-'+$ReleaseId.Substring(10))
$package=Join-Path $output 'HIFC-0.5.0-Web'
New-Item -ItemType Directory -Path (Join-Path $package 'client') -Force|Out-Null
Push-Location $webRoot
try{
    if(((& git branch --show-current)-join '') -ne 'hifc-web'){throw 'Use the existing hifc-web branch.'}
    & $sdk build src/Hifc.Web.Revit/Hifc.Web.Revit.csproj -c Release --nologo -v minimal > (Join-Path $output 'build.log') 2>&1
    if($LASTEXITCODE -ne 0){Get-Content (Join-Path $output 'build.log') -Tail 20;throw 'Web client build failed.'}
    $names=@('Hifc.Web.Revit.dll','Hifc.Web.Client.dll','Hifc.Web.Contracts.dll','Newtonsoft.Json.dll','Hifc.Web.Update.exe','Hifc.Web.Update.exe.config')
    $files=@()
    foreach($name in $names){
        $source=Join-Path $webRoot ('src/Hifc.Web.Revit/bin/Release/net48/'+$name)
        if($name -notlike '*.config'){
            $assembly=if($PSVersionTable.PSEdition -eq 'Desktop'){[Reflection.Assembly]::ReflectionOnlyLoadFrom($source)}else{[Reflection.Assembly]::LoadFile($source)}
            $references=$assembly.GetReferencedAssemblies()
            if($references|Where-Object {$_.Name -match 'RuleEngine|Reference.Core|Hifc.Web.Server'}){throw 'Private server dependency found in client.'}
        }
        Copy-Item -LiteralPath $source -Destination (Join-Path $package ('client/'+$name))
        $files+=@{name=$name;sha256=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant();length=(Get-Item -LiteralPath $source).Length}
    }
    foreach($file in Get-ChildItem -LiteralPath (Join-Path $webRoot 'deploy/client') -File){
        $encoding=if($file.Extension -eq '.cmd'){New-Object Text.UTF8Encoding($false)}else{New-Object Text.UTF8Encoding($true)}
        $content=[IO.File]::ReadAllText($file.FullName) -replace '\r?\n',"`r`n"
        [IO.File]::WriteAllText((Join-Path $package $file.Name),$content,$encoding)
    }
    Copy-Item -LiteralPath (Join-Path $webRoot '.local/certificates/hifc-web-root-ca.crt') -Destination (Join-Path $package 'hifc-web-root-ca.crt')
    Add-Type -AssemblyName System.Net.Http
    $handler=New-Object Net.Http.HttpClientHandler; $handler.UseProxy=$false; $http=New-Object Net.Http.HttpClient($handler)
    try{$public=($http.GetStringAsync('https://192.168.6.45:8443/api/v1/compatibility').GetAwaiter().GetResult()|ConvertFrom-Json).signingKey}finally{$http.Dispose()}
    if([Convert]::FromBase64String($public.modulus).Length -lt 256){throw 'The zetta update signing key is unavailable.'}
    $public|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $package 'update-public-key.json') -Encoding utf8
    [ordered]@{version='0.5.0-Web';addInId='53ef39d3-a187-4fb5-90f8-29e91d747602';releaseId=$ReleaseId;sourceCommit=((& git rev-parse HEAD)-join '');createdUtc=[DateTime]::UtcNow.ToString('O');files=$files}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $package 'package.json') -Encoding utf8
    & (Join-Path $PSScriptRoot 'Test-WebInstallerEntry.ps1') -Package $package
    if($LASTEXITCODE -ne 0){throw 'Web installer validation failed.'}
    $archive=Join-Path $output 'HIFC-0.5.0-Web.zip'
    Compress-Archive -LiteralPath $package -DestinationPath $archive
    [ordered]@{archive=$archive;package=$package;sha256=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant();sourceCommit=((& git rev-parse HEAD)-join '')}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $webRoot '.local/latest-client-release.json') -Encoding utf8
    Get-Content -LiteralPath (Join-Path $webRoot '.local/latest-client-release.json')
}finally{Pop-Location}
