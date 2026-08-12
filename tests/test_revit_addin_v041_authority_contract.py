from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
STAGE01 = ROOT / "src" / "BIMBaoGui.RevitAddin" / "Stage01"
MCP = ROOT / "src" / "BIMBaoGui.RevitAddin" / "McpBridge" / "McpStage01Adapter.cs"
WORKFLOW = ROOT / ".github" / "workflows" / "build-revit-mcp.yml"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def test_read_service_uses_state_specific_field_authority():
    source = read(STAGE01 / "NativeStage01RevitReadService.cs")

    assert "NativeStage01LiveEvidence" in source
    assert "NativeStage01FieldAuthorityPolicy.ApplyInitialValues" in source
    assert "NativeStage01FieldAuthorityPolicy.Compare" in source
    assert "NativeStage01StorageState.NoRecord" in source
    assert "NativeStage01StorageState.Current" in source
    assert "NativeStage01StorageState.MigratableLegacy" in source
    assert "SetIfBlank" not in source


def test_live_capture_preserves_axis_meaning():
    source = read(STAGE01 / "NativeStage01RevitReadService.cs")

    x_index = source.index("evidence.BaseX")
    y_index = source.index("evidence.BaseY")
    assert "position.NorthSouth" in source[x_index: x_index + 300]
    assert "position.EastWest" in source[y_index: y_index + 300]
    assert "X（南北）" in source
    assert "Y（东西）" in source


def test_field_authority_models_are_explicit_and_do_not_include_business_guessing():
    policy = read(STAGE01 / "NativeStage01FieldAuthorityPolicy.cs")
    evidence = read(STAGE01 / "NativeStage01LiveEvidence.cs")

    for token in (
        "NativeStage01Drift",
        "ApplyInitialValues",
        "Compare",
        "NativeStage01Keys.ProjectName",
        "NativeStage01Keys.ProjectNumber",
        "NativeStage01Keys.BaseX",
        "NativeStage01Keys.BaseY",
        "NativeStage01Keys.BaseElevation",
        "NativeStage01Keys.TrueNorthAngle",
    ):
        assert token in policy
    for property_name in (
        "ProjectName",
        "ProjectNumber",
        "BaseX",
        "BaseY",
        "BaseElevation",
        "TrueNorthAngle",
        "LengthUnit",
        "AreaUnit",
        "AngleUnit",
    ):
        assert property_name in evidence
    assert "SubitemName" not in evidence
    assert "Organizations" not in evidence
    assert "PlanningTargets" not in evidence


def test_view_and_mcp_expose_migration_live_evidence_and_drift():
    view = read(STAGE01 / "NativeStage01View.cs")
    adapter = read(MCP)

    for label in ("上次确认值", "当前 RVT 值", "已变化"):
        assert label in view
    for key in (
        '["payload_schema_version"]',
        '["source_payload_version"]',
        '["requires_migration_confirmation"]',
        '["live_evidence"]',
        '["drifts"]',
        '["project_information_available"]',
        '["project_position_available"]',
        '["units_available"]',
        '["stored_authority"]',
    ):
        assert key in adapter


def test_revit_ci_runs_authority_contract():
    workflow = read(WORKFLOW)
    assert "tests/test_revit_addin_v041_authority_contract.py" in workflow


def test_live_evidence_availability_is_explicitly_captured():
    source = read(STAGE01 / "NativeStage01RevitReadService.cs")

    assert "ProjectInformationAvailable = true" in source
    assert "ProjectPositionAvailable = true" in source
    assert "UnitsAvailable = true" in source


def test_no_record_keeps_read_only_target_units_instead_of_copying_live_units():
    policy = read(STAGE01 / "NativeStage01FieldAuthorityPolicy.cs")

    assert "ApplyAsInitial" in policy
    unit_section = policy[policy.index("NativeStage01Keys.LengthUnit") :]
    assert "ApplyAsInitial = false" in unit_section



def test_no_record_status_copy_distinguishes_live_units_from_target_units():
    source = read(STAGE01 / "NativeStage01RevitReadService.cs")

    assert "单位作为新表单初值" not in source
    assert "单位保持工作流目标" in source

def test_mcp_read_projects_payloads_through_a_guarded_version_aware_helper():
    adapter = read(MCP)

    assert "TryProjectPayload" in adapter
    assert "NativeStage01StorageState.Corrupt" in adapter
    assert "NativeStage01StorageState.UnsupportedFuture" in adapter
    assert "NativeStage01Canonicalizer.ToJson(model, modelVersion)" in adapter
    assert "return string.Empty" in adapter
    assert '["project_information_available"]' in adapter
    assert '["project_position_available"]' in adapter
    assert '["units_available"]' in adapter
    assert '["code"] = value.Code' in adapter
    assert '["stored_authority"] = value.StoredAuthority' in adapter
