from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ADDIN = ROOT / "src" / "BIMBaoGui.RevitAddin"
CONTRACTS = ROOT / "src" / "BIMBaoGui.McpContracts"
SERVER = ROOT / "src" / "BIMBaoGui.McpServer"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def test_revit_addin_keeps_existing_business_files_frozen_and_adds_bridge_sidecar():
    app = read(ADDIN / "App.cs")
    project = read(ADDIN / "BIMBaoGui.RevitAddin.csproj")
    assert "McpBridgeHost.Start" in app
    assert "McpBridgeHost.Stop" in app
    assert "RevitExternalEventDispatcher.EnsureInitialized" in app
    assert "return Result.Succeeded" in app
    assert "BIMBaoGui.McpContracts.csproj" in project
    for forbidden in ("Grasshopper", "RhinoCommon", "RhinoInside"):
        assert forbidden not in project


def test_pipe_bridge_is_current_user_authenticated_and_size_limited():
    pipe = read(ADDIN / "McpBridge" / "McpNamedPipeServer.cs")
    host = read(ADDIN / "McpBridge" / "McpBridgeHost.cs")
    assert "NamedPipeServerStream" in pipe
    assert "PipeSecurity" in pipe
    assert "WindowsIdentity.GetCurrent().User" in pipe
    assert "PipeAccessRule" in pipe
    assert "PipeAccessRights.FullControl" in pipe
    assert "BridgeProtocol.MaxRequestBytes" in pipe
    assert "BridgeProtocol.MaxResponseBytes" in pipe
    assert "SessionToken" in host
    assert "RandomNumberGenerator" in host


def test_bridge_routes_revit_work_only_through_existing_external_event_dispatcher():
    gateway = read(ADDIN / "McpBridge" / "McpRevitCommandGateway.cs")
    assert "TaskCompletionSource" in gateway
    assert "RunContinuationsAsynchronously" in gateway
    for method in (
        "RequestDocumentSnapshot",
        "RequestStage01Read",
        "RequestStage01Write",
        "RequestStage02Preview",
        "RequestStage02Write",
    ):
        assert method in gateway
    assert "new Transaction(" not in gateway


def test_router_exposes_only_approved_business_methods():
    router = read(ADDIN / "McpBridge" / "McpBridgeCommandRouter.cs")
    for method in (
        "BridgeMethodNames.Ping",
        "BridgeMethodNames.DocumentStatus",
        "BridgeMethodNames.RulePackageIdentity",
        "BridgeMethodNames.Stage01FormSchema",
        "BridgeMethodNames.Stage01Read",
        "BridgeMethodNames.Stage01Validate",
        "BridgeMethodNames.Stage01Write",
        "BridgeMethodNames.Stage02Preview",
        "BridgeMethodNames.Stage02Write",
    ):
        assert method in router
    assert "BridgeErrorCodes.UnknownMethod" in router
    for forbidden in (
        "execute_csharp",
        "execute_revit_api",
        "run_script",
        "click_ui",
        "arbitrary_transaction",
    ):
        assert forbidden not in router.lower()


def test_stage01_mcp_schema_exposes_required_condition_declaration():
    adapter = read(ADDIN / "McpBridge" / "McpStage01Adapter.cs")
    assert '["default_active_group"] = NativeStage01ViewModel.ConditionsGroup' in adapter
    assert '["condition_declaration"]' in adapter
    assert '["required"] = true' in adapter
    assert "NativeProjectConditionDeclarationPolicy.NoneConditionId" in adapter
    assert "NativeProjectConditionDeclarationPolicy.NoneDisplayName" in adapter
    assert '["exclusive_with_actual_conditions"] = true' in adapter
    assert '["declaration_option"] = "actual"' in adapter
    assert '["declaration_option"] = "none"' in adapter


def test_official_sdk_stdio_server_is_self_contained_and_logs_to_stderr():
    project = read(SERVER / "BIMBaoGui.McpServer.csproj")
    program = read(SERVER / "Program.cs")
    tools = read(SERVER / "BimBaoGuiTools.cs")
    assert "<TargetFramework>net8.0</TargetFramework>" in project
    assert "<RuntimeIdentifier>win-x64</RuntimeIdentifier>" in project
    assert "<SelfContained>true</SelfContained>" in project
    assert "<PublishSingleFile>true</PublishSingleFile>" in project
    assert "<PublishTrimmed>false</PublishTrimmed>" in project
    assert 'Include="ModelContextProtocol" Version="1.3.0"' in project
    assert "WithStdioServerTransport" in program
    assert "WithToolsFromAssembly" in program
    assert "LogToStandardErrorThreshold" in program
    assert "McpServerToolType" in tools
    assert tools.count("[McpServerTool(") == 9
    for tool_name in (
        "bimbaogui_list_revit_sessions",
        "bimbaogui_get_document_status",
        "bimbaogui_get_rule_package_identity",
        "bimbaogui_stage01_get_form_schema",
        "bimbaogui_stage01_read",
        "bimbaogui_stage01_validate",
        "bimbaogui_stage01_write",
        "bimbaogui_stage02_preview",
        "bimbaogui_stage02_write",
    ):
        assert tool_name in tools


def test_write_tools_require_confirmation_and_hash_leases():
    tools = read(SERVER / "BimBaoGuiTools.cs")
    stage01 = read(ADDIN / "McpBridge" / "McpStage01Adapter.cs")
    stage02 = read(ADDIN / "McpBridge" / "McpStage02Adapter.cs")
    assert "bool confirm" in tools
    assert "validation_hash" in tools
    assert "preview_hash" in tools
    assert "BridgeErrorCodes.ConfirmationRequired" in stage01
    assert "BridgeErrorCodes.ConfirmationRequired" in stage02
    assert "Consume(validationHash)" in stage01
    assert "Consume(previewHash)" in stage02
    assert "NativeStage02RevitWriteService" not in tools
