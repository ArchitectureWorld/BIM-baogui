import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "src" / "BIMBaoGui.RevitAddin"
STAGE02 = PROJECT / "Stage02"
WORKFLOW = ROOT / ".github" / "workflows" / "build-revit-addin.yml"


def read_stage02(name: str) -> str:
    return (STAGE02 / name).read_text(encoding="utf-8")


def test_stage02_preview_reads_live_revit_and_exact_parameter_evidence():
    source = read_stage02("NativeStage02RevitService.cs")
    assert "NativeStage01RevitReadService.Read" in source
    assert "FilteredElementCollector(document)" in source
    assert "Selection.GetElementIds" in source
    assert "NativeStage02InventoryPolicy.Resolve" in source
    assert "NativeStage02RoleMatcher.Match" in source
    assert "SharedParameterElement.Lookup" in source
    assert "document.ParameterBindings" in source
    assert "get_Parameter(property.ParameterGuid)" in source
    assert "NativeStage02ValueCodec.Read" in source
    assert "NativeStage02PreviewCompiler.Compile" in source
    assert "DocumentFingerprint" in source


def test_stage02_write_rebuilds_preview_and_allows_partial_success():
    source = read_stage02("NativeStage02RevitWriteService.cs")
    assert "RebuildPreview" in source
    assert "PreviewHash" in source
    assert "NativeStage02ParameterBindingService.Ensure" in source
    assert re.search(
        r'new\s+Transaction\s*\(\s*document\s*,\s*"HBR Stage02 参数',
        source,
    )
    assert re.search(
        r'new\s+Transaction\s*\(\s*document\s*,\s*"HBR Stage02 构件',
        source,
    )
    assert "NativeStage02ValueCodec.WriteAndVerify" in source
    assert "transaction.RollBack()" in source
    assert "continue;" in source
    assert "PartialSuccess" in source


def test_stage02_shared_parameters_preserve_fixed_guids_and_binding_scope():
    source = read_stage02("NativeStage02ParameterBindingService.cs")
    assert "property.ParameterGuid" in source
    assert "NativeStage02SharedParameterFile.Write" in source
    assert "SharedParameterElement.Lookup" in source
    assert "NewInstanceBinding" in source
    assert "NewTypeBinding" in source
    assert "ParameterBindings.ReInsert" in source
    assert "ParameterBindings.Insert" in source


def test_stage02_is_dispatched_only_through_revit_external_event():
    source = (PROJECT / "RevitExternalEventDispatcher.cs").read_text(
        encoding="utf-8"
    )
    assert "RequestStage02Preview" in source
    assert "RequestStage02Write" in source
    assert "NativeStage02RevitService.CreatePreview" in source
    assert "NativeStage02RevitWriteService.Execute" in source


def test_stage02_workspace_is_real_and_not_a_placeholder():
    view = read_stage02("NativeStage02View.cs")
    workspace = (PROJECT / "WorkspaceControl.cs").read_text(encoding="utf-8")
    assert "全模型" in view
    assert "当前 Revit 选择" in view
    assert "生成预览" in view
    assert "确认写入" in view
    assert "NativeStage02FieldStatus" in view
    assert "new ScrollViewer" in view
    assert "new NativeStage02View" in workspace
    assert "Stage02 等待开发" not in workspace


def test_stage02_detailed_status_is_fixed_height_and_scrollable():
    view = read_stage02("NativeStage02View.cs")
    assert "Height = new GridLength(96)" in view
    assert "Content = _statusText" in view
    assert "VerticalScrollBarVisibility = ScrollBarVisibility.Auto" in view
    assert "HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled" in view


def test_native_ci_runs_stage02_revit_contract():
    workflow = WORKFLOW.read_text(encoding="utf-8")
    assert "Verify native Stage02 Revit contract" in workflow
    assert (
        "python -m pytest tests/test_revit_addin_stage02_revit_contract.py -q"
        in workflow
    )
