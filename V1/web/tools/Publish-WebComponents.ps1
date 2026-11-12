[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ReleaseId,
    [string]$SourceCommit,
    [string]$NodePath='C:\Program Files\nodejs\node.exe',
    [string]$NodeLicensePath,
    [string]$SyncSourceDirectory,
    [string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$webRoot=Split-Path -Parent $PSScriptRoot
$repository=Split-Path -Parent (Split-Path -Parent $webRoot)
. (Join-Path $PSScriptRoot 'WebSourceGuard.ps1')
$sourceSnapshot=New-HifcSourceSnapshot -Repository $repository -ExpectedCommit $SourceCommit -RequiredBranch 'hifc-web'
$SourceCommit=$sourceSnapshot.SourceCommit
if($ReleaseId -notmatch '^0\.5\.0-dev-[0-9]{8}T[0-9]{6}Z$' -or $SourceCommit -notmatch '^[a-f0-9]{40}$'){throw 'Invalid main release/source binding.'}
if(!$SyncSourceDirectory){$SyncSourceDirectory=Join-Path $webRoot 'platform-sync'}
$SyncSourceDirectory=[IO.Path]::GetFullPath($SyncSourceDirectory)
Assert-HifcSourceDirectory -Snapshot $sourceSnapshot -Directory $SyncSourceDirectory -RelativePath 'V1/web/platform-sync'
if(!$OutputDirectory){$OutputDirectory=Join-Path $webRoot ('.local/components-'+$ReleaseId.Substring(10))}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
$allowed=[IO.Path]::GetFullPath((Join-Path $webRoot '.local'))+[IO.Path]::DirectorySeparatorChar
if(!$OutputDirectory.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){throw 'Component build output must remain in V1/web/.local.'}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Use a fresh output directory; published component versions are immutable.'}
for($parent=$OutputDirectory; $parent; $parent=[IO.Path]::GetDirectoryName($parent)){
    if((Test-Path -LiteralPath $parent) -and (([IO.File]::GetAttributes($parent) -band [IO.FileAttributes]::ReparsePoint) -ne 0)){throw 'Component build output cannot use redirected directories.'}
}
New-Item -ItemType Directory -Path (Join-Path $webRoot '.local') -Force | Out-Null
$signature=Get-AuthenticodeSignature -LiteralPath $NodePath
if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(?:^|, )O=OpenJS Foundation(?:,|$)'){throw 'Node must carry a valid OpenJS Foundation Authenticode signature.'}
$pe=[IO.File]::OpenRead($NodePath)
try{$reader=[IO.BinaryReader]::new($pe);$pe.Position=0x3c;$pe.Position=$reader.ReadInt32();if($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x8664){throw 'Node PE must target Windows x64.'}}finally{$pe.Dispose()}
$nodeVersion=((& $NodePath --version)-join '').Trim()
if($LASTEXITCODE -ne 0 -or $nodeVersion -ne 'v25.4.0'){throw 'Node 25.4.0 is required; global Node will not be changed.'}
if(!$NodeLicensePath){
    $NodeLicensePath=Join-Path $webRoot '.local/node-v25.4.0-LICENSE'
    if(!(Test-Path -LiteralPath $NodeLicensePath)){Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/nodejs/node/v25.4.0/LICENSE' -OutFile $NodeLicensePath -TimeoutSec 30}
}
if((Get-Item -LiteralPath $NodeLicensePath).Length -lt 1000 -or !(Select-String -LiteralPath $NodeLicensePath -Pattern 'Node.js' -Quiet)){throw 'Node distribution license is missing.'}
$npm=Join-Path (Split-Path -Parent $NodePath) 'node_modules/npm/bin/npm-cli.js'
if(!(Test-Path -LiteralPath $npm)){throw 'Build-machine npm CLI was not found beside the verified Node runtime.'}
$sourcePackage=Get-Content -LiteralPath (Join-Path $SyncSourceDirectory 'package.json') -Raw | ConvertFrom-Json
if($sourcePackage.version -ne '0.2.1-web.1' -or $sourcePackage.name -ne 'hifc-web-platform-sync'){throw 'Only the authorized Web 0.2.1-web.1 CLI can be published as a Web component.'}
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
Add-Type -AssemblyName System.IO.Compression
function Write-Json([string]$Path,$Value){[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 12)+"`n",(New-Object Text.UTF8Encoding($false)))}
function Hash([string]$Path){return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Copy-Tree([string]$Source,[string]$Destination){
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach($item in Get-ChildItem -LiteralPath $Source -Recurse -Force){
        if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Source links are forbidden in component packages.'}
        $relative=$item.FullName.Substring($Source.Length).TrimStart('\','/')
        $target=Join-Path $Destination $relative
        if($item.PSIsContainer){New-Item -ItemType Directory -Path $target -Force | Out-Null}else{Copy-Item -LiteralPath $item.FullName -Destination $target}
    }
}
function Zip-Reproducible([string]$Source,[string]$Archive){
    $files=@(Get-ChildItem -LiteralPath $Source -File -Recurse -Force)
    $names=[string[]]@($files|ForEach-Object {$_.FullName.Substring($Source.Length+1).Replace('\','/')})
    [Array]::Sort($names,[StringComparer]::Ordinal)
    $output=[IO.File]::Open($Archive,[IO.FileMode]::CreateNew)
    try{
        $zip=[IO.Compression.ZipArchive]::new($output,[IO.Compression.ZipArchiveMode]::Create,$true,[Text.Encoding]::UTF8)
        try{foreach($name in $names){
            $entry=$zip.CreateEntry($name,[IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime=[DateTimeOffset]::new(2000,1,1,0,0,0,[TimeSpan]::Zero)
            $entry.ExternalAttributes=0
            $input=[IO.File]::OpenRead((Join-Path $Source $name));$destination=$entry.Open()
            try{$input.CopyTo($destination)}finally{$destination.Dispose();$input.Dispose()}
        }}finally{$zip.Dispose()}
    }finally{$output.Dispose()}
}
$build=Join-Path $OutputDirectory 'build'
New-Item -ItemType Directory -Path $build | Out-Null
foreach($name in @('package.json','package-lock.json','tsconfig.json')){Copy-Item -LiteralPath (Join-Path $SyncSourceDirectory $name) -Destination (Join-Path $build $name)}
Copy-Tree (Join-Path $SyncSourceDirectory 'src') (Join-Path $build 'src')
$sourceHashes=@(Get-ChildItem -LiteralPath (Join-Path $build 'src') -File -Recurse | Sort-Object FullName | ForEach-Object {[ordered]@{path=$_.FullName.Substring($build.Length+1).Replace('\','/');sha256=Hash $_.FullName}})
$lockHash=Hash (Join-Path $build 'package-lock.json')
$priorBrowserDownload=$env:PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD
try{
    $env:PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD='1'
    Push-Location $build
    try{
        & $NodePath $npm ci --ignore-scripts --no-audit --no-fund > (Join-Path $OutputDirectory 'npm-ci.log') 2>&1
        if($LASTEXITCODE -ne 0){throw 'Locked build-machine npm ci failed; see npm-ci.log.'}
        & $NodePath (Join-Path $build 'node_modules/typescript/bin/tsc') -p (Join-Path $build 'tsconfig.json') > (Join-Path $OutputDirectory 'typescript.log') 2>&1
        if($LASTEXITCODE -ne 0){throw 'Platform synchronization TypeScript build failed; see typescript.log.'}
        & $NodePath $npm prune --omit=dev --ignore-scripts --no-audit --no-fund > (Join-Path $OutputDirectory 'npm-prune.log') 2>&1
        if($LASTEXITCODE -ne 0){throw 'Production dependency pruning failed; see npm-prune.log.'}
    }finally{Pop-Location}
}finally{$env:PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=$priorBrowserDownload}
if((Test-Path -LiteralPath (Join-Path $build 'node_modules/typescript')) -or (Test-Path -LiteralPath (Join-Path $build 'node_modules/@types/node'))){throw 'Development dependencies remain in runtime.'}
$nodeStage=Join-Path $OutputDirectory 'node-runtime';New-Item -ItemType Directory -Path $nodeStage | Out-Null
Copy-Item -LiteralPath $NodePath -Destination (Join-Path $nodeStage 'node.exe')
Copy-Item -LiteralPath $NodeLicensePath -Destination (Join-Path $nodeStage 'LICENSE')
Write-Json (Join-Path $nodeStage 'provenance.json') ([ordered]@{component='node';version='25.4.0';runtime='windows-x64';source='Installed OpenJS signed distribution';distributionUrl='https://nodejs.org/dist/v25.4.0/win-x64/node.exe';sha256=Hash $NodePath;authenticodeStatus=$signature.Status.ToString();signer=$signature.SignerCertificate.Subject;signerThumbprint=$signature.SignerCertificate.Thumbprint;licenseSha256=Hash $NodeLicensePath;licenseUrl='https://raw.githubusercontent.com/nodejs/node/v25.4.0/LICENSE'})
$syncStage=Join-Path $OutputDirectory 'platform-sync-runtime';New-Item -ItemType Directory -Path $syncStage | Out-Null
Copy-Tree (Join-Path $build 'dist/src') (Join-Path $syncStage 'dist/src')
Copy-Tree (Join-Path $build 'node_modules') (Join-Path $syncStage 'node_modules')
Write-Json (Join-Path $syncStage 'package.json') ([ordered]@{name='hifc-web-platform-sync';version='0.2.1-web.1';private=$true;type='module';engines=@{node='25.4.0'};dependencies=$sourcePackage.dependencies})
Write-Json (Join-Path $syncStage 'provenance.json') ([ordered]@{component='platform-sync';version='0.2.1-web.1';sourceVersion=$sourcePackage.version;mainSourceCommit=$SourceCommit;packageLockSha256=$lockHash;build='npm ci --ignore-scripts; tsc; npm prune --omit=dev --ignore-scripts';sources=$sourceHashes})
$help=((& (Join-Path $nodeStage 'node.exe') (Join-Path $syncStage 'dist/src/cli.js') help) -join '').Trim()
if($LASTEXITCODE -ne 0){throw 'Packaged runtime CLI probe failed.'}
$helpResult=$help|ConvertFrom-Json
if($helpResult.version -ne '0.2.1-web.1' -or !$helpResult.commands -or !$helpResult.launch_authorization){throw 'Packaged runtime CLI did not report Web launch authorization/version.'}
$locks=@()
foreach($item in @(@{id='node';version='25.4.0';stage=$nodeStage;entry='node.exe'},@{id='platform-sync';version='0.2.1-web.1';stage=$syncStage;entry='dist/src/cli.js'})){
    $published=Join-Path $OutputDirectory ('components/'+$ReleaseId+'/'+$item.id+'/'+$item.version)
    New-Item -ItemType Directory -Path $published -Force | Out-Null
    $archive=Join-Path $published 'archive.zip';Zip-Reproducible $item.stage $archive
    # Re-zip into an independent file to prove stable ordering, timestamps and bytes.
    $repeat=Join-Path $OutputDirectory ($item.id+'-repro.zip');Zip-Reproducible $item.stage $repeat
    $hash=Hash $archive
    if($hash -ne (Hash $repeat)){throw 'Component archive was not reproducible.'}
    $lock=[ordered]@{mainReleaseId=$ReleaseId;mainSourceCommit=$SourceCommit;componentId=$item.id;version=$item.version;runtimeIdentifier='windows-x64';protocolMajor=3;archiveSha256=$hash;archiveLength=(Get-Item -LiteralPath $archive).Length;entryPoint=$item.entry}
    if($lock.archiveLength -gt 256MB){throw 'Component archive exceeds client safety limit.'}
    Write-Json (Join-Path $published 'lock.json') $lock
    $locks+=@($lock)
}
Assert-HifcSourceSnapshot $sourceSnapshot
$lockFile=Join-Path $OutputDirectory 'component-locks.json';Write-Json $lockFile $locks
Write-Json (Join-Path $OutputDirectory 'verification.json') ([ordered]@{nodeVersion=$nodeVersion;nodeAuthenticode=$signature.Status.ToString();cli=$helpResult;reproducibleArchives=$true;clientInstallScripts=$false;componentLockFile=$lockFile;componentDirectory=(Join-Path $OutputDirectory 'components')})
Get-Content -LiteralPath (Join-Path $OutputDirectory 'verification.json')
