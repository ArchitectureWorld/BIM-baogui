import hashlib
import json
import re
import subprocess
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
BASELINE = ROOT / "specs/revit-addin/v0.4.3-functional-baseline.json"


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def tracked_sources(roots: list[str]) -> list[str]:
    result = subprocess.run(
        ["git", "ls-files", "--", *roots],
        cwd=ROOT,
        check=True,
        capture_output=True,
        text=True,
        encoding="utf-8",
    )
    return sorted(
        path.replace("\\", "/")
        for path in result.stdout.splitlines()
        if path.endswith(".cs")
        and "bin/" not in path.replace("\\", "/")
        and "obj/" not in path.replace("\\", "/")
    )


def test_revit_manual_and_mcp_product_matches_v043_frozen_baseline():
    manifest = json.loads(BASELINE.read_text(encoding="utf-8"))
    assert manifest["schema_version"] == "BIMBAOGUI_REVIT_FUNCTIONAL_BASELINE_V4"
    assert manifest["product_line"] == "BIMBaoGui Revit 2020 Native + MCP"
    assert manifest["product_version"] == "0.4.3"
    assert manifest["payload_schema_version"] == "0.9.1"
    assert manifest["source_branch"] == "feat/revit-native-total-plan-phase1-v0.4.3"
    assert manifest["delivery"] == {
        "external_acceptance": "Golden RVT -> official HIFCTool -> IFCFlux exact identity",
        "installer_artifact": "BIMBaoGui-Revit2020-Native-MCP-v0.4.3",
        "single_revit_branch": "feat/revit-native-total-plan-phase1-v0.4.3",
        "target": "Autodesk Revit 2020",
    }

    roots = manifest["roots"]
    assert roots == [
        "src/BIMBaoGui.HifcCore",
        "src/BIMBaoGui.RevitAddin",
        "src/BIMBaoGui.McpContracts",
        "src/BIMBaoGui.McpServer",
    ]
    expected_hashes = manifest["sha256_by_path"]
    assert list(expected_hashes) == sorted(expected_hashes)
    assert tracked_sources(roots) == sorted(expected_hashes)
    snapshot = "".join(
        f"{path}\0{digest}\n" for path, digest in expected_hashes.items()
    ).encode("utf-8")
    assert manifest["source_snapshot_sha256"] == sha256(snapshot)

    for path, expected_hash in expected_hashes.items():
        assert re.fullmatch(r"[0-9a-f]{64}", expected_hash)
        assert sha256((ROOT / path).read_bytes()) == expected_hash


def test_v043_baseline_freezes_native_total_plan_boundaries():
    manifest = json.loads(BASELINE.read_text(encoding="utf-8"))
    assert manifest["capabilities"] == [
        "Stage01 freezes total-plan registration, coordinates, and planning objectives.",
        "Stage02A freezes whole-model or user-selected processing, explicit semantic confirmation, reliable element area, and per-rule geometry evidence.",
        "Stage02B freezes six manually entered actual indicators with per-indicator partial success and no project-level automatic aggregation.",
        "Stage03 freezes four-state strict checklist, issue traceability, and reasoned forced test export.",
        "The MCP surface contains exactly 13 approved business tools and exposes no arbitrary Revit API execution.",
        "Official carrier projection is fail-closed by propertyId.",
    ]
