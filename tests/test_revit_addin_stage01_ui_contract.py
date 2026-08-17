from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "src" / "BIMBaoGui.RevitAddin"
WORKFLOW = ROOT / ".github" / "workflows" / "build-revit-mcp.yml"


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
    assert "确认当前文件尚未开始正式建模" not in source
    assert "_confirmBlankProject" not in source
    assert "ConfirmBlankProject = false" in source
    assert "AllowReinitialize" in source
    assert "new Transaction(" not in source


def test_project_conditions_are_the_first_required_declaration_step():
    view = read(PROJECT / "Stage01" / "NativeStage01View.cs")
    view_model = read(PROJECT / "Stage01" / "NativeStage01ViewModel.cs")
    policy = read(
        PROJECT / "Stage01" / "NativeProjectConditionDeclarationPolicy.cs"
    )
    assert "无上述项目条件（已确认）" in view
    assert "项目条件为必填声明" in view
    assert "SetNoConditions" in view
    assert "NativeProjectConditionDeclarationPolicy.SetActualCondition" in view_model
    assert "NativeProjectConditionDeclarationPolicy.SetNoConditions" in view_model
    assert "Groups.First" not in view_model
    assert "groups.Add(ConditionsGroup)" in view_model
    assert "NoneConditionId" in policy
    assert "PROJECT_CONDITION" not in policy


def test_stage01_required_fields_precede_one_remembered_optional_expander():
    source = read(PROJECT / "Stage01" / "NativeStage01View.cs")
    assert "new Expander" in source
    assert "选填项（共 " in source
    assert "_optionalExpansionByGroup" in source
    assert "GetOptionalFieldCount" in source
    assert "GetFilledOptionalFieldCount" in source
    assert "HasOptionalValidationError" in source
    assert ".Where(NativeStage01Validator.IsRequired)" in source
    assert "!NativeStage01Validator.IsRequired" in source
    assert "NativeStage01ViewModel.ConditionsGroup" in source


def test_total_plan_field_cards_expose_fixed_sections_and_status_sources():
    source = read(PROJECT / "Stage01" / "NativeStage01View.cs")
    for label in (
        "项目登记信息",
        "项目位置与坐标",
        "规划目标与限值",
        "其他项目输入",
    ):
        assert label in source
    assert "规划目标/限值" in source
    assert "转到 02B 填写" in source
    assert "presentation.Source" in source
    assert "presentation.ReadbackState" in source


def test_total_building_area_routes_to_exact_stage02b_metric():
    source = read(PROJECT / "Stage01" / "NativeStage01View.cs")
    workspace = read(PROJECT / "WorkspaceControl.cs")
    assert 'NavigateToMetric("ca21e324-046b-5bfd-84c8-0d3470082303")' in source
    assert "NavigateToMetric" in workspace


def test_stage_status_regions_cannot_grow_and_displace_the_workspace():
    stage01 = read(PROJECT / "Stage01" / "NativeStage01View.cs")
    workspace = read(PROJECT / "WorkspaceControl.cs")
    assert "Height = new GridLength(96)" in stage01
    assert "Content = _statusText" in stage01
    assert "VerticalScrollBarVisibility = ScrollBarVisibility.Auto" in stage01
    assert "HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled" in stage01
    assert "Height = new GridLength(32)" in workspace
    assert "TextWrapping = TextWrapping.NoWrap" in workspace
    assert "TextTrimming = TextTrimming.CharacterEllipsis" in workspace
    assert "ToolTip = fullStatus" in workspace


def test_external_event_dispatcher_is_the_only_ui_to_revit_write_bridge():
    source = read(PROJECT / "RevitExternalEventDispatcher.cs")
    assert "RequestStage01Read" in source
    assert "RequestStage01Write" in source
    assert "NativeStage01RevitReadService.Read" in source
    assert "NativeStage01RevitService.Execute" in source


def test_workspace_hosts_real_stage01_stage02_and_stage03_products():
    source = read(PROJECT / "WorkspaceControl.cs")
    assert "NativeStage01View" in source
    assert "NativeStage02View" in source
    assert "NativeStage03View" in source
    assert "new NativeStage02View" in source
    assert "new NativeStage03View" in source
    assert "02A 构件与属性准备" in source
    assert "03 检测与 H-IFC" in source
    assert "Stage02 等待开发" not in source
    assert "Stage03 等待开发" not in source
    assert "Stage03 将作为独立原生模块继续开发" not in source


def test_unified_ci_runs_stage01_ui_contract():
    workflow = read(WORKFLOW)
    assert "Verify native and MCP contracts" in workflow
    assert "tests/test_revit_addin_stage01_ui_contract.py" in workflow
