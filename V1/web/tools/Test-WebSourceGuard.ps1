[CmdletBinding()]
param([string]$HelperPath='')
$ErrorActionPreference='Stop'
$webRoot=Split-Path -Parent $PSScriptRoot
$helper=if($HelperPath){$HelperPath}else{Join-Path $PSScriptRoot 'WebSourceGuard.ps1'}
if(Test-Path -LiteralPath $helper){. $helper}
if(-not(Get-Command New-HifcSourceSnapshot -ErrorAction SilentlyContinue)){throw 'Web source guard is missing.'}
$run=Join-Path $webRoot ('.local/source-guard-'+[Guid]::NewGuid().ToString('N'))
$fixture=Join-Path $run 'repository'
New-Item -ItemType Directory -Path (Join-Path $fixture 'V1/web/platform-sync/src') -Force|Out-Null
function Invoke-FixtureGit([string[]]$Arguments){$output=@(& git -C $fixture @Arguments);if($LASTEXITCODE -ne 0){throw ('Fixture Git failed: '+($Arguments -join ' '))};return $output}
function Save([string]$Relative,[string]$Value){$path=Join-Path $fixture $Relative;New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force|Out-Null;[IO.File]::WriteAllText($path,$Value,[Text.UTF8Encoding]::new($false))}
function Reject([scriptblock]$Action,[string]$Label){$rejected=$false;try{& $Action|Out-Null}catch{$rejected=$true};if(-not $rejected){throw ('Unsafe source accepted: '+$Label)};Write-Output ('PASS '+$Label)}
[void](Invoke-FixtureGit @('init','--quiet'))
[void](Invoke-FixtureGit @('symbolic-ref','HEAD','refs/heads/HIFC-Web'))
[void](Invoke-FixtureGit @('config','core.autocrlf','false'))
Save '.gitignore' "V1/**/.local/`n**/bin/`n**/obj/`n"
Save 'V1/web/platform-sync/src/main.ts' 'export const value=1;'
Save 'tracked-outside-V1.txt' 'tracked baseline'
[void](Invoke-FixtureGit @('add','--all'))
[void](Invoke-FixtureGit @('-c','user.name=SourceGuard Fixture','-c','user.email=source-guard@example.invalid','commit','--quiet','--no-gpg-sign','-m','fixture baseline'))
$baseline=(Invoke-FixtureGit @('rev-parse','HEAD'))-join ''
$snapshot=New-HifcSourceSnapshot -Repository $fixture -RequiredBranch 'hifc-web'
Assert-HifcSourceSnapshot $snapshot
Write-Output 'PASS clean committed source'
Save 'V1/web/.local/build/output.log' 'ignored build output'
Save 'V1/web/src/App/bin/Release/generated.dll' 'ignored binary'
Save 'V1/web/src/App/obj/generated.cs' 'ignored generated source'
Save '.local/existing-unrelated.txt' 'unrelated root temporary file'
Assert-HifcSourceSnapshot $snapshot
Write-Output 'PASS ignored build outputs and unrelated root temporary files'
Save 'V1/web/platform-sync/src/main.ts' 'export const value=2;'
Reject {New-HifcSourceSnapshot -Repository $fixture} 'unstaged source is rejected before build'
[void](Invoke-FixtureGit @('add','V1/web/platform-sync/src/main.ts'))
Reject {New-HifcSourceSnapshot -Repository $fixture} 'staged source is rejected before build'
# An index-only change must also fail when the working file again matches HEAD.
Save 'V1/web/platform-sync/src/main.ts' 'export const value=1;'
Reject {New-HifcSourceSnapshot -Repository $fixture} 'index differs while working file matches HEAD'
[void](Invoke-FixtureGit @('reset','--quiet','HEAD','--','V1/web/platform-sync/src/main.ts'))
Save 'V1/web/platform-sync/src/new.ts' 'export const untracked=true;'
Reject {New-HifcSourceSnapshot -Repository $fixture} 'untracked V1 source is rejected'
Remove-Item -LiteralPath (Join-Path $fixture 'V1/web/platform-sync/src/new.ts')
Save 'Directory.Build.targets' '<Project />'
Reject {New-HifcSourceSnapshot -Repository $fixture} 'untracked root build configuration is rejected'
Remove-Item -LiteralPath (Join-Path $fixture 'Directory.Build.targets')
Save 'tracked-outside-V1.txt' 'changed tracked input'
Reject {Assert-HifcSourceSnapshot $snapshot} 'tracked changes outside V1 are conservatively rejected'
Save 'tracked-outside-V1.txt' 'tracked baseline'
Save 'V1/web/platform-sync/src/main.ts' 'export const value=3;'
Reject {Assert-HifcSourceSnapshot $snapshot} 'source changed during build is rejected'
[void](Invoke-FixtureGit @('add','V1/web/platform-sync/src/main.ts'))
[void](Invoke-FixtureGit @('-c','user.name=SourceGuard Fixture','-c','user.email=source-guard@example.invalid','commit','--quiet','--no-gpg-sign','-m','fixture changed during build'))
Reject {Assert-HifcSourceSnapshot $snapshot} 'HEAD changed during build is rejected even when clean'
Reject {New-HifcSourceSnapshot -Repository $fixture -ExpectedCommit $baseline} 'explicit component source commit must equal HEAD'
$current=New-HifcSourceSnapshot -Repository $fixture
Assert-HifcSourceDirectory -Snapshot $current -Directory (Join-Path $fixture 'V1/web/platform-sync') -RelativePath 'V1/web/platform-sync'
Reject {Assert-HifcSourceDirectory -Snapshot $current -Directory $run -RelativePath 'V1/web/platform-sync'} 'external component source directory is rejected'
Assert-HifcSourceSnapshot $current
Write-Output ('PASS source guard fixture; '+$run)
