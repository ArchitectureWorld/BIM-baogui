from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CONTRACTS = ROOT / "src" / "BIMBaoGui.McpContracts"
SERVER = ROOT / "src" / "BIMBaoGui.McpServer"
ADDIN = ROOT / "src" / "BIMBaoGui.RevitAddin"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def test_stage03_tools_are_approved_and_discoverable():
    contracts = read(CONTRACTS / "ToolContracts.cs")
    tools = read(SERVER / "BimBaoGuiTools.cs")
    names = (
        "bimbaogui_stage03_scan",
        "bimbaogui_stage03_export",
        "bimbaogui_stage03_get_last_result",
        "bimbaogui_stage03_revalidate_file",
    )
    for name in names:
        assert name in contracts
        assert name in tools


def test_stage03_bridge_routes_to_same_native_workflow():
    router = read(ADDIN / "McpBridge" / "McpBridgeCommandRouter.cs")
    adapter = read(ADDIN / "McpBridge" / "McpStage03Adapter.cs")
    gateway = read(ADDIN / "McpBridge" / "McpRevitCommandGateway.cs")
    dispatcher = read(ADDIN / "RevitExternalEventDispatcher.cs")
    assert "BridgeMethodNames.Stage03Scan" in router
    assert "BridgeMethodNames.Stage03Export" in router
    assert "BridgeMethodNames.Stage03GetLastResult" in router
    assert "BridgeMethodNames.Stage03RevalidateFile" in router
    assert "McpLeaseStore<NativeStage03ScanResult>" in adapter
    assert "_scanLeases.Consume(scanHash)" in adapter
    assert "ScanStage03Async" in gateway
    assert "ExportStage03Async" in gateway
    assert "NativeStage03WorkflowService.Scan" in dispatcher
    assert "NativeStage03WorkflowService.Execute" in dispatcher


def test_stage03_scan_carries_force_reason_and_output_then_export_consumes_lease():
    tools = read(SERVER / "BimBaoGuiTools.cs")
    adapter = read(ADDIN / "McpBridge" / "McpStage03Adapter.cs")
    router = read(ADDIN / "McpBridge" / "McpBridgeCommandRouter.cs")
    assert "scan_hash" in tools
    assert "output_directory" in tools
    assert "confirm" in tools
    assert "force_reason" in tools
    assert "new { mode, force_reason, output_directory }" in tools
    assert "force_reason" in router
    assert "output_directory" in router
    assert "ConfirmationRequired" in adapter
    assert "ForceReason = forceReason" in adapter
    assert "OutputDirectory = outputDirectory" in adapter
    assert "ShouldCreateLease(result)" in adapter


def test_stage03_mcp_projection_is_explicit_and_has_no_fixed_property_table():
    adapter = read(ADDIN / "McpBridge" / "McpStage03Adapter.cs")
    for key in (
        "normalized_output_directory",
        "preflight_hash",
        "official_acceptance_manifest",
        "official_acceptance_revit_readbacks",
        "checklist_counts",
        "checklist",
        "is_test_export",
        "counts_as_normal_export_pass",
        "official_acceptance_status",
    ):
        assert f'["{key}"]' in adapter
    assert "manifest?.Properties" in adapter
    assert "result?.OfficialAcceptanceRevitReadbacks" in adapter
    assert "result?.Checklist" in adapter


def test_stage03_does_not_expose_arbitrary_revit_execution():
    text = "\n".join(
        read(path)
        for path in (
            CONTRACTS / "ToolContracts.cs",
            SERVER / "BimBaoGuiTools.cs",
            ADDIN / "McpBridge" / "McpBridgeCommandRouter.cs",
        )
    ).lower()
    for forbidden in (
        "execute_csharp",
        "execute_revit_api",
        "arbitrary_transaction",
        "run_script",
    ):
        assert forbidden not in text
