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


def test_stage01_geolocation_is_written_and_read_back_in_radians():
    write_source = read("NativeStage01RevitService.cs")
    read_source = read("NativeStage01RevitReadService.cs")
    assert "document.SiteLocation" in write_source
    assert "site.Longitude = geo.LongitudeRadians" in write_source
    assert "site.Latitude = geo.LatitudeRadians" in write_source
    assert "site.Longitude" in read_source
    assert "site.Latitude" in read_source
    assert "NativeStage01GeoLocationPolicy.FormatDegrees" in read_source


def test_stage01_persists_field_outcomes_and_workflow_result_in_same_group():
    source = read("NativeStage01RevitService.cs")
    assert "FieldOutcomes" in source
    assert "WorkflowResult" in source
    assert "NativeWorkflowResultCanonicalizer.Build" in source
    assert "NativeWorkflowResultStorage.Write" in source
    assert source.index("new TransactionGroup(") < source.index(
        "NativeWorkflowResultStorage.Write"
    ) < source.index("group.Assimilate()")


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
    assert "model.PlanningTargets" in source
    assert "target.Value1" in source


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


def test_stage01_current_read_never_silently_reconciles_condition_schema():
    source = read("NativeStage01RevitReadService.cs")
    storage = read("NativeStage01StoragePolicy.cs")
    migration = read("NativeStage01MigrationService.cs")

    assert "NativeStage01ConditionSchemaPolicy.Reconcile" not in source
    assert "NativeStage01ConditionSchemaPolicy.IsComplete" in storage
    assert "ConditionSchemaMismatch" in storage
    assert "NativeStage01ConditionSchemaPolicy.Reconcile" in migration
    assert "尚未写回 RVT" in migration


def test_unified_ci_runs_stage01_revit_contract():
    workflow = WORKFLOW.read_text(encoding="utf-8")
    assert "Verify native and MCP contracts" in workflow
    assert "tests/test_revit_addin_stage01_revit_contract.py" in workflow
