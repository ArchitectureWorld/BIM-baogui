import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REGISTRY = ROOT / "src/BIMBaoGui.Stage01/Resources/stage01_file_initialization_registry_v0.1.json"


def test_stage01_registry_counts_and_unique_keys():
    data = json.loads(REGISTRY.read_text(encoding="utf-8"))
    internal = data["internal_workflow_fields"]
    mvd = data["mvd_fields"]
    assert len(internal) == 12
    assert len(mvd) == 102
    keys = [item["field_key"] for item in internal + mvd]
    assert len(keys) == len(set(keys))


def test_registry_is_single_file_runtime_dependency():
    project = (ROOT / "src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj").read_text(encoding="utf-8")
    assert "EmbeddedResource" in project
    assert "stage01_file_initialization_registry_v0.1.json" in project
