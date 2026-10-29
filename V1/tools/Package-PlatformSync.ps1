$ErrorActionPreference='Stop'
$workspace=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$source=Join-Path $workspace 'V1\platform-sync'
$package=Join-Path $workspace 'V1\dist\Hifc.PlatformSync-0.2.0'
if(Test-Path -LiteralPath $package){throw 'Release package already exists; inspect it before reusing.'}
New-Item -ItemType Directory -Path (Join-Path $package 'dist') -Force | Out-Null
foreach($name in @('package.json','package-lock.json')){Copy-Item -LiteralPath (Join-Path $source $name) -Destination $package}
Copy-Item -LiteralPath (Join-Path $source 'dist\src') -Destination (Join-Path $package 'dist\src') -Recurse
Push-Location -LiteralPath $package
try{& npm.cmd ci --omit=dev --ignore-scripts --no-audit --no-fund; if($LASTEXITCODE -ne 0){throw 'Runtime dependency install failed.'}}finally{Pop-Location}
$files=@(Get-ChildItem -LiteralPath $package -File -Recurse | ForEach-Object {
 @{name=$_.FullName.Substring($package.Length+1).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
})
@{version='0.2.0';minimum_node='24';files=$files} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $package 'build-manifest.json') -Encoding utf8
& 'C:\Program Files\nodejs\node.exe' (Join-Path $package 'dist\src\cli.js') help
if($LASTEXITCODE -ne 0){throw 'Packaged CLI did not start.'}
Write-Output ('PACKAGED '+$files.Count+' files')
