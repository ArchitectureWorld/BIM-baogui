import hashlib
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
BASELINE = ROOT / "specs/revit-addin/v0.4.2-functional-baseline.json"


def test_v042_baseline_remains_frozen_historical_evidence():
    manifest = json.loads(BASELINE.read_text(encoding="utf-8"))

    assert manifest["schema_version"] == "BIMBAOGUI_REVIT_FUNCTIONAL_BASELINE_V3"
    assert manifest["product_version"] == "0.4.2"
    assert manifest["source_branch"] == "feat/revit-stage02-manual-semantic-v0.4.2"
    assert manifest["delivery"]["installer_artifact"] == (
        "BIMBaoGui-Revit2020-Native-MCP-v0.4.2"
    )
    snapshot = "".join(
        f"{path}\0{digest}\n"
        for path, digest in manifest["sha256_by_path"].items()
    ).encode("utf-8")
    assert manifest["source_snapshot_sha256"] == hashlib.sha256(snapshot).hexdigest()
