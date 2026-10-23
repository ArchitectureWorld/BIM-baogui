[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$buildProperties = [xml](Get-Content -LiteralPath (Join-Path $workspace 'Directory.Build.props') -Raw)
$packageVersion = [string]$buildProperties.Project.PropertyGroup.Version
if ($packageVersion -notmatch '^\d+\.\d+\.\d+(?:-[a-zA-Z0-9.-]+)?$') { throw 'Invalid package version.' }
$testProject = Join-Path $workspace 'tests\Hifc.Reference.Tests\Hifc.Reference.Tests.csproj'
$pluginProject = Join-Path $workspace 'src\Hifc.Revit\Hifc.Revit.csproj'
$uiProject = Join-Path $workspace 'tests\Hifc.Ui.Tests\Hifc.Ui.Tests.csproj'
& dotnet build $testProject -c Release --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Core test build failed.' }
& (Join-Path $workspace 'tests\Hifc.Reference.Tests\bin\Release\net48\Hifc.Reference.Tests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
& dotnet build $uiProject -c Release --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw 'UI test build failed.' }
# Keep each run's evidence and state fixtures separate. Tests still exercise
# repeated writes inside a run, without reusing cloud-synchronized older output.
# Keep the evidence directory short: .NET Framework state-file tests append
# descriptive names and can otherwise exceed Windows MAX_PATH in managed worktrees.
$uiOutput = Join-Path $workspace ('validation\ui-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $uiOutput -Force | Out-Null
$uiTest = Start-Process -FilePath (Join-Path $workspace 'tests\Hifc.Ui.Tests\bin\Release\net48\Hifc.Ui.Tests.exe') -ArgumentList ('"' + $uiOutput + '"') -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput (Join-Path $uiOutput 'test-output.log') -RedirectStandardError (Join-Path $uiOutput 'test-error.log')
Get-Content -LiteralPath (Join-Path $uiOutput 'test-output.log')
if ($uiTest.ExitCode -ne 0) { throw 'UI interaction tests failed.' }
foreach($suite in @('Hifc.Workbook.Tests','Hifc.Redline.Tests')) {
    & dotnet build (Join-Path $workspace ('tests\'+$suite+'\'+$suite+'.csproj')) -c Release --nologo -v minimal
    if($LASTEXITCODE -ne 0){throw "$suite build failed."}
    $suiteOutput=Join-Path $uiOutput $suite
    New-Item -ItemType Directory -Path $suiteOutput -Force | Out-Null
    $suiteExe=Join-Path $workspace ('tests\'+$suite+'\bin\Release\net48\'+$suite+'.exe')
    $suiteRun=Start-Process -FilePath $suiteExe -ArgumentList ('"'+$suiteOutput+'"') -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput (Join-Path $suiteOutput 'stdout.log') -RedirectStandardError (Join-Path $suiteOutput 'stderr.log')
    Get-Content -LiteralPath (Join-Path $suiteOutput 'stdout.log')
    if($suiteRun.ExitCode -ne 0){throw "$suite failed."}
}
& dotnet build $pluginProject -c Release --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Revit plugin build failed.' }
$package = Join-Path $workspace ('dist\Hifc.V1.Revit2020-' + $packageVersion)
New-Item -ItemType Directory -Path $package -Force | Out-Null
$binaries = Join-Path $workspace 'src\Hifc.Revit\bin\Release\net48'
foreach ($legacy in @('Hifc.Revit.dll','Hifc.Revit.pdb')) {
    $legacyPath = Join-Path $package $legacy
    if (Test-Path -LiteralPath $legacyPath) { Remove-Item -LiteralPath $legacyPath }
}
foreach ($name in @('Hifc.V1.Revit.dll','Hifc.Reference.Core.dll','Hifc.V1.Revit.pdb','Hifc.Reference.Core.pdb')) {
    Copy-Item -LiteralPath (Join-Path $binaries $name) -Destination (Join-Path $package $name) -Force
}
@{ workspace_root = $workspace } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $package 'settings.json') -Encoding utf8
$items = @(Get-ChildItem -LiteralPath $package -File | Where-Object { $_.Name -ne 'build-manifest.json' } | ForEach-Object {
    @{ name=$_.Name; bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
@{ version=$packageVersion; revit='2020'; target_framework='net48'; reference_contract_sha256='c1f3d74e17229aee4ed10bc4980532d2c7c61a5a783061c6e979562b808052c9'; files=$items } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $package 'build-manifest.json') -Encoding utf8
Write-Output "Package: $package"
