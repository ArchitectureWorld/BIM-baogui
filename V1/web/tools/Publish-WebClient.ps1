[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$webRoot=Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Get-WebDotNet.ps1')
$sdk=Get-HifcWebDotNet
$output=Join-Path $webRoot ('.local/client-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ'))
$package=Join-Path $output 'HIFC-0.5.0-Web'
New-Item -ItemType Directory -Path (Join-Path $package 'client') -Force|Out-Null
Push-Location $webRoot
try{
    if(((& git branch --show-current)-join '') -ne 'hifc-web'){throw 'Use the existing hifc-web branch.'}
    & $sdk build src/Hifc.Web.Revit/Hifc.Web.Revit.csproj -c Release --nologo -v minimal > (Join-Path $output 'build.log') 2>&1
    if($LASTEXITCODE -ne 0){Get-Content (Join-Path $output 'build.log') -Tail 20;throw 'Web client build failed.'}
    $names=@('Hifc.Web.Revit.dll','Hifc.Web.Client.dll','Hifc.Web.Contracts.dll','Newtonsoft.Json.dll')
    $files=@()
    foreach($name in $names){
        $source=Join-Path $webRoot ('src/Hifc.Web.Revit/bin/Release/net48/'+$name)
        $assembly=if($PSVersionTable.PSEdition -eq 'Desktop'){[Reflection.Assembly]::ReflectionOnlyLoadFrom($source)}else{[Reflection.Assembly]::LoadFile($source)}
        $references=$assembly.GetReferencedAssemblies()
        if($references|Where-Object {$_.Name -match 'RuleEngine|Reference.Core|Hifc.Web.Server'}){throw 'Private server dependency found in client.'}
        Copy-Item -LiteralPath $source -Destination (Join-Path $package ('client/'+$name))
        $files+=@{name=$name;sha256=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()}
    }
    foreach($file in Get-ChildItem -LiteralPath (Join-Path $webRoot 'deploy/client') -File){
        $encoding=if($file.Extension -eq '.cmd'){New-Object Text.UTF8Encoding($false)}else{New-Object Text.UTF8Encoding($true)}
        $content=[IO.File]::ReadAllText($file.FullName) -replace '\r?\n',"`r`n"
        [IO.File]::WriteAllText((Join-Path $package $file.Name),$content,$encoding)
    }
    Copy-Item -LiteralPath (Join-Path $webRoot '.local/certificates/hifc-web-root-ca.crt') -Destination (Join-Path $package 'hifc-web-root-ca.crt')
    [ordered]@{version='0.5.0-Web';addInId='53ef39d3-a187-4fb5-90f8-29e91d747602';sourceCommit=((& git rev-parse HEAD)-join '');createdUtc=[DateTime]::UtcNow.ToString('O');files=$files}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $package 'package.json') -Encoding utf8
    & powershell.exe -NoProfile -File (Join-Path $package 'Install-Web.ps1') -ValidateOnly
    if($LASTEXITCODE -ne 0){throw 'Web installer validation failed.'}
    $archive=Join-Path $output 'HIFC-0.5.0-Web.zip'
    Compress-Archive -LiteralPath $package -DestinationPath $archive
    [ordered]@{archive=$archive;package=$package;sha256=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant();sourceCommit=((& git rev-parse HEAD)-join '')}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $webRoot '.local/latest-client-release.json') -Encoding utf8
    Get-Content -LiteralPath (Join-Path $webRoot '.local/latest-client-release.json')
}finally{Pop-Location}
