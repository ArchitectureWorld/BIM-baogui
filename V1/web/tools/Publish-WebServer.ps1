[CmdletBinding()]
param([string]$ReleaseId=('0.5.0-dev-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')))
$ErrorActionPreference='Stop'
if($ReleaseId -notmatch '^0\.5\.0-dev-[a-zA-Z0-9._-]+$'){throw 'Invalid Web release id.'}
$webRoot=Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Get-WebDotNet.ps1')
$sdk=Get-HifcWebDotNet
$releaseRoot=Join-Path $webRoot ('.local/releases/'+$ReleaseId)
if(Test-Path -LiteralPath $releaseRoot){throw 'Release output already exists.'}
$payload=Join-Path $releaseRoot 'payload'
New-Item -ItemType Directory -Path $payload -Force|Out-Null
Push-Location $webRoot
try {
    $branch=(& git branch --show-current)-join ''
    if($branch -ne 'hifc-web'){throw 'Publish is restricted to the existing hifc-web branch.'}
    $sourceCommit=(& git rev-parse HEAD)-join ''
    & (Join-Path $PSScriptRoot 'Publish-WebClient.ps1') -ReleaseId $ReleaseId|Out-Null
    $clientRelease=Get-Content -LiteralPath (Join-Path $webRoot '.local/latest-client-release.json')|ConvertFrom-Json
    if($clientRelease.sourceCommit -ne $sourceCommit){throw 'Client/server source revisions differ.'}
    foreach($entry in @(@('src/Hifc.Web.Server/Hifc.Web.Server.csproj','server'),@('tests/Hifc.Web.DatabaseTests/Hifc.Web.DatabaseTests.csproj','database-tests'),@('tests/Hifc.Web.Tests/Hifc.Web.Tests.csproj','web-tests'),@('tests/Hifc.Web.RuleRegression/Hifc.Web.RuleRegression.csproj','rule-tests'))){
        $log=Join-Path $releaseRoot ($entry[1]+'-publish.log')
        & $sdk publish $entry[0] -c Release -r linux-x64 --self-contained false -p:UseAppHost=false -o (Join-Path $payload $entry[1]) > $log 2>&1
        if($LASTEXITCODE -ne 0){Get-Content -LiteralPath $log -Tail 12;throw 'Linux server publish failed.'}
    }
    foreach($file in @('Dockerfile','compose.yaml','Caddyfile.template','postgres-init.sql','apply-ingress.sh','hifc-web-ingress.service','hifc-web.service','install-release.sh')){
        $content=[IO.File]::ReadAllText((Join-Path $webRoot ('deploy/'+$file))) -replace '\r\n',"`n"
        [IO.File]::WriteAllText((Join-Path $payload $file),$content,(New-Object Text.UTF8Encoding($false)))
    }
    New-Item -ItemType Directory -Path (Join-Path $payload 'server/downloads') -Force|Out-Null
    Copy-Item -LiteralPath $clientRelease.archive -Destination (Join-Path $payload 'server/downloads/HIFC-0.5.0-Web.zip')
    Copy-Item -LiteralPath (Join-Path $clientRelease.package 'package.json') -Destination (Join-Path $payload 'server/downloads/package.json')
    [ordered]@{releaseId=$ReleaseId;sourceCommit=$sourceCommit;branch=$branch;createdUtc=[DateTime]::UtcNow.ToString('O');stage='web-experience-candidate';authenticationConfigured=$true;clientPackageSha256=$clientRelease.sha256;experienceAcceptancePassed=$false}|
        ConvertTo-Json|Set-Content -LiteralPath (Join-Path $payload 'release.json') -Encoding utf8
    $archive=Join-Path $releaseRoot ($ReleaseId+'.tar.gz')
    & tar.exe -czf $archive -C $payload .
    if($LASTEXITCODE -ne 0){throw 'Web release packaging failed.'}
    [ordered]@{releaseId=$ReleaseId;archive=$archive;sha256=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant();sourceCommit=$sourceCommit}|
        ConvertTo-Json|Set-Content -LiteralPath (Join-Path $webRoot '.local/latest-server-release.json') -Encoding utf8
    Get-Content -LiteralPath (Join-Path $webRoot '.local/latest-server-release.json')
} finally {Pop-Location}
