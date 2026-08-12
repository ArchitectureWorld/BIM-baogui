from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
STAGE01 = ROOT / "src" / "BIMBaoGui.RevitAddin" / "Stage01"
WORKFLOW = ROOT / ".github" / "workflows" / "build-revit-mcp.yml"


def read(name: str) -> str:
    return (STAGE01 / name).read_text(encoding="utf-8")


def test_stage01_write_is_one_revit_undo_with_exact_readback_and_rollback():
    source = read("NativeStage01RevitService.cs")
    assert "new TransactionGroup(" in source
    assert "new Transaction(" in source
    assert "NativeStage01Storage.Write" in source
    assert "NativeStage01ParameterProjectionService.WriteAndVerify" in source
    assert "VerifyReadback" in source
    assert "group.RollBack()" in source
    assert "group.Assimilate()" in source
    assert "document.Regenerate()" in source


def test_project_position_freezes_x_as_northing_and_y_as_easting():
    source = read("NativeStage01RevitService.cs")
    assert "plan.NorthSouthMeters" in source
    assert "plan.EastWestMeters" in source
    assert "new ProjectPosition(" in source
    assert "eastWestInternal" in source
    assert "northSouthInternal" in source
    assert "position.NorthSouth" in source
    assert "position.EastWest" in source


def test_stage01_parameter_projection_is_database_driven_and_guid_exact():
    source = read("NativeStage01ParameterProjectionService.cs")
    assert "catalog.Stage01Fields" in source
    assert "field.ParameterGuid" in source
    assert "SharedParameterElement.Lookup" in source
    assert "OST_ProjectInformation" in source
    assert "WriteInStage01" in source
    assert "IfcProject" in source
    assert "GetOrganizationValue" not in source
    assert "Fixture" not in source


def test_stage01_failure_report_is_atomic_and_records_transaction_truth():
    source = read("NativeStage01FailureReportWriter.cs")
    assert "FileMode.CreateNew" in source
    assert "File.Move" in source
    assert "TransactionRolledBack" in source
    assert "OperationStage" in source
    assert "RulePackageSha256" in source


def test_stage01_first_initialization_does_not_scan_or_block_existing_model():
    service = read("NativeStage01RevitService.cs")
    preflight = read("NativeStage01WritePreflight.cs")
    assert "NativeStage01BlankModelGate.FindBlockingElements" not in service
    assert "BlankConfirmationRequired" not in preflight
    assert "ModelNotBlank" not in preflight


def test_unified_ci_runs_stage01_revit_contract():
    workflow = WORKFLOW.read_text(encoding="utf-8")
    assert "Verify native and MCP contracts" in workflow
    assert "tests/test_revit_addin_stage01_revit_contract.py" in workflow
