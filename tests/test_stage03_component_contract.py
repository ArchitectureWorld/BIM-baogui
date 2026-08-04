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
    assert "new Stage03ComponentAttributes(this)" in component
    for text in [
        "Strict",
        "Force",
        "字段",
        "运行状态",
        "RAW IFC",
        "HIFC-MVD IFC",
        "fields JSON",
    ]:
        assert text in attributes


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
