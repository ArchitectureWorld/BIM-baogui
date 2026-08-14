from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
CONTRACTS = ROOT / "src" / "BIMBaoGui.McpContracts" / "ToolContracts.cs"
TOOLS = ROOT / "src" / "BIMBaoGui.McpServer" / "BimBaoGuiTools.cs"
ROUTER = (
    ROOT
    / "src"
    / "BIMBaoGui.RevitAddin"
    / "McpBridge"
    / "McpBridgeCommandRouter.cs"
)
ADAPTER = (
    ROOT
    / "src"
    / "BIMBaoGui.RevitAddin"
    / "McpBridge"
    / "McpStage02Adapter.cs"
)


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def test_stage02_preview_dto_and_tool_expose_semantic_assignment_inputs():
    contracts = read(CONTRACTS)
    tools = read(TOOLS)
    assert "Stage02RoleOverrideCommand" in contracts
    assert "IdentificationMode" in contracts
    assert "BulkRoleId" in contracts
    assert "RoleOverrides" in contracts
    for parameter in (
        "string identification_mode",
        "string bulk_role_id",
        "IReadOnlyList<Stage02RoleOverrideCommand> role_overrides",
    ):
        assert parameter in tools
    assert tools.count("[McpServerTool(") == 13


def test_stage02_semantic_inputs_reach_shared_preview_request_path():
    router = read(ROUTER)
    adapter = read(ADAPTER)
    for field in (
        "payload.scope",
        "payload.identification_mode",
        "payload.bulk_role_id",
        "payload.role_overrides",
    ):
        assert field in router
    assert "NativeStage02WorkbenchRequestPolicy.Build" in adapter
    assert "BridgeErrorCodes.InvalidArgument" in adapter
    assert 'case "automatic"' in adapter
    assert 'case "manual"' in adapter


def test_stage02_preview_v2_projects_canonical_and_assignment_evidence():
    adapter = read(ADAPTER)
    for output in (
        "schema_version",
        "canonical_json",
        "identification_mode",
        "bulk_role_id",
        "automatic_role_id",
        "effective_role_id",
        "assignment_mode",
        "assignment_source",
        "assignment_action",
        "manual_carrier_evidence",
        "assigned_element_count",
        "removed_assignment_count",
        "failed_assignment_count",
    ):
        assert f'["{output}"]' in adapter
    assert "Consume(previewHash)" in adapter
    assert "BridgeErrorCodes.ConfirmationRequired" in adapter
