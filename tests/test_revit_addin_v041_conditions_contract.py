from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
STAGE01 = ROOT / "src" / "BIMBaoGui.RevitAddin" / "Stage01"
RULES = ROOT / "src" / "BIMBaoGui.RevitAddin" / "Rules" / "NativeRuleCatalog.cs"
MCP = ROOT / "src" / "BIMBaoGui.RevitAddin" / "McpBridge" / "McpStage01Adapter.cs"
WORKFLOW = ROOT / ".github" / "workflows" / "build-revit-mcp.yml"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def test_default_and_migration_never_auto_confirm_no_conditions():
    catalog = read(RULES)
    migration = read(STAGE01 / "NativeStage01MigrationService.cs")

    assert "model.SetCondition(condition.ConditionId, false)" in catalog
    assert "NativeProjectConditionDeclarationPolicy.NoneConditionId" in catalog
    assert "false" in catalog
    assert "NativeProjectConditionDeclarationPolicy.NoneConditionId" in migration
    assert "false" in migration
    for forbidden in (
        "defaultToNoneWhenEmpty: true",
        "NormalizeLoadedDeclaration",
    ):
        assert forbidden not in catalog
        assert forbidden not in migration


def test_condition_policy_detects_conflict_and_only_user_operations_resolve_it():
    policy = read(STAGE01 / "NativeProjectConditionDeclarationPolicy.cs")
    schema = read(STAGE01 / "NativeStage01ConditionSchemaPolicy.cs")

    assert "NativeProjectConditionDeclarationState.Missing" in policy
    assert "NativeProjectConditionDeclarationState.Conflict" in policy
    assert "SetActualCondition" in policy
    assert "SetNoConditions" in policy
    assert "model.SetCondition(NoneConditionId, false)" in policy
    assert "model.SetCondition(NoneConditionId, selected)" in policy
    assert "NativeProjectConditionDeclarationPolicy.SetNoConditions" not in schema
    assert "NativeProjectConditionDeclarationPolicy.SetActualCondition" not in schema


def test_ui_and_mcp_share_explicit_declaration_gate():
    view = read(STAGE01 / "NativeStage01View.cs")
    adapter = read(MCP)

    assert "项目条件为必填声明" in view
    assert "无上述项目条件（已确认）" in view
    assert "NativeProjectConditionDeclarationPolicy" in view
    assert "NativeProjectConditionDeclarationPolicy" in adapter
    assert "NativeStage01Validator" in adapter
    assert ".Validate(" in adapter
    assert '["required"] = true' in adapter
    assert '["exclusive_with_actual_conditions"] = true' in adapter


def test_mcp_form_schema_exposes_runtime_condition_defaults_as_unselected():
    adapter = read(MCP)
    actual_projection = adapter.split("conditions.Add", 1)[0]

    assert '["default_active"] = false' in actual_projection
    assert '["catalog_default_active"] = value.DefaultActive' in actual_projection
    assert '["default_active"] = value.DefaultActive' not in actual_projection


def test_revit_ci_runs_conditions_contract():
    workflow = read(WORKFLOW)
    assert "tests/test_revit_addin_v041_conditions_contract.py" in workflow


def test_validator_rejects_wrong_payload_version_and_missing_none_schema_key():
    validator = read(STAGE01 / "NativeStage01Validator.cs")

    assert "PayloadVersionMismatch" in validator
    assert "NativeStage01Canonicalizer.PayloadSchemaVersion" in validator
    assert "NativeProjectConditionDeclarationPolicy.NoneConditionId" in validator
    assert "ConditionMissing" in validator
