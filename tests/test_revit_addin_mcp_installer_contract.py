import ctypes
import hashlib
import os
import shutil
import subprocess
import threading
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
INSTALLER = ROOT / "installer" / "Install-Revit2020.ps1"
INSTALL_CMD = ROOT / "installer" / "Install.cmd"
UNINSTALL_CMD = ROOT / "installer" / "Uninstall.cmd"
PROBE_CMD = ROOT / "installer" / "McpProbe.cmd"
CONFIG_EXAMPLE = ROOT / "installer" / "mcp-server-config.example.json"
WORKFLOW = ROOT / ".github" / "workflows" / "build-revit-mcp.yml"
STDIO_WORKFLOW = ROOT / ".github" / "workflows" / "verify-revit-mcp-stdio.yml"
ADDIN_PROJECT = ROOT / "src" / "BIMBaoGui.RevitAddin" / "BIMBaoGui.RevitAddin.csproj"
HIFC_PROJECT = ROOT / "src" / "BIMBaoGui.HifcCore" / "BIMBaoGui.HifcCore.csproj"
MCP_PROJECT = ROOT / "src" / "BIMBaoGui.McpServer" / "BIMBaoGui.McpServer.csproj"
README = ROOT / "docs" / "revit-addin" / "README.md"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def test_product_and_install_paths_are_uniformly_versioned_043():
    installer = read(INSTALLER)
    addin_project = read(ADDIN_PROJECT)
    hifc_project = read(HIFC_PROJECT)
    mcp_project = read(MCP_PROJECT)
    probe = read(PROBE_CMD)
    example = read(CONFIG_EXAMPLE)
    for project in (addin_project, hifc_project, mcp_project):
        assert "<Version>0.4.3</Version>" in project
        assert "<AssemblyVersion>0.4.3.0</AssemblyVersion>" in project
    assert '$mcpVersion = "0.4.3"' in installer
    assert 'Join-Path $mcpBaseRoot $mcpVersion' in installer
    assert "McpServer\\0.4.3" in probe
    assert "McpServer\\\\0.4.3" in example


def test_installer_keeps_revit_user_addin_and_adds_stage03_dependencies():
    source = read(INSTALLER)
    assert '$env:APPDATA' in source
    assert '"Autodesk\\Revit\\Addins\\2020"' in source
    assert '$env:LOCALAPPDATA' in source
    assert '$mcpVersion = "0.4.3"' in source
    assert 'Join-Path $mcpBaseRoot $mcpVersion' in source
    assert '"BIMBaoGui.McpServer.exe"' in source
    assert '"BIMBaoGui.McpContracts.dll"' in source
    assert '"BIMBaoGui.HifcCore.dll"' in source
    assert '"mcp-server-config.json"' in source
    assert "hifcCoreDllSha256" in source


def test_installer_removes_only_explicitly_supported_legacy_mcp_versions():
    source = read(INSTALLER)
    assert "$legacyMcpVersions = @('0.4.0', '0.4.1', '0.4.2')" in source
    assert "foreach ($legacyMcpVersion in $legacyMcpVersions)" in source
    assert "Join-Path $mcpBaseRoot $legacyMcpVersion" in source
    assert "'^\\d+\\.\\d+\\.\\d+$'" not in source
    assert 'Remove-Item -LiteralPath $_.FullName -Recurse -Force' not in source


def test_uninstall_waits_for_a_transient_mcp_executable_lock(tmp_path: Path):
    app_data = tmp_path / "AppData"
    local_app_data = tmp_path / "LocalAppData"
    mcp_root = (
        local_app_data / "BIMBaoGui" / "McpServer" / "0.4.3"
    )
    mcp_root.mkdir(parents=True)
    executable = mcp_root / "BIMBaoGui.McpServer.exe"
    executable.write_bytes(b"locked smoke payload")
    sentinel = (
        local_app_data
        / "BIMBaoGui"
        / "McpServer"
        / "9.9.9"
        / "must-survive.marker"
    )
    sentinel.parent.mkdir(parents=True)
    sentinel.write_text("sentinel", encoding="utf-8")

    create_file = ctypes.windll.kernel32.CreateFileW
    create_file.argtypes = (
        ctypes.c_wchar_p,
        ctypes.c_uint32,
        ctypes.c_uint32,
        ctypes.c_void_p,
        ctypes.c_uint32,
        ctypes.c_uint32,
        ctypes.c_void_p,
    )
    create_file.restype = ctypes.c_void_p
    close_handle = ctypes.windll.kernel32.CloseHandle
    close_handle.argtypes = (ctypes.c_void_p,)
    close_handle.restype = ctypes.c_int
    handle = create_file(
        str(executable),
        0x80000000,
        0x00000001 | 0x00000002,
        None,
        3,
        0,
        None,
    )
    assert handle != ctypes.c_void_p(-1).value

    released = threading.Event()

    def release_after_first_delete_attempt():
        # The known timing is intentional: hold the file across the immediate
        # delete, then release it so bounded condition polling can succeed.
        released.wait(5.0)
        close_handle(handle)

    thread = threading.Thread(target=release_after_first_delete_attempt)
    thread.start()
    environment = os.environ.copy()
    environment["APPDATA"] = str(app_data)
    environment["LOCALAPPDATA"] = str(local_app_data)
    try:
        result = subprocess.run(
            [
                "pwsh",
                "-NoProfile",
                "-File",
                str(INSTALLER),
                "-Uninstall",
                "-Force",
            ],
            cwd=ROOT,
            env=environment,
            check=False,
            capture_output=True,
            text=True,
            encoding="utf-8",
            timeout=15,
        )
    finally:
        released.set()
        thread.join(timeout=2)

    assert result.returncode == 0, result.stdout + result.stderr
    assert not mcp_root.exists()
    assert sentinel.read_text(encoding="utf-8") == "sentinel"


def test_installer_generates_absolute_mcp_client_configuration():
    source = read(INSTALLER)
    assert '[IO.Path]::GetFullPath' in source
    assert 'mcpServers' in source
    assert 'bimbaogui-revit' in source
    assert 'command' in source
    assert 'ConvertTo-Json' in source
    assert 'mcpServerExeSha256' in source
    assert 'contractsDllSha256' in source


def test_uninstall_removes_only_product_roots_and_stale_bridge_discovery():
    source = read(INSTALLER)
    assert '$mcpServerRoot' in source
    assert '$mcpConfigPath' in source
    assert '$bridgeDiscoveryRoot' in source
    assert 'Remove-Item -LiteralPath $mcpServerRoot -Recurse -Force' in source
    assert 'Remove-Item -LiteralPath $mcpConfigPath -Force' in source
    assert 'Get-ChildItem -LiteralPath $bridgeDiscoveryRoot' in source
    assert 'Get-ChildItem -LiteralPath $mcpBaseRoot -Directory' not in source
    assert 'claude_desktop_config' not in source.lower()
    assert 'codex' not in source.lower()


def test_package_contains_double_click_probe_and_generic_config_example():
    probe = read(PROBE_CMD)
    example = read(CONFIG_EXAMPLE)
    assert '%LOCALAPPDATA%' in probe
    assert 'BIMBaoGui.McpServer.exe' in probe
    assert '--probe' in probe
    assert 'exit /b %BIMBAOGUI_EXIT_CODE%' in probe
    assert '"mcpServers"' in example
    assert '"bimbaogui-revit"' in example
    assert '"command"' in example


def test_probe_script_git_blob_has_no_carriage_returns():
    raw = subprocess.check_output(
        ["git", "show", "HEAD:installer/McpProbe.cmd"],
        cwd=ROOT,
    )
    assert b"\r" not in raw


def test_existing_double_click_install_and_uninstall_entrypoints_remain():
    install = read(INSTALL_CMD)
    uninstall = read(UNINSTALL_CMD)
    assert 'Install-Revit2020.ps1' in install
    assert '-SourceRoot' in install
    assert 'Install-Revit2020.ps1' in uninstall
    assert '-Uninstall' in uninstall


def test_double_click_install_passes_a_valid_package_root_to_powershell(
    tmp_path: Path,
):
    package_root = tmp_path / "package"
    package_root.mkdir()
    shutil.copy2(INSTALL_CMD, package_root / INSTALL_CMD.name)
    shutil.copy2(INSTALLER, package_root / INSTALLER.name)
    environment = os.environ.copy()
    environment["APPDATA"] = str(tmp_path / "AppData")
    environment["LOCALAPPDATA"] = str(tmp_path / "LocalAppData")

    result = subprocess.run(
        [
            environment["COMSPEC"],
            "/d",
            "/c",
            "Install.cmd < NUL",
        ],
        cwd=package_root,
        env=environment,
        check=False,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=15,
    )
    output = result.stdout + result.stderr

    assert result.returncode != 0
    assert "BIMBaoGui.RevitAddin.dll" in output, output
    assert "SourceRoot=" in output, output
    assert "Illegal characters in path" not in output, output


def test_double_click_install_hashes_payload_in_fresh_windows_powershell_51(
    tmp_path: Path,
):
    package_root = tmp_path / "中文 package"
    package_root.mkdir()
    for source in (INSTALL_CMD, UNINSTALL_CMD, INSTALLER):
        shutil.copy2(source, package_root / source.name)

    addin_payload = package_root / "BIMBaoGui.RevitAddin"
    mcp_payload = package_root / "BIMBaoGui.McpServer"
    addin_payload.mkdir()
    mcp_payload.mkdir()
    framework_assembly = (
        Path(os.environ["SystemRoot"])
        / "Microsoft.NET"
        / "Framework64"
        / "v4.0.30319"
        / "mscorlib.dll"
    )
    assert framework_assembly.is_file()
    for filename in (
        "BIMBaoGui.RevitAddin.dll",
        "BIMBaoGui.McpContracts.dll",
        "BIMBaoGui.HifcCore.dll",
    ):
        shutil.copy2(framework_assembly, addin_payload / filename)
    shutil.copy2(
        framework_assembly,
        mcp_payload / "BIMBaoGui.McpServer.exe",
    )

    environment = os.environ.copy()
    environment["APPDATA"] = str(tmp_path / "fresh AppData")
    environment["LOCALAPPDATA"] = str(tmp_path / "fresh LocalAppData")
    result = subprocess.run(
        [environment["COMSPEC"], "/d", "/c", "Install.cmd < NUL"],
        cwd=package_root,
        env=environment,
        check=False,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=30,
    )
    output = result.stdout + result.stderr

    assert result.returncode == 0, output
    assert "Get-FileHash" not in output, output
    assert (
        tmp_path
        / "fresh AppData"
        / "Autodesk"
        / "Revit"
        / "Addins"
        / "2020"
        / "BIMBaoGui.RevitAddin"
        / "install-evidence.json"
    ).is_file()


def test_installer_source_and_packaged_copy_parse_in_windows_powershell_51(
    tmp_path: Path,
):
    source_bytes = INSTALLER.read_bytes()
    source_sha256 = hashlib.sha256(source_bytes).hexdigest()
    powershell = (
        Path(os.environ["SystemRoot"])
        / "System32"
        / "WindowsPowerShell"
        / "v1.0"
        / "powershell.exe"
    )
    packaged_installer = tmp_path / "artifacts" / INSTALLER.name
    packaged_installer.parent.mkdir()
    packaging_environment = os.environ.copy()
    packaging_environment["BIMBAOGUI_INSTALLER_SOURCE"] = str(INSTALLER)
    packaging_environment["BIMBAOGUI_INSTALLER_DESTINATION"] = str(
        packaged_installer
    )
    packaging_result = subprocess.run(
        [
            str(powershell),
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            "$ErrorActionPreference = 'Stop'; "
            "Copy-Item -LiteralPath $env:BIMBAOGUI_INSTALLER_SOURCE "
            "-Destination $env:BIMBAOGUI_INSTALLER_DESTINATION",
        ],
        cwd=ROOT,
        env=packaging_environment,
        check=False,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=15,
    )
    assert packaging_result.returncode == 0, (
        "Windows PowerShell 5.1 failed to package the installer:\n"
        f"{packaging_result.stdout}{packaging_result.stderr}"
    )
    parser_command = (
        "$tokens = $null; $errors = $null; "
        "[System.Management.Automation.Language.Parser]::ParseFile("
        "$env:BIMBAOGUI_INSTALLER_UNDER_TEST, [ref]$tokens, [ref]$errors) "
        "| Out-Null; "
        "if ($errors.Count -gt 0) { "
        "$errors | ForEach-Object { Write-Error $_.Message }; exit 1 }"
    )

    for label, installer in (
        ("source", INSTALLER),
        ("packaged", packaged_installer),
    ):
        installer_bytes = installer.read_bytes()
        assert installer_bytes.startswith(b"\xef\xbb\xbf"), (
            f"{label} installer does not preserve the UTF-8 BOM"
        )
        assert hashlib.sha256(installer_bytes).hexdigest() == source_sha256, (
            f"{label} installer bytes differ from the source installer"
        )
        environment = os.environ.copy()
        environment["BIMBAOGUI_INSTALLER_UNDER_TEST"] = str(installer)
        result = subprocess.run(
            [
                str(powershell),
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                parser_command,
            ],
            cwd=ROOT,
            env=environment,
            check=False,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=15,
        )

        assert result.returncode == 0, (
            f"{label} installer does not parse in Windows PowerShell 5.1:\n"
            f"{result.stdout}{result.stderr}"
        )


def test_only_unified_workflow_owns_official_sdk_stdio_verification():
    workflow = read(WORKFLOW)
    assert not STDIO_WORKFLOW.exists()
    assert "tools/BIMBaoGui.McpSmoke/BIMBaoGui.McpSmoke.csproj" in workflow
    assert "Initialize server, list tools and call a read-only tool" in workflow
    assert "dotnet run" in workflow


def test_mcp_workflow_builds_one_complete_stage03_installable_zip():
    workflow = read(WORKFLOW)
    for text in (
        'dotnet build src/BIMBaoGui.HifcCore/BIMBaoGui.HifcCore.csproj',
        'dotnet build src/BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.csproj',
        'dotnet publish src/BIMBaoGui.McpServer/BIMBaoGui.McpServer.csproj',
        'tests/test_revit_addin_mcp_installer_contract.py',
        'tests/test_revit_addin_stage03_ui_contract.py',
        'tests/test_revit_addin_mcp_stage03_contract.py',
        'installer/McpProbe.cmd',
        'installer/mcp-server-config.example.json',
        'BIMBaoGui.HifcCore.dll',
        'BIMBaoGui.McpContracts.dll',
        'BIMBaoGui.McpServer.exe',
        'Install-Revit2020.ps1',
        'SHA256SUMS.txt',
        'name: BIMBaoGui-Revit2020-Native-MCP-v0.4.3',
    ):
        assert text in workflow


def test_readme_states_stage03_and_ifcflux_manual_boundary():
    source = read(README)
    assert "产品版本：0.4.3" in source
    assert "项目条件" in source
    assert "无上述项目条件（已确认）" in source
    assert "Stage03" in source
    assert "INTERNAL_VALIDATED" in source
    assert "IFCFLUX_MANUAL_PENDING" in source
    assert "IFCFlux" in source
