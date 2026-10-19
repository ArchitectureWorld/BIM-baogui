function Confirm-HifcServerCertificate {
    param([Parameter(Mandatory=$true)][Security.Cryptography.X509Certificates.X509Certificate2]$Certificate)
    $sha=[Security.Cryptography.SHA256]::Create()
    try{$actual=[BitConverter]::ToString($sha.ComputeHash($Certificate.RawData)).Replace('-','')}finally{$sha.Dispose()}
    if($actual -cne '34F12F7297D570C3641DB2DF80FF6755D0FF09AC29CF13121121D55AE86FE15F' -or $Certificate.Thumbprint -ne '90DEB5EDA2139C762DE841D4C76C4BE973DE081F'){
        throw '服务器根证书与已核实的指纹不匹配。'
    }
}
function New-HifcRootStore {
    param([Security.Cryptography.X509Certificates.StoreLocation]$Location)
    return New-Object Security.Cryptography.X509Certificates.X509Store('Root',$Location)
}
function Find-HifcTrustedRoot {
    param([Parameter(Mandatory=$true)][Security.Cryptography.X509Certificates.X509Certificate2]$Certificate)
    $expected=[Convert]::ToBase64String($Certificate.RawData)
    foreach($location in @([Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser,[Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)){
        $store=New-HifcRootStore -Location $location
        try{
            $store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
            foreach($entry in $store.Certificates){
                if($entry.Thumbprint -eq $Certificate.Thumbprint -and [Convert]::ToBase64String($entry.RawData) -ceq $expected){return $location.ToString()}
            }
        }finally{$store.Close()}
    }
    return $null
}
function Install-HifcServerCertificate {
    param([Parameter(Mandatory=$true)][Security.Cryptography.X509Certificates.X509Certificate2]$Certificate)
    Confirm-HifcServerCertificate -Certificate $Certificate
    $location=Find-HifcTrustedRoot -Certificate $Certificate
    if($location){Write-Host '已确认服务器根证书受信任，继续安装。';return}
    Write-Host '正在为当前 Windows 账号自动配置 HIFC 服务器根证书，无需手动选择证书目录。'
    $store=New-HifcRootStore -Location CurrentUser
    try{
        $store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        $store.Add($Certificate)
    }catch{
        throw 'Windows 未允许导入服务器根证书。请确认系统信任提示后重新运行；若受单位策略限制，请由 IT 为当前 Revit 账号部署该根证书。'
    }finally{$store.Close()}
    if(-not(Find-HifcTrustedRoot -Certificate $Certificate)){throw '证书写入后未能从受信任的根证书库重新读到，安装已停止。'}
    Write-Host '服务器根证书已自动安装并重新核验。'
}
