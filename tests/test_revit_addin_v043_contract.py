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
V042_BASELINE = ROOT / "specs/revit-addin/v0.4.2-functional-baseline.json"
BRANCH = "feat/revit-native-total-plan-phase1-v0.4.3"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8-sig")


def test_all_product_and_installer_versions_are_043():
    for project in PRODUCT_PROJECTS:
        text = read(project)
        assert "<Version>0.4.3</Version>" in text
        assert "<FileVersion>0.4.3.0</FileVersion>" in text
        assert "<AssemblyVersion>0.4.3.0</AssemblyVersion>" in text

    assert '$mcpVersion = "0.4.3"' in read(INSTALLER)
    assert "McpServer\\0.4.3" in read(PROBE)
    assert "McpServer\\\\0.4.3" in read(CONFIG)
    assert "产品版本：0.4.3" in read(README)
    assert BRANCH in read(README)
    workflow = read(WORKFLOW)
    assert "BIMBaoGui-Revit2020-Native-MCP-v0.4.3" in workflow
    assert "build_hbr_rulepack_v043.py" in workflow
    assert BRANCH in workflow


def test_workflow_live_release_contract_is_043_only():
    workflow = read(WORKFLOW)

    assert 'Version.ToString() -ne "0.4.3.0"' in workflow
    assert '$evidence.productVersion -ne "0.4.3"' in workflow
    assert '0.4.3+build.${{ github.run_number }}.sha.${{ github.sha }}' in workflow
    assert '"BIMBaoGui\\McpServer\\0.4.0"' in workflow
    assert '"BIMBaoGui\\McpServer\\0.4.1"' in workflow
    assert '"BIMBaoGui\\McpServer\\0.4.2"' in workflow
    assert '"BIMBaoGui\\McpServer\\9.9.9"' in workflow
    assert "tests/test_revit_addin_v043_contract.py" in workflow
    assert "tests/test_revit_addin_workflow_result_contract.py" in workflow
    assert "tests/test_revit_addin_stage02b_revit_contract.py" in workflow
    assert "tests/test_revit_addin_stage02b_ui_contract.py" in workflow
    assert "tests/test_hbr_rulepack_v043.py" in workflow

    for stale_live_literal in (
        'Version.ToString() -ne "0.4.2.0"',
        '$evidence.productVersion -ne "0.4.2"',
        '"0.4.2+build.${{ github.run_number }}.sha.${{ github.sha }}"',
        "BIMBaoGui-Revit2020-Native-MCP-v0.4.2",
        '"BIMBaoGui\\McpServer\\0.4.2"\n            $mcpExe',
    ):
        assert stale_live_literal not in workflow


def test_workflow_covers_v043_release_inputs():
    workflow = read(WORKFLOW)
    for path in (
        "specs/hbr-rules/v1/source/hbr_rule_source.v0.4.3-overlay.json",
        "tools/build_hbr_rulepack_v043.py",
        "src/BIMBaoGui.RevitAddin/Stage02B/**",
        "src/BIMBaoGui.RevitAddin/Workflow/**",
        "src/BIMBaoGui.RevitAddin/Issues/**",
        "tests/test_hbr_rulepack_v043.py",
    ):
        assert path in workflow


def test_functional_baseline_builder_is_deterministic_and_preserves_v042(tmp_path: Path):
    first = tmp_path / "first.json"
    second = tmp_path / "second.json"
    v042_before = V042_BASELINE.read_bytes()
    command = [
        sys.executable,
        str(BUILDER),
        "--version",
        "0.4.3",
        "--branch",
        BRANCH,
    ]

    subprocess.run(command + ["--output", str(first)], cwd=ROOT, check=True)
    subprocess.run(command + ["--output", str(second)], cwd=ROOT, check=True)

    assert first.read_bytes() == second.read_bytes()
    assert V042_BASELINE.read_bytes() == v042_before
    manifest = json.loads(first.read_text(encoding="utf-8"))
    assert manifest["schema_version"] == "BIMBAOGUI_REVIT_FUNCTIONAL_BASELINE_V4"
    assert manifest["product_version"] == "0.4.3"
    assert manifest["source_branch"] == BRANCH
    assert manifest["delivery"]["external_acceptance"] == (
        "Golden RVT -> official HIFCTool -> IFCFlux exact identity"
    )
    snapshot = "".join(
        f"{path}\0{digest}\n"
        for path, digest in manifest["sha256_by_path"].items()
    ).encode("utf-8")
    assert manifest["source_snapshot_sha256"] == hashlib.sha256(snapshot).hexdigest()


def test_functional_baseline_builder_rejects_unreleased_versions(tmp_path: Path):
    result = subprocess.run(
        [
            sys.executable,
            str(BUILDER),
            "--version",
            "0.4.4",
            "--branch",
            BRANCH,
            "--output",
            str(tmp_path / "invalid.json"),
        ],
        cwd=ROOT,
        check=False,
        capture_output=True,
        text=True,
    )

    assert result.returncode != 0
    assert "unsupported functional baseline version" in result.stderr
