import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
VIEW = ROOT / "src" / "BIMBaoGui.RevitAddin" / "Stage02" / "NativeStage02View.cs"
REQUEST_POLICY = (
    ROOT
    / "src"
    / "BIMBaoGui.RevitAddin"
    / "Stage02"
    / "NativeStage02WorkbenchRequestPolicy.cs"
)


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
    assert "_roleOverrides" in text
    assert "NativeStage02WorkbenchRequestPolicy.Build" in text
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


def test_stage02_ui_uses_the_shared_canonical_request_policy():
    text = source()
    policy = REQUEST_POLICY.read_text(encoding="utf-8")
    assert "NativeStage02WorkbenchRequestPolicy.Build" in text
    assert "OrderBy(value => value.ElementUniqueId, StringComparer.Ordinal)" in policy
    assert "NativeStage02IdentificationMode.Automatic" in policy
    assert "NativeStage02IdentificationMode.Manual" in policy


def test_stage02a_ui_is_candidate_confirmation_then_write_preview():
    text = source()
    for label in (
        "选择范围",
        "生成候选",
        "待确认",
        "批量接受当前候选",
        "刷新写入预览",
        "确认写入",
        "几何来源",
        "当前面积",
        "几何检查",
        "批准",
        "拒绝",
        "复核人",
        "依据",
    ):
        assert label in text
    assert "NativeStage02RoleConfirmation" in text
    assert "Confirmations" in text
    assert "CreatePreview" in text
    assert "NativeStage02ManualReviewStorage" in text
    assert "人工复核：不适用" not in text
    assert "人工复核：本期未实现" not in text


def test_batch_accept_only_updates_confirmations_and_refreshes_preview():
    text = source()
    batch_handler = re.search(
        r"private\s+void\s+BatchAcceptCandidates\s*\(\s*\).*?"
        r"(?=\n\s*private\s+void\s+AcceptCandidate)",
        text,
        re.S,
    )
    assert batch_handler is not None
    body = batch_handler.group(0)
    assert "Confirmations" in body
    assert "CreatePreview" in body or "RequestPreview" in body
    assert "RequestStage02Write" not in body
