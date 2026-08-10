import re
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def read(relative_path: str) -> str:
    path = ROOT / relative_path
    assert path.is_file(), f"missing Task 10 production file: {relative_path}"
    return path.read_text(encoding="utf-8")


def method_body(text: str, signature: str) -> str:
    start = text.index(signature)
    brace = text.index("{", start)
    depth = 0
    for index in range(brace, len(text)):
        if text[index] == "{":
            depth += 1
        elif text[index] == "}":
            depth -= 1
            if depth == 0:
                return text[brace : index + 1]
    raise AssertionError(f"unclosed method: {signature}")


def registered_labels(method: str) -> list[str]:
    matches = re.finditer(
        r'pManager\.Add(?:\w*Parameter)\(\s*'
        r'(?:new\s+\w+\(\)\s*,\s*)?"([^"]+)"',
        method,
    )
    return [match.group(1) for match in matches]


def test_public_stage03_has_exact_name_default_mode_and_primary_exposure():
    component = read(
        "src/BIMBaoGui.Stage01/Stage03ValidationExportComponent.cs"
    )
    assert '"湖北BIM报规｜03 检测、导出与 H-IFC 转译"' in component
    assert re.search(
        r"private\s+const\s+bool\s+DefaultStrictMode\s*=\s*true\s*;",
        component,
    )
    assert "GH_Exposure.primary" in component
    assert re.search(
        r"public\s+override\s+Guid\s+ComponentGuid\s*=>\s*"
        r"new\s+Guid\(\"[0-9a-fA-F-]{36}\"\)",
        component,
    )
    assert 'new Guid("9bf87680-c1dc-499a-b267-33a430ee4201")' in component


def test_public_stage03_has_exact_five_inputs_and_eight_outputs():
    component = read(
        "src/BIMBaoGui.Stage01/Stage03ValidationExportComponent.cs"
    )
    inputs = method_body(component, "protected override void RegisterInputParams")
    outputs = method_body(component, "protected override void RegisterOutputParams")
    assert registered_labels(inputs) == [
        "文件上下文",
        "执行",
        "输出目录",
        "全部通过才导出",
        "强制原因",
    ]
    assert registered_labels(outputs) == [
        "允许导出",
        "字段通过",
        "全部阻断",
        "RAW IFC",
        "HIFC-MVD IFC",
        "fields JSON",
        "规则哈希",
        "状态",
    ]
    assert re.search(
        r'AddTextParameter\(\s*"字段通过"[\s\S]*?GH_ParamAccess\.tree',
        outputs,
    )


def test_stage03_rejects_relative_output_directory_before_full_path_resolution():
    component = read(
        "src/BIMBaoGui.Stage01/Stage03ValidationExportComponent.cs"
    )
    preflight = method_body(component, "private static bool TryCreateRequest")
    assert re.search(
        r"!Path\.IsPathRooted\(\s*outputDirectory\s*\)",
        preflight,
    )


def test_public_stage03_uses_rising_edge_signature_and_generation_state():
    component = read(
        "src/BIMBaoGui.Stage01/Stage03ValidationExportComponent.cs"
    )
    state = read(
        "src/BIMBaoGui.Stage01/Stage03/Stage03ComponentStatePolicy.cs"
    )
    assert "Stage03ComponentInputSignature" in component
    assert "Stage03ComponentRunToken" in component
    assert "TryBegin" in component
    assert "TryPublish" in component
    assert "ObserveExecution" in state
    assert "Generation" in state
    assert "Signature" in state
    assert "OriginalForceReason" in state
    assert "Stage03GateMode.Strict" in component
    assert "Stage03GateMode.Force" in component


def test_public_stage03_card_shows_mode_counts_state_and_three_paths():
    component = read(
        "src/BIMBaoGui.Stage01/Stage03ValidationExportComponent.cs"
    )
    attributes = read(
        "src/BIMBaoGui.Stage01/UI/Stage03ComponentAttributes.cs"
    )
    presentation = read(
        "src/BIMBaoGui.Stage01/Stage03/Stage03ComponentStatePolicy.cs"
    )
    assert "new Stage03ComponentAttributes(this)" in component
    for text in [
        "严格门禁｜全部通过后导出",
        "测试放行｜缺陷仍写入报告",
    ]:
        assert text in presentation
    assert "Stage03ComponentPresentationPolicy.ModeDescription" in attributes
    for text in [
        "字段",
        "运行支持",
        "运行状态",
        "RAW IFC",
        "HIFC-MVD IFC",
        "fields JSON",
    ]:
        assert text in attributes


def test_stage03_runtime_support_uses_one_database_decision_and_ui_only_counts_snapshot():
    component = read(
        "src/BIMBaoGui.Stage01/Stage03ValidationExportComponent.cs"
    )
    attributes = read(
        "src/BIMBaoGui.Stage01/UI/Stage03ComponentAttributes.cs"
    )
    scanner = read(
        "src/BIMBaoGui.Stage01/Revit/Stage03ModelScanService.cs"
    )
    build_view = method_body(
        component,
        "private Stage03ComponentViewState BuildViewStateLocked",
    )

    for name in [
        "RuntimeSupportedCount",
        "RuntimeNotImplementedCount",
        "RuntimeUnclassifiedRequirementCount",
        "RuntimeOfficialEvidenceOnlyCount",
    ]:
        assert name in component
    assert "field.RuntimeStatus" in build_view
    assert "field.Requirement" not in build_view
    assert "OwnerStrategy" not in build_view
    assert '"运行支持"' in attributes
    assert re.search(
        r"private\s+const\s+float\s+CardHeight\s*=\s*364f\s*;",
        attributes,
    )
    assert "230f" in method_body(attributes, "private void DrawBody")
    assert "_cardBounds.Y + 326f" in method_body(
        attributes,
        "private void DrawFooter",
    )
    assert scanner.count("_database.GetRuntimeStatusDecision(property)") == 1
    assert "runtimeDecisions[property.PropertyId]" in scanner


def test_stage03_force_with_business_defects_is_explicitly_orange():
    component = read(
        "src/BIMBaoGui.Stage01/Stage03ValidationExportComponent.cs"
    )
    attributes = read(
        "src/BIMBaoGui.Stage01/UI/Stage03ComponentAttributes.cs"
    )
    view_state = component[
        component.index("internal sealed class Stage03ComponentViewState")
        : component.index("public sealed class Stage03ValidationExportComponent")
    ]
    build_view = method_body(
        component,
        "private Stage03ComponentViewState BuildViewStateLocked",
    )
    footer = method_body(attributes, "private void DrawFooter")
    tone_color = method_body(attributes, "private static Color ResolveToneColor")

    assert "ForcedWithBusinessDefects" in view_state
    assert "BusinessBlockerCount" in view_state
    assert "businessBlockerCount" in build_view
    assert "Stage03ComponentPresentationPolicy.IsForcedWithBusinessDefects" in (
        build_view
    )
    assert "Stage03ComponentPresentationPolicy.ResolveTone" in footer
    assert "view.ForcedWithBusinessDefects" in footer
    assert re.search(
        r"Stage03ComponentStatusTone\.Warning\s*:\s*return\s+Warning\s*;",
        tone_color,
    )
    assert '"Strict｜' not in attributes
    assert '"Force｜' not in attributes


def test_stage03_card_reserves_left_and_right_port_channels():
    attributes = read(
        "src/BIMBaoGui.Stage01/UI/Stage03ComponentAttributes.cs"
    )
    layout = method_body(attributes, "protected override void Layout")
    assert re.search(
        r"private\s+const\s+float\s+InputChannelWidth\s*=\s*152f\s*;",
        attributes,
    )
    assert re.search(
        r"private\s+const\s+float\s+CardWidth\s*=\s*620f\s*;",
        attributes,
    )
    assert re.search(
        r"private\s+const\s+float\s+OutputChannelWidth\s*=\s*162f\s*;",
        attributes,
    )
    assert re.search(
        r"componentBox\.Width\s*=\s*InputChannelWidth\s*\+\s*"
        r"CardWidth\s*\+\s*OutputChannelWidth\s*;",
        layout,
    )
    assert re.search(
        r"_contentBounds\s*=\s*new\s+RectangleF\(\s*"
        r"componentBox\.Left\s*\+\s*InputChannelWidth,\s*"
        r"componentBox\.Top,\s*CardWidth,\s*CardHeight\s*\)\s*;",
        layout,
    )
    assert "_cardBounds = _contentBounds;" in layout
    assert "_cardBounds = componentBox;" not in layout
    assert "LayoutInputParams(Owner, componentBox);" in layout
    assert "LayoutOutputParams(Owner, componentBox);" in layout


def test_stage03_all_blockers_excludes_general_workflow_messages():
    component = read(
        "src/BIMBaoGui.Stage01/Stage03ValidationExportComponent.cs"
    )
    formatter = read(
        "src/BIMBaoGui.Stage01/Stage03/Stage03FieldDetailFormatter.cs"
    )
    publish = method_body(component, "private void PublishResult")
    solve = method_body(component, "protected override void SolveInstance")
    outputs = method_body(component, "protected override void RegisterOutputParams")
    assert "completed.Messages" not in publish
    assert re.search(
        r"FormatAllBlockers\(\s*completed\.GateDecision,\s*"
        r"completed\.TechnicalFatalCodes,\s*completed\.Diagnostics\s*\)",
        publish,
    )
    assert re.search(
        r"FormatAllBlockers\(\s*Stage03GateDecision\s+gate,\s*"
        r"IEnumerable<string>\s+technicalFatalCodes,\s*"
        r"IEnumerable<Stage03Diagnostic>\s+diagnostics\s*\)",
        formatter,
    )
    assert '"消息|' not in solve
    assert "输入签名已变化，旧 UI 结果已失效。" not in solve
    assert re.search(
        r'_status\s*=\s*"执行中"\s*;\s*'
        r"_blockers\s*=\s*Array\.Empty<string>\(\)\s*;",
        solve,
    )
    assert "输入检查失败、业务阻断、技术致命码和阻断级诊断" in outputs
    assert "诊断和消息" not in outputs


def test_stage03_component_failure_paths_emit_only_shared_json_records():
    component = read(
        "src/BIMBaoGui.Stage01/Stage03ValidationExportComponent.cs"
    )
    formatter = read(
        "src/BIMBaoGui.Stage01/Stage03/Stage03FieldDetailFormatter.cs"
    )
    solve = method_body(component, "protected override void SolveInstance")
    complete = method_body(component, "private async Task CompleteAsync")
    outputs = method_body(component, "protected override void RegisterOutputParams")

    for legacy_prefix in [
        '"输入阻断|',
        '"技术致命|',
        '"诊断|',
    ]:
        assert legacy_prefix not in solve
        assert legacy_prefix not in complete

    component_failure = method_body(
        formatter,
        "internal static IReadOnlyList<string> FormatComponentFailure",
    )

    assert solve.count(
        "Stage03FieldDetailFormatter.FormatComponentFailure("
    ) == 2
    assert re.search(
        r"_blockers\s*=\s*"
        r"Stage03FieldDetailFormatter\.FormatComponentFailure\("
        r"[\s\S]*?preflightError\s*\)\s*;",
        solve,
    )
    assert re.search(
        r"_blockers\s*=\s*"
        r"Stage03FieldDetailFormatter\.FormatComponentFailure\("
        r"[\s\S]*?startError\s*\)\s*;",
        solve,
    )
    assert complete.count(
        "Stage03FieldDetailFormatter.FormatComponentFailure("
    ) == 1
    assert re.search(
        r"ClearPublishedState\(\s*\"Stage03 失败\",\s*"
        r"Stage03FieldDetailFormatter\.FormatComponentFailure\(",
        complete,
    )
    assert "Stage03TechnicalFatalCodes.InvalidIfc" in complete
    assert "failure.Message" in complete
    assert "FormatAllBlockers(" in component_failure
    assert 'Severity = "ERROR"' in component_failure
    assert "每项均为稳定 JSON" in outputs


def test_stage03_blocking_diagnostic_policy_is_a_shared_boundary():
    policy = read(
        "src/BIMBaoGui.Stage01/Stage03/Stage03BlockingDiagnosticPolicy.cs"
    )
    formatter = read(
        "src/BIMBaoGui.Stage01/Stage03/Stage03FieldDetailFormatter.cs"
    )
    coordinator = read(
        "src/BIMBaoGui.Stage01/Stage03/Stage03WorkflowCoordinator.cs"
    )
    assert "internal static class Stage03BlockingDiagnosticPolicy" in policy
    assert "class Stage03BlockingDiagnosticPolicy" not in formatter
    assert "Stage03BlockingDiagnosticPolicy.IsBlocking" in formatter
    assert "Stage03BlockingDiagnosticPolicy.IsBlocking" in coordinator


def test_stage03_scan_maps_live_document_path_through_real_adapter():
    scanner = read(
        "src/BIMBaoGui.Stage01/Revit/Stage03ModelScanService.cs"
    )
    adapter = read(
        "src/BIMBaoGui.Stage01/Stage03/Stage03ProductionWorkflowServices.cs"
    )
    assert re.search(
        r"class\s+Stage03ModelScanResult[\s\S]*?"
        r"DocumentPath\s*\{\s*get;\s*set;\s*\}",
        scanner,
    )
    assert re.search(
        r"DocumentPath\s*=\s*document\.PathName\s*\?\?\s*string\.Empty",
        scanner,
    )
    assert "new Stage03WorkflowScanResult" in adapter
    assert re.search(
        r"DocumentPath\s*=\s*(?:scan|result)\.DocumentPath",
        adapter,
    )


def test_stage03_production_adapter_uses_host_seam_and_real_services():
    adapter = read(
        "src/BIMBaoGui.Stage01/Stage03/Stage03ProductionWorkflowServices.cs"
    )
    for required in [
        "HbrRuleDatabase.Current",
        "Stage03RevitPhaseService",
        "ScanInHostContext",
        "ExportInHostContext",
        "Stage03IfcTranslationService",
        "Stage03FieldReportWriter.Write",
        "Stage03FailureReportWriter.TryWrite",
    ]:
        assert required in adapter
    assert "Task.Run" not in adapter


def test_stage03_translator_is_atomic_re_read_and_provenance_preserving():
    translator = read(
        "src/BIMBaoGui.Stage01/Stage03/Stage03IfcTranslationService.cs"
    )
    for required in [
        "new UTF8Encoding(false, true)",
        "IfcStepDocument.Parse",
        "HbrIfcEnricher",
        "prePublishInspection",
        "FileMode.CreateNew",
        "Flush(true)",
        "reReadCandidateInspection",
        "File.Move",
        "Task.Run",
    ]:
        assert required in translator
    assert translator.count("Task.Run") == 1
    assert "Task.FromResult" not in translator
    for forbidden in [
        "File.Delete",
        "Directory.Delete",
        ".bak",
        ".backup",
        "MvdIfcFileService",
    ]:
        assert forbidden not in translator


def test_stage03_new_production_path_has_no_forbidden_execution_or_cleanup():
    translator = read(
        "src/BIMBaoGui.Stage01/Stage03/Stage03IfcTranslationService.cs"
    )
    non_translator = "\n".join(
        read(path)
        for path in [
            "src/BIMBaoGui.Stage01/Stage03ValidationExportComponent.cs",
            "src/BIMBaoGui.Stage01/UI/Stage03ComponentAttributes.cs",
            "src/BIMBaoGui.Stage01/Stage03/Stage03FieldDetailFormatter.cs",
            "src/BIMBaoGui.Stage01/Stage03/Stage03ComponentStatePolicy.cs",
            "src/BIMBaoGui.Stage01/Stage03/Stage03ProductionWorkflowServices.cs",
            "src/BIMBaoGui.Stage01/Stage03/Stage03WorkflowCoordinator.cs",
            "src/BIMBaoGui.Stage01/Revit/Stage03RevitPhaseService.cs",
            "src/BIMBaoGui.Stage01/Revit/Stage03ModelScanService.cs",
            "src/BIMBaoGui.Stage01/Revit/AutodeskIfcExportService.cs",
        ]
    )
    assert translator.count("Task.Run") == 1
    assert "Task.Run" not in non_translator
    combined = translator + "\n" + non_translator
    for forbidden in [
        "File.Delete",
        "Directory.Delete",
        ".bak",
        ".backup",
        "MvdIfcFileService",
    ]:
        assert forbidden not in combined


def test_legacy_stage03_and_stage04_are_hidden():
    legacy_stage03 = read(
        "src/BIMBaoGui.Stage01/Stage03OfficialHifcWriteComponent.cs"
    )
    legacy_stage04 = read(
        "src/BIMBaoGui.Stage01/Stage04MvdIfcNormalizeComponent.cs"
    )
    assert "GH_Exposure.hidden" in legacy_stage03
    assert "GH_Exposure.hidden" in legacy_stage04
