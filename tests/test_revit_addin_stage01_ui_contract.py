from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "src" / "BIMBaoGui.RevitAddin"
WORKFLOW = ROOT / ".github" / "workflows" / "build-revit-addin.yml"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def test_stage01_workspace_is_left_directory_plus_one_continuous_scroll_form():
    source = read(PROJECT / "Stage01" / "NativeStage01View.cs")
    assert "new ScrollViewer" in source
    assert "_directoryPanel" in source
    assert "_formPanel" in source
    assert "new TabControl" not in source
    assert "new Frame" not in source
    assert "PreviousPage" not in source
    assert "NextPage" not in source


def test_stage01_workspace_exposes_explicit_read_validate_and_write_actions():
    source = read(PROJECT / "Stage01" / "NativeStage01View.cs")
    for label in ("读取当前文件", "校验", "写入并回读"):
        assert label in source
    assert "RequestStage01Read" in source
    assert "RequestStage01Write" in source
    assert "ConfirmBlankProject" in source
    assert "AllowReinitialize" in source
    assert "new Transaction(" not in source


def test_external_event_dispatcher_is_the_only_ui_to_revit_write_bridge():
    source = read(PROJECT / "RevitExternalEventDispatcher.cs")
    assert "RequestStage01Read" in source
    assert "RequestStage01Write" in source
    assert "NativeStage01RevitReadService.Read" in source
    assert "NativeStage01RevitService.Execute" in source


def test_workspace_hosts_real_stage01_stage02_and_keeps_stage03_independent():
    source = read(PROJECT / "WorkspaceControl.cs")
    assert "NativeStage01View" in source
    assert "NativeStage02View" in source
    assert "new NativeStage02View" in source
    assert "02 构件与属性准备" in source
    assert "03 检测与 H-IFC" in source
    assert "Stage02 等待开发" not in source
    assert "Stage02 将作为独立原生模块继续开发" not in source
    assert "Stage03 将作为独立原生模块继续开发" in source


def test_native_ci_runs_stage01_ui_contract():
    workflow = read(WORKFLOW)
    assert "Verify native Stage01 UI contract" in workflow
    assert (
        "python -m pytest tests/test_revit_addin_stage01_ui_contract.py -q"
        in workflow
    )
