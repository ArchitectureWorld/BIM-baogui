[CmdletBinding()]
param([switch]$ValidateOnly)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'CertificateTrust.ps1')
$package=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'package.json') -Raw|ConvertFrom-Json
$expected=@('Hifc.Web.Revit.dll','Hifc.Web.Client.dll','Hifc.Web.Contracts.dll','Newtonsoft.Json.dll','Hifc.Web.Update.exe','Hifc.Web.Update.exe.config')
if($package.version -ne '0.5.0-Web' -or $package.addInId -ne '53ef39d3-a187-4fb5-90f8-29e91d747602' -or $package.releaseId -notmatch '^0\.5\.0-dev-[0-9]{8}T[0-9]{6}Z$'){throw 'Web 安装包标识无效。'}
$actual=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'client') -File|ForEach-Object {$_.Name})
if(@(Compare-Object $expected $actual).Count -ne 0){throw '客户端文件清单无效。'}
foreach($name in $expected){
    $entry=@($package.files|Where-Object {$_.name -ceq $name})
    if($entry.Count -ne 1 -or (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot ('client/'+$name)) -Algorithm SHA256).Hash -ne $entry[0].sha256){throw ('文件校验失败：'+$name)}
}
$certificatePath=Join-Path $PSScriptRoot 'hifc-web-root-ca.crt'
$certificate=New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($certificatePath)
Confirm-HifcServerCertificate -Certificate $certificate
$publicKeyPath=Join-Path $PSScriptRoot 'update-public-key.json'
$public=Get-Content -LiteralPath $publicKeyPath -Raw|ConvertFrom-Json
if([Convert]::FromBase64String($public.modulus).Length -lt 256 -or -not $public.exponent){throw '更新签名公钥无效。'}
if($ValidateOnly){Write-Output 'PASS Web package files, independent identity, update key and pinned public certificate';return}
if(Get-Process -Name Revit -ErrorAction SilentlyContinue){throw '请先关闭 Revit，再安装 Web 版。'}
if(([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){
    $session=(Get-Process -Id $PID).SessionId
    $shells=@(Get-CimInstance Win32_Process -Filter "Name='explorer.exe' AND SessionId=$session")
    $desktopUsers=@($shells|ForEach-Object {(Invoke-CimMethod -InputObject $_ -MethodName GetOwnerSid).Sid}|Where-Object {$_})
    if($desktopUsers.Count -gt 0 -and $desktopUsers -notcontains [Security.Principal.WindowsIdentity]::GetCurrent().User.Value){
        throw '当前管理员身份与桌面账号不同。请关闭此窗口，用运行 Revit 的账号直接双击“安装 Web 版.cmd”，无需选择“以管理员身份运行”。'
    }
}
try{Install-HifcServerCertificate -Certificate $certificate}finally{$certificate.Dispose()}
[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.Net.Http
$handler=New-Object Net.Http.HttpClientHandler; $handler.UseProxy=$false; $http=New-Object Net.Http.HttpClient($handler); $http.Timeout=[TimeSpan]::FromSeconds(15)
try{$ready=$http.GetStringAsync('https://192.168.6.45:8443/health/ready').GetAwaiter().GetResult()|ConvertFrom-Json}finally{$http.Dispose()}
if($ready.state -ne 'ready' -or -not $ready.authenticationConfigured){throw '服务器尚未开放完整体验，请稍后重试。'}
$installation=Join-Path $env:LOCALAPPDATA 'HIFC/0.5.0-Web/Revit2020'
$manifest=Join-Path $env:APPDATA 'Autodesk/Revit/Addins/2020/Hifc.Web.addin'
if(Test-Path -LiteralPath $manifest){
    [xml]$existing=Get-Content -LiteralPath $manifest
    $existingAssembly=[IO.Path]::GetFullPath($existing.RevitAddIns.AddIn.Assembly)
    if($existing.RevitAddIns.AddIn.AddInId -ne $package.addInId -or -not $existingAssembly.StartsWith($installation+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($existingAssembly) -ne 'Hifc.Web.Revit.dll'){throw '已有同名清单不属于此 Web 安装，已停止。'}
}
New-Item -ItemType Directory -Path $installation -Force|Out-Null
New-Item -ItemType Directory -Path (Split-Path -Parent $manifest) -Force|Out-Null
$currentMetadata=Join-Path $installation 'package.json'
if(Test-Path -LiteralPath $currentMetadata){$current=Get-Content -LiteralPath $currentMetadata -Raw|ConvertFrom-Json;if($current.releaseId -and [string]::CompareOrdinal($current.releaseId,$package.releaseId) -gt 0){Write-Host '本机已安装更新的版本，继续保留当前版本。';return}}
$installedKey=Join-Path $installation 'update-public-key.json'
if(Test-Path -LiteralPath $installedKey){$known=Get-Content -LiteralPath $installedKey -Raw|ConvertFrom-Json;if($known.modulus -cne $public.modulus -or $known.exponent -cne $public.exponent){throw '更新签名身份与已安装版本不一致，请核实服务器。'}}
$versionDirectory=Join-Path $installation ('releases/'+$package.releaseId)
New-Item -ItemType Directory -Path $versionDirectory -Force|Out-Null
foreach($name in $expected){Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('client/'+$name)) -Destination (Join-Path $versionDirectory $name) -Force}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'package.json') -Destination (Join-Path $versionDirectory 'package.json') -Force
Copy-Item -LiteralPath $publicKeyPath -Destination $installedKey -Force
$assembly=[Security.SecurityElement]::Escape((Join-Path $versionDirectory 'Hifc.Web.Revit.dll'))
$xml="<?xml version=`"1.0`" encoding=`"utf-8`"?><RevitAddIns><AddIn Type=`"Application`"><Name>HIFC-V1-Web</Name><Assembly>$assembly</Assembly><AddInId>$($package.addInId)</AddInId><FullClassName>Hifc.Web.Revit.WebApp</FullClassName><VendorId>HIFC</VendorId><VendorDescription>HIFC Web</VendorDescription></AddIn></RevitAddIns>"
[IO.File]::WriteAllText($manifest,$xml,(New-Object Text.UTF8Encoding($false)))
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'package.json') -Destination (Join-Path $installation 'package.json') -Force
Write-Host '安装完成。打开模型副本 → HIFC-V1-Web 独立页签 → 模型检查 / 属性管理。'
Write-Host '首次出现 Revit 发布者提示时，请核对 Hifc.Web.Revit.dll 并选择“载入一次”。'
Write-Host '首次填写手机号和真实姓名，后续自动连接；换电脑使用相同人员信息，保留原到期日。'
Write-Host '新版将从 zetta 自动检查和下载；关闭 Revit 后更新，下次启动生效。'
