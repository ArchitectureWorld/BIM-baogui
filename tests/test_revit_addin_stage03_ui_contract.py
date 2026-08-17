from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "src" / "BIMBaoGui.RevitAddin"
STAGE03 = PROJECT / "Stage03"
README = ROOT / "docs" / "revit-addin" / "README.md"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def test_stage03_workspace_is_real_and_exposes_manual_ifcflux_flow():
    view = read(STAGE03 / "NativeStage03View.cs")
    workspace = read(PROJECT / "WorkspaceControl.cs")
    for label in (
        "扫描与预检",
        "导出并转译",
        "重新校验结果",
        "打开导出位置文件夹",
        "严格模式",
        "强制测试模式",
        "IFCFlux",
    ):
        assert label in view
    assert "new NativeStage03View" in workspace
    assert "Stage03 等待开发" not in workspace
    assert "_stage03Placeholder" not in workspace


def test_stage03_output_directory_is_model_scoped_and_force_reason_ui_is_removed():
    view = read(STAGE03 / "NativeStage03View.cs")
    store = read(STAGE03 / "NativeStage03OutputDirectoryStore.cs")
    workspace = read(PROJECT / "WorkspaceControl.cs")

    assert "强制原因" not in view
    assert "_forceReason" not in view
    assert "BIMBaoGui_HIFC_Output" not in view
    assert "ApplyDocumentPath" in view
    assert "_stage03View.ApplyDocumentPath" in workspace
    assert "stage03-output-directories.json" in store
    assert "LocalApplicationData" in store
    assert "StringComparer.OrdinalIgnoreCase" in store
    assert "NormalizeDocumentPath" in store
    assert "Remember(" in store
    assert "Resolve(" in store


def test_stage03_uses_external_event_for_all_revit_work():
    dispatcher = read(PROJECT / "RevitExternalEventDispatcher.cs")
    view = read(STAGE03 / "NativeStage03View.cs")
    assert "RequestStage03Scan" in dispatcher
    assert "RequestStage03Export" in dispatcher
    assert "RequestStage03Revalidate" in dispatcher
    assert "NativeStage03WorkflowService.Scan" in dispatcher
    assert "NativeStage03WorkflowService.Execute" in dispatcher
    assert "NativeStage03WorkflowService.RevalidateFile" in dispatcher
    assert "new Transaction(" not in view
    assert "Document.Export" not in view


def test_stage03_report_region_has_fixed_height_and_internal_scroll():
    view = read(STAGE03 / "NativeStage03View.cs")
    assert "new GridLength(96)" in view
    assert "VerticalScrollBarVisibility = ScrollBarVisibility.Auto" in view
    assert "TextWrapping = TextWrapping.Wrap" in view


def test_stage03_checklist_has_fixed_columns_colours_and_recheck_contract():
    view = read(STAGE03 / "NativeStage03View.cs")
    models = read(STAGE03 / "NativeStage03Models.cs")
    compiler = read(STAGE03 / "NativeStage03IssueCompiler.cs")
    presentation = read(STAGE03 / "NativeStage03ChecklistPresentation.cs")
    workspace = read(PROJECT / "WorkspaceControl.cs")

    for column in (
        "检查项名称", "来源阶段", "适用依据", "当前值", "状态", "问题说明", "处理入口"
    ):
        assert column in view
    for colour in ("#FFE5E7EB", "#FFDCFCE7", "#FFFEE2E2", "#FFFEF3C7"):
        assert colour in presentation
    assert "复查该项" in view
    assert "重新读取完整清单" in view
    assert "FocusCheckId" in models
    assert "FocusCheckId = focusCheckId" in view
    assert "RequestStage03Scan" in view
    assert "NativeStage03IssueCompiler.Compile" in view
    assert "Navigate(NativeIssueRecord issue)" in workspace
    assert "_stage02View.NavigateToIssue(issue)" in workspace
    assert "NavigateToCheck(issue.CheckId)" in workspace
    assert "item.Elements" in compiler
    assert "item.ElementUniqueId" in compiler
    assert "_initialChecklist" in view
    assert "CreateInitialChecklist" in view
    assert "?? _initialChecklist" in view


def test_installable_readme_matches_the_stage03_interaction_contract():
    readme = read(README)
    assert "强制测试模式不再要求填写原因" in readme
    assert "按 Revit 模型规范化完整路径分别保存" in readme
    assert "stage03-output-directories.json" in readme
    assert "打开导出位置文件夹" in readme
