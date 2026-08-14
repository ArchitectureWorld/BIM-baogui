import hashlib
import json
import subprocess
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
PRODUCT_PROJECTS = (
    ROOT / "src/BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.csproj",
    ROOT / "src/BIMBaoGui.HifcCore/BIMBaoGui.HifcCore.csproj",
    ROOT / "src/BIMBaoGui.McpContracts/BIMBaoGui.McpContracts.csproj",
    ROOT / "src/BIMBaoGui.McpServer/BIMBaoGui.McpServer.csproj",
)
INSTALLER = ROOT / "installer/Install-Revit2020.ps1"
PROBE = ROOT / "installer/McpProbe.cmd"
CONFIG = ROOT / "installer/mcp-server-config.example.json"
README = ROOT / "docs/revit-addin/README.md"
WORKFLOW = ROOT / ".github/workflows/build-revit-mcp.yml"
BUILDER = ROOT / "tools/build_revit_functional_baseline.py"
WORKSPACE = ROOT / "src/BIMBaoGui.RevitAddin/WorkspaceControl.cs"
BRANCH = "feat/revit-stage02-manual-semantic-v0.4.2"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8-sig")


def test_every_shipped_surface_uses_v042_identity():
    for project in PRODUCT_PROJECTS:
        text = read(project)
        assert "<Version>0.4.2</Version>" in text
        assert "<FileVersion>0.4.2.0</FileVersion>" in text
        assert "<AssemblyVersion>0.4.2.0</AssemblyVersion>" in text

    assert '$mcpVersion = "0.4.2"' in read(INSTALLER)
    assert "McpServer\\0.4.2" in read(PROBE)
    assert "McpServer\\\\0.4.2" in read(CONFIG)
    assert "产品版本：0.4.2" in read(README)
    assert "BIMBaoGui-Revit2020-Native-MCP-v0.4.2" in read(WORKFLOW)


def test_workflow_verifies_v042_and_seeds_both_superseded_mcp_versions():
    workflow = read(WORKFLOW)

    assert 'Version.ToString() -ne "0.4.2.0"' in workflow
    assert '$evidence.productVersion -ne "0.4.2"' in workflow
    assert '"BIMBaoGui\\McpServer\\0.4.0"' in workflow
    assert '"BIMBaoGui\\McpServer\\0.4.1"' in workflow
    assert "tests/test_revit_addin_v042_contract.py" in workflow
    assert "tests/test_revit_addin_v041_contract.py" not in workflow


def test_functional_baseline_builder_is_deterministic(tmp_path: Path):
    first = tmp_path / "first.json"
    second = tmp_path / "second.json"
    command = [
        sys.executable,
        str(BUILDER),
        "--version",
        "0.4.2",
        "--branch",
        BRANCH,
    ]

    subprocess.run(command + ["--output", str(first)], cwd=ROOT, check=True)
    subprocess.run(command + ["--output", str(second)], cwd=ROOT, check=True)

    assert first.read_bytes() == second.read_bytes()
    manifest = json.loads(first.read_text(encoding="utf-8"))
    assert manifest["product_version"] == "0.4.2"
    assert manifest["source_branch"] == BRANCH
    assert list(manifest["sha256_by_path"]) == sorted(
        manifest["sha256_by_path"]
    )
    snapshot = "".join(
        f"{path}\0{digest}\n"
        for path, digest in manifest["sha256_by_path"].items()
    ).encode("utf-8")
    assert manifest["source_snapshot_sha256"] == hashlib.sha256(
        snapshot
    ).hexdigest()


def test_addin_embeds_and_displays_loaded_runtime_identity():
    project = read(PRODUCT_PROJECTS[0])
    workspace = read(WORKSPACE)

    assert "HbrBuildNumber" in project
    assert "HbrCommitSha" in project
    assert "$(Version)+build.$(HbrBuildNumber).sha.$(HbrCommitSha)" in project
    assert "PluginRuntimeIdentity.Read" in workspace
    for label in ("插件版本", "构建号", "Commit", "DLL 路径"):
        assert label in workspace


def test_loaded_identity_flows_to_bridge_reports_and_install_evidence():
    bridge = read(ROOT / "src/BIMBaoGui.RevitAddin/McpBridge/McpBridgeHost.cs")
    reports = read(
        ROOT / "src/BIMBaoGui.RevitAddin/Stage03/NativeStage03ReportWriter.cs"
    )
    installer = read(INSTALLER)

    assert "PluginRuntimeIdentity.Read" in bridge
    assert "PluginRuntimeIdentity.Read" in reports
    assert '["build_number"]' in reports
    assert '["commit_sha"]' in reports
    assert "Get-RevitAssemblyIdentity" in installer
    assert "AssemblyInformationalVersionAttribute" in installer
    assert '"HBR.BuildNumber"' in installer
    assert '"HBR.CommitSha"' in installer


def test_readme_preserves_payload_migration_and_manual_acceptance_boundaries():
    source = read(README)

    for text in (
        "0.9.0 → 0.9.1",
        "等待迁移确认",
        "读取动作不会改写原 Storage",
        "现场值漂移",
        "不会静默覆盖",
        "INTERNAL_VALIDATED",
        "IFCFLUX_MANUAL_PENDING",
        "IFCFlux",
    ):
        assert text in source
