[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Package,[string]$Launcher,[switch]$WithInstallation)
$ErrorActionPreference='Stop'
$webRoot=Split-Path -Parent $PSScriptRoot
$windowsPowerShell=Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
$run=Join-Path $webRoot ('.local/installer-entry-'+[Guid]::NewGuid().ToString('N'))
$fixture=Join-Path $run '同事安装 目录/HIFC-0.5.0-Web'
New-Item -ItemType Directory -Path (Split-Path -Parent $fixture) -Force|Out-Null
Copy-Item -LiteralPath ([IO.Path]::GetFullPath($Package)) -Destination $fixture -Recurse
if($Launcher){
    $text=[IO.File]::ReadAllText([IO.Path]::GetFullPath($Launcher)) -replace '\r?\n',"`r`n"
    [IO.File]::WriteAllText((Join-Path $fixture '安装 Web 版.cmd'),$text,[Text.UTF8Encoding]::new($false))
}
$localAppData=Join-Path $run 'localappdata';$appData=Join-Path $run 'appdata'
$root=Join-Path $localAppData 'HIFC/0.5.0-Web/Revit2020'
$manifest=Join-Path $appData 'Autodesk/Revit/Addins/2020/Hifc.Web.addin'
$parentScript=@'
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false)
Write-Output ('HIFC_TEST_PARENT_POLICY='+(Get-ExecutionPolicy))
$command='/d /s /c ""'+$env:HIFC_INSTALLER_TEST_ENTRY+'"'
if($env:HIFC_INSTALLER_TEST_MODE -eq 'validate'){$command+=' -ValidateOnly'}
$command+='"'
$start=New-Object Diagnostics.ProcessStartInfo
$start.FileName=$env:ComSpec;$start.Arguments=$command;$start.UseShellExecute=$false;$start.CreateNoWindow=$true
$start.RedirectStandardInput=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
$start.StandardOutputEncoding=New-Object Text.UTF8Encoding($false);$start.StandardErrorEncoding=New-Object Text.UTF8Encoding($false)
$batch=New-Object Diagnostics.Process
try{
    $batch.StartInfo=$start;[void]$batch.Start()
    $stdout=$batch.StandardOutput.ReadToEndAsync();$stderr=$batch.StandardError.ReadToEndAsync()
    $batch.StandardInput.WriteLine();$batch.StandardInput.Close();$batch.WaitForExit()
    [Console]::Out.Write($stdout.GetAwaiter().GetResult());[Console]::Error.Write($stderr.GetAwaiter().GetResult())
    exit $batch.ExitCode
}finally{$batch.Dispose()}
'@
$encoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($parentScript))
function Read-PersistentPolicies {
    (& $windowsPowerShell -NoProfile -Command 'Get-ExecutionPolicy -List | Where-Object {$_.Scope -ne "Process"} | Select-Object Scope,ExecutionPolicy | ConvertTo-Json -Compress') -join ''
}
function Invoke-Installer([string]$Mode){
    $start=New-Object Diagnostics.ProcessStartInfo
    $start.FileName=$windowsPowerShell
    $start.Arguments='-NoProfile -NonInteractive -ExecutionPolicy Restricted -EncodedCommand '+$encoded
    $start.WorkingDirectory=$run;$start.UseShellExecute=$false;$start.CreateNoWindow=$true
    $start.RedirectStandardInput=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    $start.StandardOutputEncoding=New-Object Text.UTF8Encoding($false)
    $start.StandardErrorEncoding=New-Object Text.UTF8Encoding($false)
    $start.EnvironmentVariables['HIFC_INSTALLER_TEST_ENTRY']=Join-Path $fixture '安装 Web 版.cmd'
    $start.EnvironmentVariables['HIFC_INSTALLER_TEST_MODE']=$Mode
    $start.EnvironmentVariables['LOCALAPPDATA']=$localAppData;$start.EnvironmentVariables['APPDATA']=$appData
    $start.EnvironmentVariables['PSModulePath']=Join-Path (Split-Path -Parent $windowsPowerShell) 'Modules'
    $process=New-Object Diagnostics.Process
    try{
        $process.StartInfo=$start;[void]$process.Start()
        $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
        $process.StandardInput.WriteLine();$process.StandardInput.Close()
        if(-not $process.WaitForExit(45000)){throw ('Installer entry did not finish: '+$Mode)}
        $output=$stdout.GetAwaiter().GetResult()+$stderr.GetAwaiter().GetResult()
        [IO.File]::WriteAllText((Join-Path $run ($Mode+'.log')),$output,[Text.UTF8Encoding]::new($false))
        if($output -notmatch 'HIFC_TEST_PARENT_POLICY=Restricted'){throw 'Restricted parent policy was not active.'}
        return [pscustomobject]@{exitCode=$process.ExitCode;output=$output}
    }finally{$process.Dispose()}
}
$before=Read-PersistentPolicies
$validation=Invoke-Installer 'validate'
if($validation.exitCode -ne 0 -or $validation.output -notmatch 'PASS Web package files'){
    Write-Output $validation.output
    throw ('Actual installer entry failed under Restricted; see '+$run)
}
if((Test-Path -LiteralPath $manifest) -or (Test-Path -LiteralPath $root)){throw 'ValidateOnly installed files instead of just validating.'}
if($WithInstallation){
    if(Get-Process -Name Revit -ErrorAction SilentlyContinue){throw 'Actual installation verification requires Revit to be closed.'}
    $thumbprint='90DEB5EDA2139C762DE841D4C76C4BE973DE081F'
    if(-not((Test-Path -LiteralPath ('Cert:/CurrentUser/Root/'+$thumbprint)) -or (Test-Path -LiteralPath ('Cert:/LocalMachine/Root/'+$thumbprint)))){throw 'Certificate trust must already be confirmed by the user for the installation fixture.'}
    $localManifest=Join-Path (Split-Path -Parent $manifest) 'Hifc.V1.addin'
    New-Item -ItemType Directory -Path (Split-Path -Parent $manifest) -Force|Out-Null
    [IO.File]::WriteAllText($localManifest,'Protected Local installer sentinel',[Text.UTF8Encoding]::new($false))
    $localHash=(Get-FileHash -LiteralPath $localManifest -Algorithm SHA256).Hash
    $installed=Invoke-Installer 'install'
    if($installed.exitCode -ne 0){Write-Output $installed.output;throw 'Actual installer entry could not complete the isolated installation.'}
    $metadata=Get-Content -LiteralPath (Join-Path $root 'package.json') -Raw -Encoding utf8|ConvertFrom-Json
    $version=Join-Path $root ('releases/'+$metadata.releaseId)
    [xml]$addin=Get-Content -LiteralPath $manifest -Encoding utf8
    if($addin.RevitAddIns.AddIn.AddInId -ne '53ef39d3-a187-4fb5-90f8-29e91d747602' -or $addin.RevitAddIns.AddIn.Assembly -ne (Join-Path $version 'Hifc.Web.Revit.dll')){throw 'The isolated installation did not activate its owned Web manifest.'}
    foreach($file in $metadata.files){if((Get-FileHash -LiteralPath (Join-Path $version $file.name) -Algorithm SHA256).Hash -ne $file.sha256){throw 'Installed payload differs from its package.'}}
    $manifestHash=(Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash
    $metadataHash=(Get-FileHash -LiteralPath (Join-Path $root 'package.json') -Algorithm SHA256).Hash
    [IO.File]::AppendAllText((Join-Path $fixture 'client/Hifc.Web.Revit.dll'),'tampered fixture')
    $rejected=Invoke-Installer 'corrupt'
    if($rejected.exitCode -eq 0){throw 'The CMD entry hid an installer failure exit code.'}
    if((Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash -ne $manifestHash -or (Get-FileHash -LiteralPath (Join-Path $root 'package.json') -Algorithm SHA256).Hash -ne $metadataHash -or (Get-FileHash -LiteralPath $localManifest -Algorithm SHA256).Hash -ne $localHash){throw 'Rejected installation changed active Web or Local files.'}
}
if((Read-PersistentPolicies) -cne $before){throw 'Persistent execution policies changed.'}
[ordered]@{verifiedUtc=[DateTime]::UtcNow.ToString('O');parentExecutionPolicy='Restricted';actualCmdEntryVerified=$true;spaceAndChinesePathVerified=$true;persistentPoliciesUnchanged=$true;isolatedInstallationVerified=[bool]$WithInstallation;failureExitAndOldInstallationVerified=[bool]$WithInstallation;scope='real Windows PowerShell 5.1 and CMD; isolated user directories; not a colleague physical Windows 11 machine'}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $run 'result.json') -Encoding utf8
Write-Output ('PASS actual CMD installer under Restricted; installation='+[bool]$WithInstallation+'; persistent policies unchanged; '+$run)
