[CmdletBinding()]
param([switch]$ValidateOnly)
$ErrorActionPreference='Stop'
$pinnedRoot='34F12F7297D570C3641DB2DF80FF6755D0FF09AC29CF13121121D55AE86FE15F'
$thumbprint='90DEB5EDA2139C762DE841D4C76C4BE973DE081F'
$package=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'package.json') -Raw|ConvertFrom-Json
$expected=@('Hifc.Web.Revit.dll','Hifc.Web.Client.dll','Hifc.Web.Contracts.dll','Newtonsoft.Json.dll')
if($package.version -ne '0.5.0-Web' -or $package.addInId -ne '53ef39d3-a187-4fb5-90f8-29e91d747602'){throw 'Web 安装包标识无效。'}
$actual=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'client') -File|ForEach-Object {$_.Name})
if(@(Compare-Object $expected $actual).Count -ne 0){throw '客户端文件清单无效。'}
foreach($name in $expected){
    $entry=@($package.files|Where-Object {$_.name -ceq $name})
    if($entry.Count -ne 1 -or (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot ('client/'+$name)) -Algorithm SHA256).Hash -ne $entry[0].sha256){throw ('文件校验失败：'+$name)}
}
$certificatePath=Join-Path $PSScriptRoot 'hifc-web-root-ca.crt'
$certificate=New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($certificatePath)
$sha=[System.Security.Cryptography.SHA256]::Create()
try{$rootHash=[BitConverter]::ToString($sha.ComputeHash($certificate.RawData)).Replace('-','')}finally{$sha.Dispose()}
if($rootHash -cne $pinnedRoot -or $certificate.Thumbprint -ne $thumbprint){throw '服务器根证书与已核实的指纹不匹配。'}
if($ValidateOnly){Write-Output 'PASS Web package files, independent identity and pinned public certificate';return}
if(Get-Process -Name Revit -ErrorAction SilentlyContinue){throw '请先关闭 Revit，再安装 Web 版。'}
$trusted=(Test-Path -LiteralPath ('Cert:\CurrentUser\Root\'+$thumbprint)) -or (Test-Path -LiteralPath ('Cert:\LocalMachine\Root\'+$thumbprint))
if(-not $trusted){
    Write-Host '需要由你本人信任局域网 HIFC 服务器证书。'
    Write-Host ('SHA-256：'+$pinnedRoot)
    Write-Host '在证书窗口点击“安装证书”，选择“当前用户”，将证书放入“受信任的根证书颁发机构”，核对后完成安全确认。'
    Start-Process -FilePath $certificatePath
    [void](Read-Host '完成证书导入后按回车继续；取消可关闭本窗口')
    $trusted=(Test-Path -LiteralPath ('Cert:\CurrentUser\Root\'+$thumbprint)) -or (Test-Path -LiteralPath ('Cert:\LocalMachine\Root\'+$thumbprint))
    if(-not $trusted){throw '尚未检测到已信任的服务器证书，安装已停止。'}
}
[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
$ready=Invoke-RestMethod -Uri 'https://192.168.6.45:8443/health/ready' -TimeoutSec 15
if($ready.state -ne 'ready' -or -not $ready.authenticationConfigured){throw '服务器尚未开放完整体验，请稍后重试。'}
$installation=Join-Path $env:LOCALAPPDATA 'HIFC/0.5.0-Web/Revit2020'
$manifest=Join-Path $env:APPDATA 'Autodesk/Revit/Addins/2020/Hifc.Web.addin'
if(Test-Path -LiteralPath $manifest){
    [xml]$existing=Get-Content -LiteralPath $manifest
    if($existing.RevitAddIns.AddIn.AddInId -ne $package.addInId -or $existing.RevitAddIns.AddIn.Assembly -ne (Join-Path $installation 'Hifc.Web.Revit.dll')){throw '已有同名清单不属于此 Web 安装，已停止。'}
}
New-Item -ItemType Directory -Path $installation -Force|Out-Null
New-Item -ItemType Directory -Path (Split-Path -Parent $manifest) -Force|Out-Null
foreach($name in $expected){Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('client/'+$name)) -Destination (Join-Path $installation $name) -Force}
$assembly=[Security.SecurityElement]::Escape((Join-Path $installation 'Hifc.Web.Revit.dll'))
$xml="<?xml version=`"1.0`" encoding=`"utf-8`"?><RevitAddIns><AddIn Type=`"Application`"><Name>HIFC-V1-Web</Name><Assembly>$assembly</Assembly><AddInId>$($package.addInId)</AddInId><FullClassName>Hifc.Web.Revit.WebApp</FullClassName><VendorId>HIFC</VendorId><VendorDescription>HIFC Web</VendorDescription></AddIn></RevitAddIns>"
[IO.File]::WriteAllText($manifest,$xml,(New-Object Text.UTF8Encoding($false)))
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'package.json') -Destination (Join-Path $installation 'package.json') -Force
Write-Host '安装完成。打开模型副本 → HIFC-V1-Web 独立页签 → 模型检查 / 属性管理。'
Write-Host '首次出现 Revit 发布者提示时，请核对 Hifc.Web.Revit.dll 并选择“载入一次”。'
Write-Host '首次登记填写手机号和真实姓名，后续在当前 Windows 用户下自动连接。'
