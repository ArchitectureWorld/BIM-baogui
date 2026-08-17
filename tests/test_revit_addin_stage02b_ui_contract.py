from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "src" / "BIMBaoGui.RevitAddin"
STAGE02B = PROJECT / "Stage02B"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def test_workspace_navigation_is_the_fixed_five_item_sequence():
    source = read(PROJECT / "WorkspaceControl.cs")
    labels = (
        "01 项目初始化",
        "02A 构件与属性准备",
        "02B 项目实际指标",
        "03 检测与 H-IFC",
        "问题中心",
    )
    positions = [source.index(label) for label in labels]
    assert positions == sorted(positions)
    assert "01 文件初始化" not in source
    assert "02 构件与属性准备" not in source


def test_workspace_injects_one_shared_issue_hub_into_all_workspaces():
    source = read(PROJECT / "WorkspaceControl.cs")
    assert "new NativeIssueHub()" in source
    assert "new NativeStage02View(_issueHub)" in source
    assert "new NativeStage02BView(_issueHub)" in source
    assert "new NativeStage03View(_issueHub)" in source
    assert "new NativeIssueCenterView(" in source
    assert "_issueHub," in source
    assert "NavigateToMetric" in source
    assert "NavigateToField" in source


def test_stage02b_renders_exactly_catalog_six_rows_and_only_two_actions():
    source = read(STAGE02B / "NativeStage02BView.cs")
    model = read(STAGE02B / "NativeStage02BViewModel.cs")
    for label in (
        "指标名称",
        "完整 identity",
        "单位",
        "人工输入",
        "上次成功值",
        "本次状态",
        "官方载体状态",
        "保存全部",
        "仅重试失败项",
    ):
        assert label in source
    assert "NativeStage02BMetricCatalog.Current.MetricsFor" in model
    assert "Assert" not in source
    assert "BuildSaveAllRequest" in model
    assert "BuildRetryRequest" in model
    assert "ApplyRead" in model
    assert "ApplyWrite" in model
    assert "NavigateToMetric" in source
    for forbidden in ("模型扫描", "ElementId", "构件选择", "PickElements"):
        assert forbidden not in source


def test_stage03_legacy_constructors_delegate_to_shared_hub_constructor():
    source = read(PROJECT / "Stage03" / "NativeStage03View.cs")
    assert "NativeStage03View(NativeIssueHub hub)" in source
    assert "NativeStage03OutputDirectoryStore store, NativeIssueHub hub" in source
    assert ": this(new NativeStage03OutputDirectoryStore(), new NativeIssueHub())" in source
    assert ": this(store, new NativeIssueHub())" in source
