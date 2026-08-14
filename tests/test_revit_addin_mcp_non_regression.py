import hashlib
import json
import os
import re
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[1]
BASELINE = ROOT / "specs" / "revit-addin" / "v0.4.1-functional-baseline.json"
V042_DEVELOPMENT_BRANCH = "feat/revit-stage02-manual-semantic-v0.4.2"


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def source_paths(roots: list[str]) -> list[str]:
    paths: set[str] = set()
    for root in roots:
        directory = ROOT / root
        assert directory.is_dir(), f"baseline root does not exist: {root}"
        for path in directory.rglob("*.cs"):
            relative = path.relative_to(ROOT)
            if any(part in {"bin", "obj"} for part in relative.parts):
                continue
            paths.add(relative.as_posix())
    return sorted(paths)


def test_revit_manual_and_mcp_product_matches_current_baseline():
    if os.environ.get("GITHUB_REF_NAME", "") == V042_DEVELOPMENT_BRANCH:
        pytest.skip(
            "v0.4.2 development intentionally changes the frozen v0.4.1 source "
            "snapshot; refresh and re-enable this release gate only after the "
            "v0.4.2 functional surface is locked."
        )

    manifest = json.loads(BASELINE.read_text(encoding="utf-8"))
    assert manifest["schema_version"] == "BIMBAOGUI_REVIT_FUNCTIONAL_BASELINE_V3"
    assert manifest["product_line"] == "BIMBaoGui Revit 2020 Native + MCP"
    assert manifest["product_version"] == "0.4.1"
    assert manifest["payload_schema_version"] == "0.9.1"

    roots = manifest["roots"]
    assert roots == [
        "src/BIMBaoGui.HifcCore",
        "src/BIMBaoGui.RevitAddin",
        "src/BIMBaoGui.McpContracts",
        "src/BIMBaoGui.McpServer",
    ]
    expected_hashes = manifest["sha256_by_path"]
    snapshot_bytes = "".join(
        f"{path}\0{expected_hashes[path]}\n"
        for path in sorted(expected_hashes)
    ).encode("utf-8")
    assert manifest["source_snapshot_sha256"] == sha256(snapshot_bytes)
    expected_paths = sorted(expected_hashes)
    current_paths = source_paths(roots)
    assert current_paths == expected_paths

    drift = []
    for path in expected_paths:
        expected_hash = expected_hashes[path]
        assert re.fullmatch(r"[0-9a-f]{64}", expected_hash)
        actual_hash = sha256((ROOT / path).read_bytes())
        if expected_hash != actual_hash:
            drift.append(
                {
                    "path": path,
                    "expected_sha256": expected_hash,
                    "actual_sha256": actual_hash,
                }
            )
    assert drift == []


def test_v041_baseline_records_the_revised_semantic_boundaries():
    manifest = json.loads(BASELINE.read_text(encoding="utf-8"))
    capabilities = set(manifest["capabilities"])

    for required in (
        "Stage01 project conditions remain explicit user business declarations; none is never auto-confirmed.",
        "Stage01 payload 0.9.0 is validated before an in-memory 0.9.1 migration candidate is created.",
        "Revit-native fields use per-field authority and drift evidence instead of one global overwrite priority.",
        "Stage02 and Stage03 consume only Current Stage01 storage, never an unconfirmed migration candidate.",
        "IFCFlux external status remains IFCFLUX_MANUAL_PENDING until user inspection.",
        "The MCP surface contains 13 approved business tools and exposes no arbitrary Revit API execution.",
    ):
        assert required in capabilities

    delivery = manifest["delivery"]
    assert delivery["single_revit_branch"] == "feat/revit-native-addin-mcp-v0.3"
    assert delivery["installer_artifact"] == (
        "BIMBaoGui-Revit2020-Native-MCP-v0.4.1"
    )
    assert delivery["target"] == "Autodesk Revit 2020"
    assert delivery["external_acceptance"] == "IFCFlux manual inspection"
