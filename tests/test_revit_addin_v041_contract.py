from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ADDIN = ROOT / "src/BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.csproj"
HIFC = ROOT / "src/BIMBaoGui.HifcCore/BIMBaoGui.HifcCore.csproj"
CONTRACTS = ROOT / "src/BIMBaoGui.McpContracts/BIMBaoGui.McpContracts.csproj"
SERVER = ROOT / "src/BIMBaoGui.McpServer/BIMBaoGui.McpServer.csproj"
WORKSPACE = ROOT / "src/BIMBaoGui.RevitAddin/WorkspaceControl.cs"
WORKFLOW = ROOT / ".github/workflows/build-revit-mcp.yml"
INSTALLER = ROOT / "installer/Install-Revit2020.ps1"
PROBE = ROOT / "installer/McpProbe.cmd"
CONFIG = ROOT / "installer/mcp-server-config.example.json"
README = ROOT / "docs/revit-addin/README.md"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def test_all_four_product_projects_are_versioned_041():
    for path in (ADDIN, HIFC, CONTRACTS, SERVER):
        source = read(path)
        assert "<Version>0.4.1</Version>" in source
        assert "<FileVersion>0.4.1.0</FileVersion>" in source
        assert "<AssemblyVersion>0.4.1.0</AssemblyVersion>" in source


def test_addin_build_embeds_runtime_build_and_commit_metadata():
    source = read(ADDIN)
    assert "HbrBuildNumber" in source
    assert "HbrCommitSha" in source
    assert "<InformationalVersion>$(Version)+build.$(HbrBuildNumber).sha.$(HbrCommitSha)</InformationalVersion>" in source
    assert '<AssemblyMetadata Include="HBR.BuildNumber" Value="$(HbrBuildNumber)" />' in source
    assert '<AssemblyMetadata Include="HBR.CommitSha" Value="$(HbrCommitSha)" />' in source


def test_workspace_displays_loaded_plugin_identity_at_top():
    source = read(WORKSPACE)
    assert "PluginRuntimeIdentity.Read" in source
    assert "插件版本" in source
    assert "构建号" in source
    assert "Commit" in source
    assert "DLL 路径" in source
    assert "HorizontalScrollBarVisibility = ScrollBarVisibility.Auto" in source
    assert "IsReadOnly = true" in source


def test_ci_injects_and_verifies_build_identity_and_packages_041():
    source = read(WORKFLOW)
    assert "HbrBuildNumber=${{ github.run_number }}" in source
    assert "HbrCommitSha=${{ github.sha }}" in source
    assert 'metadata["HBR.BuildNumber"]' in source
    assert 'metadata["HBR.CommitSha"]' in source
    assert '0.4.1.0' in source
    assert "BIMBaoGui-Revit2020-Native-MCP-v0.4.1" in source


def test_installer_probe_config_and_readme_use_041():
    assert '$mcpVersion = "0.4.1"' in read(INSTALLER)
    assert "McpServer\\0.4.1" in read(PROBE)
    assert "McpServer\\\\0.4.1" in read(CONFIG)
    assert "产品版本：0.4.1" in read(README)


def test_bridge_and_stage03_reports_use_the_loaded_addin_identity():
    bridge = read(ROOT / "src/BIMBaoGui.RevitAddin/McpBridge/McpBridgeHost.cs")
    reports = read(ROOT / "src/BIMBaoGui.RevitAddin/Stage03/NativeStage03ReportWriter.cs")

    assert "PluginRuntimeIdentity.Read" in bridge
    assert "PluginRuntimeIdentity.Read" in reports
    assert '?? "0.4.0"' not in bridge
    assert '["product_version"] = "0.4.0"' not in reports
    assert '["build_number"]' in reports
    assert '["commit_sha"]' in reports


def test_stage01_failure_reports_use_the_normalized_loaded_product_version():
    service = read(ROOT / "src/BIMBaoGui.RevitAddin/Stage01/NativeStage01RevitService.cs")

    assert "PluginRuntimeIdentity.Read" in service
    assert "assembly.GetName().Version" not in service


def test_readme_documents_payload_migration_and_field_authority_boundaries():
    source = read(README)

    assert "0.9.0 → 0.9.1" in source
    assert "等待迁移确认" in source
    assert "读取动作不会改写原 Storage" in source
    assert "现场值漂移" in source
    assert "不会静默覆盖" in source
    assert "m / m² / °" in source


def test_ci_reflects_all_four_product_assemblies_as_041():
    source = read(WORKFLOW)

    assert "Verify unified product assembly versions" in source
    for assembly_path in (
        "src/BIMBaoGui.HifcCore/bin/Release/net48/BIMBaoGui.HifcCore.dll",
        "src/BIMBaoGui.RevitAddin/bin/Release/net48/BIMBaoGui.RevitAddin.dll",
        "src/BIMBaoGui.RevitAddin/bin/Release/net48/BIMBaoGui.McpContracts.dll",
        "src/BIMBaoGui.McpServer/bin/Release/net8.0/win-x64/BIMBaoGui.McpServer.dll",
    ):
        assert assembly_path in source
    assert 'Version.ToString() -ne "0.4.1.0"' in source


def test_installer_evidence_records_loaded_dll_version_build_and_commit_identity():
    installer = read(INSTALLER)
    workflow = read(WORKFLOW)

    assert "Get-RevitAssemblyIdentity" in installer
    assert "AssemblyInformationalVersionAttribute" in installer
    assert '"HBR.BuildNumber"' in installer
    assert '"HBR.CommitSha"' in installer
    assert "assemblyInformationalVersion" in installer
    assert "buildNumber" in installer
    assert "commitSha" in installer
    assert '$evidence.productVersion -ne "0.4.1"' in workflow
    assert '$evidence.buildNumber -ne "${{ github.run_number }}"' in workflow
    assert '$evidence.commitSha -ne "${{ github.sha }}"' in workflow
