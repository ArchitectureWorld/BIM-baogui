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
MCP_PROJECT = ROOT / "src" / "BIMBaoGui.McpServer" / "BIMBaoGui.McpServer.csproj"
README = ROOT / "docs" / "revit-addin" / "README.md"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def test_product_and_install_paths_are_uniformly_versioned_032():
    installer = read(INSTALLER)
    addin_project = read(ADDIN_PROJECT)
    mcp_project = read(MCP_PROJECT)
    probe = read(PROBE_CMD)
    example = read(CONFIG_EXAMPLE)
    assert "<Version>0.3.2</Version>" in addin_project
    assert "<AssemblyVersion>0.3.2.0</AssemblyVersion>" in addin_project
    assert "<Version>0.3.2</Version>" in mcp_project
    assert '$mcpVersion = "0.3.2"' in installer
    assert 'Join-Path $mcpBaseRoot $mcpVersion' in installer
    assert "McpServer\\0.3.2" in probe
    assert "McpServer\\\\0.3.2" in example


def test_installer_keeps_revit_user_addin_and_adds_versioned_mcp_server():
    source = read(INSTALLER)
    assert '$env:APPDATA' in source
    assert '"Autodesk\\Revit\\Addins\\2020"' in source
    assert '$env:LOCALAPPDATA' in source
    assert '$mcpVersion = "0.3.2"' in source
    assert 'Join-Path $mcpBaseRoot $mcpVersion' in source
    assert '"BIMBaoGui.McpServer.exe"' in source
    assert '"BIMBaoGui.McpContracts.dll"' in source
    assert '"mcp-server-config.json"' in source


def test_installer_removes_superseded_mcp_version_directories():
    source = read(INSTALLER)
    assert 'Get-ChildItem -LiteralPath $mcpBaseRoot -Directory' in source
    assert "'^\\d+\\.\\d+\\.\\d+$'" in source
    assert 'Remove-Item -LiteralPath $_.FullName -Recurse -Force' in source


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


def test_existing_double_click_install_and_uninstall_entrypoints_remain():
    install = read(INSTALL_CMD)
    uninstall = read(UNINSTALL_CMD)
    assert 'Install-Revit2020.ps1' in install
    assert '-SourceRoot' in install
    assert 'Install-Revit2020.ps1' in uninstall
    assert '-Uninstall' in uninstall


def test_only_unified_workflow_owns_official_sdk_stdio_verification():
    workflow = read(WORKFLOW)
    assert not STDIO_WORKFLOW.exists()
    assert "tools/BIMBaoGui.McpSmoke/BIMBaoGui.McpSmoke.csproj" in workflow
    assert "Initialize server, list tools and call a read-only tool" in workflow
    assert "dotnet run" in workflow


def test_mcp_workflow_builds_one_complete_installable_zip():
    workflow = read(WORKFLOW)
    for text in (
        'dotnet build src/BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.csproj',
        'dotnet publish src/BIMBaoGui.McpServer/BIMBaoGui.McpServer.csproj',
        'tests/test_revit_addin_mcp_installer_contract.py',
        'installer/McpProbe.cmd',
        'installer/mcp-server-config.example.json',
        'BIMBaoGui.McpContracts.dll',
        'BIMBaoGui.McpServer.exe',
        'Install-Revit2020.ps1',
        'SHA256SUMS.txt',
        'name: BIMBaoGui-Revit2020-Native-MCP-v0.3.2',
    ):
        assert text in workflow


def test_readme_states_condition_gate_and_hifc_verification_boundary():
    source = read(README)
    assert "产品版本：0.3.2" in source
    assert "项目条件" in source
    assert "无上述项目条件（已确认）" in source
    assert "Stage01、Stage02" in source
    assert "不能证明 H-IFC 已识别" in source
