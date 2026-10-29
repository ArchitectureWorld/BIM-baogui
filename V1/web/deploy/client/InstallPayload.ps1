function Resolve-HifcPayloadPath {
    param([string]$Root,[string]$Path)
    $rootPath=[IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $value=[IO.Path]::GetFullPath($Path)
    if(-not $value.StartsWith($rootPath+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw '安装文件路径超出 Web 目录。'}
    $cursor=$value
    while($cursor -and ($cursor.Equals($rootPath,[StringComparison]::OrdinalIgnoreCase) -or $cursor.StartsWith($rootPath+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase))){
        if((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Web 安装目录中不允许目录链接或重解析点。'}
        $cursor=[IO.Path]::GetDirectoryName($cursor)
    }
    return $value
}
function Test-HifcPayloadDirectory {
    param([string]$Root,[string]$Directory,$Package)
    [void](Resolve-HifcPayloadPath $Root $Directory)
    $metadataPath=Resolve-HifcPayloadPath $Root (Join-Path $Directory 'package.json')
    if(-not(Test-Path -LiteralPath $metadataPath) -or (Get-Item -LiteralPath $metadataPath).Length -gt 65536){throw '已有发行目录的版本资料缺失或无效。'}
    $stored=Get-Content -LiteralPath $metadataPath -Raw -Encoding utf8|ConvertFrom-Json
    if($stored.version -ne $Package.version -or $stored.addInId -ne $Package.addInId -or $stored.releaseId -ne $Package.releaseId -or $stored.sourceCommit -ne $Package.sourceCommit -or @($stored.files).Count -ne @($Package.files).Count){throw '同一发行编号的版本资料不一致，保留当前安装。'}
    foreach($file in $Package.files){
        $entry=@($stored.files|Where-Object {$_.name -ceq $file.name})
        $path=Resolve-HifcPayloadPath $Root (Join-Path $Directory $file.name)
        if($entry.Count -ne 1 -or $entry[0].sha256 -ne $file.sha256 -or $entry[0].length -ne $file.length -or -not(Test-Path -LiteralPath $path) -or (Get-Item -LiteralPath $path).Length -ne $file.length -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256){throw ('已有发行文件与安装包不一致，不覆盖当前版本：'+$file.name)}
    }
}
function New-HifcPayloadStage {
    param([string]$PackageRoot,[string]$InstallationRoot,$Package)
    if($Package.releaseId -notmatch '^0\.5\.0-dev-[0-9]{8}T[0-9]{6}Z$'){throw 'Web 发行编号无效。'}
    $names=@('Hifc.Web.Revit.dll','Hifc.Web.Client.dll','Hifc.Web.Contracts.dll','Newtonsoft.Json.dll','Hifc.Web.Update.exe','Hifc.Web.Update.exe.config')
    if(@($Package.files).Count -ne 6 -or @(Compare-Object $names @($Package.files|ForEach-Object name)).Count){throw 'Web 载荷文件清单无效。'}
    $root=[IO.Path]::GetFullPath($InstallationRoot)
    $releases=Resolve-HifcPayloadPath $root (Join-Path $root 'releases')
    $target=Resolve-HifcPayloadPath $root (Join-Path $releases $Package.releaseId)
    New-Item -ItemType Directory -Path $releases -Force|Out-Null
    if(Test-Path -LiteralPath $target){Test-HifcPayloadDirectory $root $target $Package;return [pscustomobject]@{Root=$root;CandidateDirectory=$target;TargetDirectory=$target;Existing=$true;Published=$true;Package=$Package}}
    $candidate=Resolve-HifcPayloadPath $root (Join-Path $releases ('.install-'+[Guid]::NewGuid().ToString('N')))
    $state=[pscustomobject]@{Root=$root;CandidateDirectory=$candidate;TargetDirectory=$target;Existing=$false;Published=$false;Package=$Package}
    New-Item -ItemType Directory -Path $candidate|Out-Null
    try{
        foreach($name in $names){$from=Join-Path $PackageRoot ('client/'+$name);if((Get-Item -LiteralPath $from -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw '安装包文件不允许链接。'};Copy-Item -LiteralPath $from -Destination (Join-Path $candidate $name)}
        Copy-Item -LiteralPath (Join-Path $PackageRoot 'package.json') -Destination (Join-Path $candidate 'package.json')
        Test-HifcPayloadDirectory $root $candidate $Package
        return $state
    }catch{Remove-HifcPayloadStage $state;throw}
}
function Complete-HifcPayloadStage {
    param($Stage)
    Test-HifcPayloadDirectory $Stage.Root $Stage.CandidateDirectory $Stage.Package
    if($Stage.Existing){return $Stage.TargetDirectory}
    [void](Resolve-HifcPayloadPath $Stage.Root $Stage.TargetDirectory)
    if(Test-Path -LiteralPath $Stage.TargetDirectory){Test-HifcPayloadDirectory $Stage.Root $Stage.TargetDirectory $Stage.Package;Remove-HifcPayloadStage $Stage;return $Stage.TargetDirectory}
    [IO.Directory]::Move($Stage.CandidateDirectory,$Stage.TargetDirectory)
    $Stage.Published=$true
    return $Stage.TargetDirectory
}
function Remove-HifcPayloadStage {
    param($Stage)
    if($Stage.Existing -or $Stage.Published){return}
    $path=Resolve-HifcPayloadPath $Stage.Root $Stage.CandidateDirectory
    $parent=Resolve-HifcPayloadPath $Stage.Root (Join-Path $Stage.Root 'releases')
    if([IO.Path]::GetDirectoryName($path) -ne $parent -or [IO.Path]::GetFileName($path) -notmatch '^\.install-[a-f0-9]{32}$'){throw '不允许清理非本次安装临时目录。'}
    if(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path -Recurse -Force}
}
