from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "src" / "BIMBaoGui.RevitAddin" / "Stage01" / "NativeStage01Storage.cs"
WORKFLOW = ROOT / ".github" / "workflows" / "build-revit-addin.yml"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def test_storage_uses_the_existing_cross_product_schema_contract():
    source = read(SOURCE)
    assert "d17f35b6-f42a-4d8f-9592-c7639b8bd320" in source
    assert 'StorageName = "HBR_BIMBAOGUI_STAGE01"' in source
    for field in (
        "PayloadJson",
        "PayloadHash",
        "FileGuid",
        "WorkflowVersion",
        "InitializedUtc",
    ):
        assert f'"{field}"' in source


def test_storage_is_a_persistence_adapter_not_a_transaction_owner():
    source = read(SOURCE)
    assert "DataStorage.Create" in source
    assert "SetEntity" in source
    assert "new Transaction(" not in source
    assert "new TransactionGroup(" not in source
    assert "ParameterBindings" not in source


def test_native_ci_runs_stage01_storage_contract():
    workflow = read(WORKFLOW)
    assert "Verify native Stage01 storage contract" in workflow
    assert (
        "python -m pytest tests/test_revit_addin_stage01_storage_contract.py -q"
        in workflow
    )
