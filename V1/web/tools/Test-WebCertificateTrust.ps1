[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Package,[switch]$WithInstallation)
$ErrorActionPreference='Stop'
if($WithInstallation -and (Get-Process -Name Revit -ErrorAction SilentlyContinue)){throw 'Actual installation verification requires Revit to be closed.'}
$webRoot=Split-Path -Parent $PSScriptRoot
$packagePath=(Resolve-Path -LiteralPath $Package).Path
$run=Join-Path $webRoot ('.local/certificate-trust-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run -Force|Out-Null
function Read-ActualRoot {
    $store=[Security.Cryptography.X509Certificates.X509Store]::new('Root','CurrentUser')
    try{$store.Open('ReadOnly');@($store.Certificates|ForEach-Object {$_.Thumbprint}|Sort-Object)-join ','}finally{$store.Close()}
}
$before=Read-ActualRoot
$storePrefix='HIFC.Web.CertTest.'+[Guid]::NewGuid().ToString('N')
$child=@'
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false)
$ProgressPreference='SilentlyContinue'
$package=$env:HIFC_CERT_TEST_PACKAGE
$helper=Join-Path $package 'CertificateTrust.ps1'
$certificate=New-Object Security.Cryptography.X509Certificates.X509Certificate2((Join-Path $package 'hifc-web-root-ca.crt'))
. $helper
# Only the store-opening boundary is redirected. Every read, add and fresh-open
# uses a real Windows X509Store; these named stores do not grant system trust.
function New-HifcRootStore {
    param([Security.Cryptography.X509Certificates.StoreLocation]$Location)
    return New-Object Security.Cryptography.X509Certificates.X509Store(($env:HIFC_CERT_TEST_PREFIX+'.'+$Location.ToString()),'CurrentUser')
}
function OpenTestStore([string]$suffix){return New-Object Security.Cryptography.X509Certificates.X509Store(($env:HIFC_CERT_TEST_PREFIX+'.'+$suffix),'CurrentUser')}
function AddTestCertificate([string]$suffix){$store=OpenTestStore $suffix;try{$store.Open('ReadWrite');$store.Add($certificate)}finally{$store.Close()}}
function CountTestCertificates([string]$suffix){$store=OpenTestStore $suffix;try{$store.Open('ReadOnly');return $store.Certificates.Count}finally{$store.Close()}}
function ClearTestStores {
    foreach($suffix in @('CurrentUser','LocalMachine','CA')){
        $store=OpenTestStore $suffix
        try{$store.Open('ReadWrite');foreach($entry in $store.Certificates){$store.Remove($entry)}}finally{$store.Close()}
    }
}
try{
    foreach($case in @('fresh','intermediate','already-trusted','repeat','machine-root','tampered-ca')){
        ClearTestStores
        if($case -eq 'intermediate'){AddTestCertificate 'CA'}
        if($case -eq 'already-trusted'){AddTestCertificate 'CurrentUser'}
        if($case -eq 'machine-root'){AddTestCertificate 'LocalMachine'}
        if($case -eq 'tampered-ca'){
            $bytes=[byte[]]$certificate.RawData.Clone();$bytes[$bytes.Length-1]=$bytes[$bytes.Length-1] -bxor 1
            $tampered=New-Object Security.Cryptography.X509Certificates.X509Certificate2(,$bytes)
            $rejected=$false
            try{Install-HifcServerCertificate $tampered}catch{if($_.Exception.Message -notmatch '指纹'){throw};$rejected=$true}finally{$tampered.Dispose()}
            if(-not $rejected -or (CountTestCertificates 'CurrentUser') -ne 0){throw 'Unpinned certificate changed trust.'}
        }else{
            Install-HifcServerCertificate $certificate
            if($case -eq 'repeat'){Install-HifcServerCertificate $certificate}
            $expectedCurrent=if($case -eq 'machine-root'){0}else{1}
            if((CountTestCertificates 'CurrentUser') -ne $expectedCurrent -or -not(Find-HifcTrustedRoot $certificate)){throw ('Certificate trust verification failed: '+$case)}
            if($case -eq 'intermediate' -and (CountTestCertificates 'CA') -ne 1){throw 'Intermediate certificate was removed.'}
        }
        Write-Output ('PASS certificate '+$case)
    }
    ClearTestStores
    if($env:HIFC_CERT_TEST_INSTALL -eq '1'){
        # Restore the production store factory and use the existing real root.
        . $helper
        if(-not(Find-HifcTrustedRoot $certificate)){throw 'Actual installation test requires the already trusted root.'}
        Remove-PSDrive -Name Cert -Force -ErrorAction SilentlyContinue
        function global:Start-Process {throw 'The installer requested a manual certificate window.'}
        & (Join-Path $package 'Install-Web.ps1')
        $manifest=Join-Path $env:APPDATA 'Autodesk/Revit/Addins/2020/Hifc.Web.addin'
        if(-not(Test-Path -LiteralPath $manifest)){throw 'Installation with an already trusted root and no Cert provider failed.'}
        Write-Output 'PASS certificate actual-install-without-provider'
        if(([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){
            $env:APPDATA=Join-Path $env:APPDATA 'wrong-admin-user'
            function Get-CimInstance {param($ClassName,$Filter);return [pscustomobject]@{Name='explorer.exe'}}
            function Invoke-CimMethod {param($InputObject,$MethodName);return [pscustomobject]@{Sid='S-1-5-21-100-200-300-999'}}
            $rejected=$false
            try{& (Join-Path $package 'Install-Web.ps1')}catch{if($_.Exception.Message -notmatch '桌面账号'){throw};$rejected=$true}
            if(-not $rejected -or (Test-Path -LiteralPath (Join-Path $env:APPDATA 'Autodesk/Revit/Addins/2020/Hifc.Web.addin'))){throw 'The installer accepted a different administrator account.'}
            Write-Output 'PASS certificate other-admin-user-rejected'
        }
    }
}catch{
    [Console]::Out.WriteLine($_.Exception.ToString());exit 1
}finally{$certificate.Dispose()}
'@
$windowsPowerShell=Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
$start=New-Object Diagnostics.ProcessStartInfo
$start.FileName=$windowsPowerShell;$start.Arguments='-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand '+[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($child))
$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
$start.StandardOutputEncoding=[Text.UTF8Encoding]::new($false);$start.StandardErrorEncoding=[Text.UTF8Encoding]::new($false)
$start.EnvironmentVariables['HIFC_CERT_TEST_PACKAGE']=$packagePath;$start.EnvironmentVariables['HIFC_CERT_TEST_PREFIX']=$storePrefix
$start.EnvironmentVariables['HIFC_CERT_TEST_INSTALL']=if($WithInstallation){'1'}else{'0'}
$start.EnvironmentVariables['APPDATA']=Join-Path $run 'appdata';$start.EnvironmentVariables['LOCALAPPDATA']=Join-Path $run 'localappdata'
$start.EnvironmentVariables['PSModulePath']=Join-Path (Split-Path -Parent $windowsPowerShell) 'Modules'
$process=New-Object Diagnostics.Process
try{
    $process.StartInfo=$start;[void]$process.Start();$stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
    if(-not $process.WaitForExit(45000)){$process.Kill();throw 'Certificate verification timed out.'}
    $output=$stdout.GetAwaiter().GetResult()+$stderr.GetAwaiter().GetResult()
    [IO.File]::WriteAllText((Join-Path $run 'result.log'),$output,[Text.UTF8Encoding]::new($false))
    $count=if($WithInstallation){7}else{6}
    if($WithInstallation -and ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){$count++}
    if($process.ExitCode -ne 0 -or @([regex]::Matches($output,'(?m)^PASS certificate ')).Count -ne $count){Write-Output $output;throw ('Certificate trust tests failed: '+$run)}
    Write-Output (($output -split '\r?\n'|Where-Object {$_ -like 'PASS certificate *'}) -join [Environment]::NewLine)
}finally{
    $process.Dispose()
    foreach($suffix in @('CurrentUser','LocalMachine','CA')){
        $storeName=$storePrefix+'.'+$suffix
        if($storeName -notmatch '^HIFC\.Web\.CertTest\.[a-f0-9]{32}\.(CurrentUser|LocalMachine|CA)$'){throw 'Invalid test certificate store name.'}
        $ownedRegistry='Registry::HKEY_CURRENT_USER\Software\Microsoft\SystemCertificates\'+$storeName
        if(Test-Path -LiteralPath $ownedRegistry){Remove-Item -LiteralPath $ownedRegistry -Recurse -Force}
    }
    if((Read-ActualRoot) -cne $before){throw 'The real user trust store changed.'}
}
[ordered]@{verifiedUtc=[DateTime]::UtcNow.ToString('O');passed=$count;failed=0;windowsPowerShell='5.1';realRootStoreUnchanged=$true;isolation='Named Windows X509 stores with only the store-opening boundary redirected; actual installation uses existing real root and isolated user folders';actualInstallationWithoutCertProvider=[bool]$WithInstallation;physicalWindows11Verified=$false}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $run 'result.json') -Encoding utf8
Set-Content -LiteralPath (Join-Path $webRoot '.local/latest-certificate-trust-run.txt') -Value $run -Encoding utf8
Write-Output ('PASS certificate trust tests: '+$run)
