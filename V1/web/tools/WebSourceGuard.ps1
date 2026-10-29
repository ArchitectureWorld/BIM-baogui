# Publishing builds the checkout, so a commit label is valid only while its
# tracked files and build-source additions still agree with that commit.
# Check all tracked files conservatively. Restrict untracked-file checks to V1
# and root build configuration so unrelated root .local files do not block a
# release. Git's normal ignore rules permit .local/bin/obj build outputs.
function Invoke-HifcSourceGit {
    param([string]$Repository,[string[]]$Arguments)
    $output=@(& git -C $Repository @Arguments)
    if($LASTEXITCODE -ne 0){throw ('Cannot verify release source with Git: '+($Arguments -join ' '))}
    return $output
}
function Assert-HifcSourceSnapshot {
    param([Parameter(Mandatory=$true)]$Snapshot)
    $root=$Snapshot.RepositoryRoot
    $head=(Invoke-HifcSourceGit $root @('rev-parse','HEAD'))-join ''
    if($head -cne $Snapshot.SourceCommit){throw 'HEAD changed during release construction; discard this candidate and build a new release.'}
    $branch=(Invoke-HifcSourceGit $root @('rev-parse','--abbrev-ref','HEAD'))-join ''
    if($branch -cne $Snapshot.Branch){throw 'The checked-out branch changed during release construction.'}
    # Porcelain reports index AND worktree changes, including index-only edits
    # whose working file has already been restored to the HEAD contents.
    $tracked=@(Invoke-HifcSourceGit $root @('status','--porcelain=v1','--untracked-files=no','--ignore-submodules=none'))
    if($tracked.Count){throw 'Tracked source differs from HEAD (staged or unstaged); commit the intended source before publishing.'}
    $sourcePaths=@('V1','.gitattributes','.gitignore','.editorconfig','global.json','NuGet.Config','nuget.config','NuGet.config','Directory.Build.props','Directory.Build.targets','Directory.Build.rsp','Directory.Packages.props','MSBuild.rsp')
    $untracked=@(Invoke-HifcSourceGit $root (@('ls-files','--others','--exclude-standard','--')+$sourcePaths))
    if($untracked.Count){throw 'Untracked V1 source or root build configuration exists; commit the intended source before publishing.'}
    $after=(Invoke-HifcSourceGit $root @('rev-parse','HEAD'))-join ''
    if($after -cne $Snapshot.SourceCommit){throw 'HEAD changed while release source was being verified.'}
}
function New-HifcSourceSnapshot {
    param([Parameter(Mandatory=$true)][string]$Repository,[string]$ExpectedCommit,[string]$RequiredBranch)
    $root=(Invoke-HifcSourceGit $Repository @('rev-parse','--show-toplevel'))-join ''
    $root=[IO.Path]::GetFullPath($root)
    $head=(Invoke-HifcSourceGit $root @('rev-parse','HEAD'))-join ''
    if($head -notmatch '^[a-f0-9]{40}$' -or ($ExpectedCommit -and $ExpectedCommit -cne $head)){throw 'The requested source commit must be the current committed HEAD.'}
    $branch=(Invoke-HifcSourceGit $root @('rev-parse','--abbrev-ref','HEAD'))-join ''
    if($RequiredBranch -and $branch -ne $RequiredBranch){throw ('Publishing requires the existing '+$RequiredBranch+' branch.')}
    $snapshot=[pscustomobject]@{RepositoryRoot=$root;SourceCommit=$head;Branch=$branch}
    Assert-HifcSourceSnapshot $snapshot
    return $snapshot
}
function Assert-HifcSourceDirectory {
    param([Parameter(Mandatory=$true)]$Snapshot,[Parameter(Mandatory=$true)][string]$Directory,[Parameter(Mandatory=$true)][string]$RelativePath)
    $expected=[IO.Path]::GetFullPath((Join-Path $Snapshot.RepositoryRoot $RelativePath)).TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)
    $actual=[IO.Path]::GetFullPath($Directory).TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)
    if(-not $actual.Equals($expected,[StringComparison]::OrdinalIgnoreCase)){throw 'Component source must come from the committed Web platform-sync directory.'}
    for($cursor=$actual;$cursor;$cursor=[IO.Path]::GetDirectoryName($cursor)){
        if((Test-Path -LiteralPath $cursor) -and (([IO.File]::GetAttributes($cursor) -band [IO.FileAttributes]::ReparsePoint) -ne 0)){throw 'Committed component source cannot use redirected directories.'}
    }
}
