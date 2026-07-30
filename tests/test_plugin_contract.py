from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def test_compiled_gha_project_exists():
    project = ROOT / "src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj"
    assert project.exists()
    text = project.read_text(encoding="utf-8")
    assert "<TargetFramework>net48</TargetFramework>" in text
    assert "Grasshopper" in text
    assert "Revit_All_Main_Versions_API_x64" in text
    assert "BIMBaoGui.Stage01.gha" in text


def test_single_custom_component_and_attributes_exist():
    component = read("src/BIMBaoGui.Stage01/Stage01Component.cs")
    attributes = read("src/BIMBaoGui.Stage01/UI/Stage01ComponentAttributes.cs")
    assert "class Stage01Component : GH_Component" in component
    assert "class Stage01ComponentAttributes : GH_ComponentAttributes" in attributes
    assert "湖北BIM报规" in component
    assert "文件初始化" in component
    assert "InlineEditor" in attributes


def test_direct_revit_api_and_rhinoinside_host_detection_exist():
    host = read("src/BIMBaoGui.Stage01/Revit/RevitHost.cs")
    service = read("src/BIMBaoGui.Stage01/Revit/Stage01RevitService.cs")
    assert "Autodesk.Revit.DB" in service
    assert "TransactionGroup" in service
    assert "RhinoInside.Revit.Revit" in host
    assert "ActiveDBDocument" in host
    assert "EnqueueAction" in host


def test_registry_is_embedded_and_old_ghx_is_not_product():
    project = read("src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj")
    assert "stage01_file_initialization_registry_v0.1.json" in project
    assert "EmbeddedResource" in project
    assert not (ROOT / "gh/01_文件初始化.ghx").exists()


def test_component_has_no_external_inputs_and_exposes_workflow_outputs():
    component = read("src/BIMBaoGui.Stage01/Stage01Component.cs")
    assert "RegisterInputParams" in component
    assert re.search(r"RegisterInputParams\([^)]*\)\s*\{\s*\}", component, re.S)
    for output in ("初始化通过", "状态", "文件上下文", "消息"):
        assert output in component


def test_revit2020_blank_gate_does_not_reference_inaccessible_datastorage_type():
    gate = read("src/BIMBaoGui.Stage01/Revit/BlankFileGate.cs")
    assert "element is DataStorage" not in gate
    assert "element.Category == null" in gate
