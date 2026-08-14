from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
VIEW = ROOT / "src" / "BIMBaoGui.RevitAddin" / "Stage02" / "NativeStage02View.cs"


def source() -> str:
    return VIEW.read_text(encoding="utf-8")


def test_stage02_exposes_manual_semantic_assignment_without_pagination():
    text = source()
    for label in (
        "自动识别",
        "手动指定",
        "批量语义类型",
        "继承批量选择",
        "恢复自动识别",
        "当前 Revit 选择",
        "确认写入",
    ):
        assert label in text
    assert "NativeStage02ManualRoleCatalog.Current" in text
    assert "RoleOverrides" in text
    assert "NativeStage02RoleAssignmentPolicy.AutoOverrideRoleId" in text
    assert "new ScrollViewer" in text
    assert "分页" not in text


def test_semantic_control_changes_invalidate_confirmed_preview():
    text = source()
    assert "_previewStale = true" in text
    assert "_resolvedRequest = null" in text
    assert "_writeButton.IsEnabled = false" in text
    assert "请重新生成预览" in text
    assert "!_previewStale" in text


def test_stage02_ui_does_not_surface_legacy_catch_all_selection_error():
    text = source()
    assert "CUSTOM_ELEMENT_UNAVAILABLE" not in text
    assert "ElementKind=" in text
    assert "Revit 类别=" in text
