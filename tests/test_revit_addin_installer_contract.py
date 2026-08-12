from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "installer" / "Install-Revit2020.ps1"
INSTALL_CMD = ROOT / "installer" / "Install.cmd"
UNINSTALL_CMD = ROOT / "installer" / "Uninstall.cmd"
WORKFLOW = ROOT / ".github" / "workflows" / "build-revit-mcp.yml"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def test_installer_is_user_level_idempotent_and_supports_uninstall():
    source = read(SCRIPT)
    assert "[switch]$Uninstall" in source
    assert "$env:APPDATA" in source
    assert '"Autodesk\\Revit\\Addins\\2020"' in source
    assert '"BIMBaoGui.RevitAddin"' in source
    assert '"BIMBaoGui.RevitAddin.addin"' in source
    assert "Remove-Item" in source
    assert "Test-Path" in source


def test_installer_refuses_to_replace_loaded_addin_without_force():
    source = read(SCRIPT)
    assert "[switch]$Force" in source
    assert 'Get-Process -Name "Revit"' in source
    assert "请先关闭 Revit" in source


def test_installer_writes_absolute_manifest_and_verifies_dll_hash():
    source = read(SCRIPT)
    assert "[IO.Path]::GetFullPath" in source
    assert "Get-FileHash" in source
    assert "sourceHash" in source
    assert "installedHash" in source
    assert "[System.Security.SecurityElement]::Escape" in source
    assert "BIMBaoGui.RevitAddin.App" in source
    assert "6F3EE836-2A54-43C1-8B90-C9D291E9A8F1" in source


def test_installer_publishes_machine_readable_install_evidence():
    source = read(SCRIPT)
    assert '"install-evidence.json"' in source
    assert "ConvertTo-Json" in source
    assert "installedDllSha256" in source
    assert "installedUtc" in source


def test_double_click_wrappers_use_their_own_extracted_directory():
    install = read(INSTALL_CMD)
    uninstall = read(UNINSTALL_CMD)
    for source in (install, uninstall):
        assert "%~dp0" in source
        assert "powershell.exe" in source.lower()
        assert "-NoProfile" in source
        assert "-ExecutionPolicy Bypass" in source
        assert "Install-Revit2020.ps1" in source
        assert 'set "BIMBAOGUI_EXIT_CODE=%ERRORLEVEL%"' in source
        assert "exit /b %BIMBAOGUI_EXIT_CODE%" in source
    assert "-SourceRoot" in install
    assert "BIMBaoGui.RevitAddin" in install
    assert "-Uninstall" in uninstall


def test_unified_workflow_packages_complete_double_click_installer():
    workflow = read(WORKFLOW)
    assert '- "installer/**"' in workflow
    assert "Copy-Item installer/Install-Revit2020.ps1 $artifactRoot/" in workflow
    assert "Copy-Item installer/Install.cmd $artifactRoot/" in workflow
    assert "Copy-Item installer/Uninstall.cmd $artifactRoot/" in workflow
    assert "SHA256SUMS.txt" in workflow


def test_checksum_manifest_uses_portable_forward_slash_paths():
    workflow = read(WORKFLOW)
    assert "[IO.Path]::DirectorySeparatorChar" in workflow
    assert "[IO.Path]::AltDirectorySeparatorChar" in workflow
    assert "Checksum manifest contains a backslash path" in workflow


def test_unified_workflow_smoke_tests_install_and_uninstall_on_windows():
    workflow = read(WORKFLOW)
    assert "Smoke-test complete installer and uninstall" in workflow
    assert "& installer/Install-Revit2020.ps1" in workflow
    assert "-Uninstall -Force" in workflow
    assert "install-evidence.json" in workflow
    assert "IsPathRooted" in workflow


def test_unified_workflow_tracks_packaged_readme_as_an_artifact_input():
    workflow = read(WORKFLOW)
    assert '- "docs/revit-addin/README.md"' in workflow
