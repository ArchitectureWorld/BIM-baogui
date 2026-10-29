function Initialize-HifcWebAcceptance {
    param([Parameter(Mandatory=$true)][string]$Run)
    $ErrorActionPreference='Stop'
    $webRoot=Split-Path -Parent $PSScriptRoot
    $runPath=[IO.Path]::GetFullPath($Run)
    $allowed=[IO.Path]::GetFullPath((Join-Path $webRoot '.local'))+[IO.Path]::DirectorySeparatorChar
    if(-not $runPath.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){throw 'Acceptance run must stay in this Web workspace.'}
    $suffix='A'+[DateTime]::UtcNow.ToString('MMddHHmmss')
    $artifacts=Join-Path $webRoot ('.local/ab/'+$suffix)
    . (Join-Path $PSScriptRoot 'Get-WebDotNet.ps1')
    $sdk=Get-HifcWebDotNet
    & $sdk build (Join-Path $webRoot 'tests/Hifc.Web.NativeTests/Hifc.Web.NativeTests.csproj') -c Release --artifacts-path $artifacts ('-p:HifcWebAcceptanceSuffix='+$suffix) --nologo -v minimal *> (Join-Path $runPath 'build.log')
    if($LASTEXITCODE -ne 0){Get-Content (Join-Path $runPath 'build.log') -Tail 24|Write-Host;throw 'Independent native acceptance build failed.'}
    $hosts=@(Get-ChildItem -LiteralPath (Join-Path $artifacts 'bin/Hifc.Web.NativeTests') -Recurse -File -Filter ('Hifc.Web.NativeTests.'+$suffix+'.dll'))
    if($hosts.Count -ne 1){throw 'Independent native host assembly is not unique.'}
    $directory=$hosts[0].DirectoryName
    $entries=@()
    foreach($name in @('Hifc.Web.NativeTests','Hifc.Web.Revit','Hifc.Web.Client','Hifc.Web.Contracts','Hifc.Web.Update')){
        $extension=if($name -eq 'Hifc.Web.Update'){'.exe'}else{'.dll'}
        $path=Join-Path $directory ($name+'.'+$suffix+$extension)
        $identity=[Reflection.AssemblyName]::GetAssemblyName($path).Name
        if($identity -ne $name+'.'+$suffix){throw 'Acceptance assembly identity is not isolated.'}
        $entries+=@{Name=$identity;Path=$path;Sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}
    }
    @{AcceptanceSuffix=$suffix;Assemblies=$entries}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $runPath 'expected-assemblies.json') -Encoding utf8
    $manifest=Join-Path $env:APPDATA 'Autodesk/Revit/Addins/2020/Hifc.Web.NativeTests.addin'
    if(Test-Path -LiteralPath $manifest){
        [xml]$previous=Get-Content -LiteralPath $manifest -Raw
        $priorAssembly=[IO.Path]::GetFullPath($previous.RevitAddIns.AddIn.Assembly)
        if($previous.RevitAddIns.AddIn.AddInId -ne '82c20f55-e1f1-451a-918d-075405720287' -or -not $priorAssembly.StartsWith($webRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'An unowned test manifest already exists.'}
        Copy-Item -LiteralPath $manifest -Destination (Join-Path $runPath 'previous-test.addin')
    }
    $xml='<?xml version="1.0" encoding="utf-8"?><RevitAddIns><AddIn Type="Application"><Name>HIFC Web Native Tests</Name><Assembly>'+[Security.SecurityElement]::Escape($hosts[0].FullName)+'</Assembly><AddInId>82c20f55-e1f1-451a-918d-075405720287</AddInId><FullClassName>HifcWebNativeHost</FullClassName><VendorId>HIWB</VendorId><VendorDescription>Temporary Web model copy validation</VendorDescription></AddIn></RevitAddIns>'
    [IO.File]::WriteAllText($manifest,$xml,[Text.UTF8Encoding]::new($false))
    return [pscustomobject]@{Assembly=$hosts[0].FullName;Manifest=$manifest;Suffix=$suffix}
}
