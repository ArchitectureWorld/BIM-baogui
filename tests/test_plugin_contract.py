import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def load_json(path: str):
    return json.loads(read(path))


def test_compiled_gha_project_exists():
    project = ROOT / "src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj"
    assert project.exists()
    text = project.read_text(encoding="utf-8")
    assert "<TargetFramework>net48</TargetFramework>" in text
    assert "Grasshopper" in text
    assert "Revit_All_Main_Versions_API_x64" in text
    assert "BIMBaoGui.Stage01.gha" in text
    assert "<Version>0.9.0</Version>" in text


def test_plugin_patch_and_file_context_schema_versions_are_explicit():
    assembly = read("src/BIMBaoGui.Stage01/AssemblyInfo.cs")
    versions = read("src/BIMBaoGui.Stage01/Context/HBRContextVersions.cs")
    payload = read("src/BIMBaoGui.Stage01/Core/CanonicalPayload.cs")
    assert 'public override string Version => "0.9.0"' in assembly
    context = read("src/BIMBaoGui.Stage01/Context/HBRFileContext.cs")
    canonicalizer = read("src/BIMBaoGui.Stage01/Context/HBRFileContextCanonicalizer.cs")
    factory = read("src/BIMBaoGui.Stage01/Context/HBRFileContextFactory.cs")
    assert 'FileContextSchema = "0.9.0"' in versions
    assert "OfficialProtocolCompatible" in context
    assert '"officialProtocolCompatible"' in canonicalizer
    assert "Stage01OfficialCompatibilityPolicy.Evaluate" in factory
    assert "HBRContextVersions.FileContextSchema" in payload


def test_stage01_custom_component_and_attributes_exist():
    component = read("src/BIMBaoGui.Stage01/Stage01Component.cs")
    attributes = read("src/BIMBaoGui.Stage01/UI/Stage01ComponentAttributes.cs")
    assert "class Stage01Component : GH_Component" in component
    assert "class Stage01ComponentAttributes : GH_ComponentAttributes" in attributes
    assert "湖北BIM报规" in component
    assert "文件初始化" in component
    assert '"湖北BIM报规｜01 文件初始化"' in component
    assert "InlineEditor" in attributes
    assert "PlanningTargetEditor" in attributes


def test_direct_revit_api_and_rhinoinside_host_detection_exist():
    host = read("src/BIMBaoGui.Stage01/Revit/RevitHost.cs")
    service = read("src/BIMBaoGui.Stage01/Revit/Stage01RevitService.cs")
    assert "Autodesk.Revit.DB" in service
    assert "TransactionGroup" in service
    assert "RhinoInside.Revit.Revit" in host
    assert "ActiveDBDocument" in host
    assert "InvokeInHostContext" in host


def test_registry_is_embedded_and_old_ghx_is_not_product():
    project = read("src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj")
    assert "stage01_file_initialization_registry_v0.1.json" in project
    assert "EmbeddedResource" in project
    assert not (ROOT / "gh/01_文件初始化.ghx").exists()


def test_stage01_has_no_external_inputs_and_exposes_strong_context_output():
    component = read("src/BIMBaoGui.Stage01/Stage01Component.cs")
    assert "RegisterInputParams" in component
    assert re.search(r"RegisterInputParams\([^)]*\)\s*\{\s*\}", component, re.S)
    assert "new HBRFileContextParam()" in component
    assert "new HBRFileContextGoo(context)" in component
    assert "供 02 构件与属性准备使用的强类型 HBR_FileContext。" in component
    assert "供 02 模型任务与骨架分流使用" not in component
    for output in ("初始化通过", "状态", "文件上下文", "消息", "上下文JSON"):
        assert output in component


def test_revit2020_blank_gate_does_not_reference_inaccessible_datastorage_type():
    gate = read("src/BIMBaoGui.Stage01/Revit/BlankFileGate.cs")
    assert "element is DataStorage" not in gate
    assert "element.Category == null" in gate


def test_blank_gate_ignores_template_metadata_and_checks_actual_model_content():
    gate = read("src/BIMBaoGui.Stage01/Revit/BlankFileGate.cs")
    assert "HasBlockingModelContent" in gate
    assert "get_Geometry" in gate
    assert "GeometryElement" in gate
    assert "else if (category != null && category.CategoryType == CategoryType.Model)" not in gate
    assert "element.GetType().Name" in gate


def test_stage01_uses_left_directory_and_internal_scroll_instead_of_paging():
    attributes = read("src/BIMBaoGui.Stage01/UI/Stage01ComponentAttributes.cs")
    assert "DrawDirectory" in attributes
    assert "DirectoryWidth" in attributes
    assert "MouseWheel" in attributes
    assert "DrawScrollBar" in attributes
    assert "DrawGroupNavigation" not in attributes
    assert "_previousPage" not in attributes
    assert "_nextPage" not in attributes
    assert "PageSize" not in attributes


def test_required_optional_inherited_and_system_fields_are_distinguished():
    attributes = read("src/BIMBaoGui.Stage01/UI/Stage01ComponentAttributes.cs")
    component = read("src/BIMBaoGui.Stage01/Stage01Component.cs")
    for label in ("必填", "选填", "继承", "系统"):
        assert label in attributes
    assert "_owner.IsFieldRequired" in attributes
    assert "PlanningTargetRequirement.Inherited" in attributes
    assert "IsFieldRequired" in component


def test_input_fields_show_type_examples_and_reject_invalid_values_before_closing():
    attributes = read("src/BIMBaoGui.Stage01/UI/Stage01ComponentAttributes.cs")
    editor = read("src/BIMBaoGui.Stage01/UI/InlineEditor.cs")
    rules = read("src/BIMBaoGui.Stage01/Core/FieldInputRules.cs")
    assert "FieldInputRules.BuildPlaceholder" in attributes
    assert "FieldInputRules.Validate" in attributes
    assert "示例" in rules
    assert "Func<string, string> validate" in editor
    assert "errorLabel" in editor


def test_planning_targets_use_structured_operator_value_and_unit_editor():
    target = read("src/BIMBaoGui.Stage01/Core/PlanningTargetValue.cs")
    catalog = read("src/BIMBaoGui.Stage01/Core/PlanningTargetCatalog.cs")
    editor = read("src/BIMBaoGui.Stage01/UI/PlanningTargetEditor.cs")
    validation = read("src/BIMBaoGui.Stage01/Core/Stage01Validation.cs")
    assert "PlanningTargetOperator" in target
    assert "PlanningTargetUnit" in target
    assert "≤" in editor and "≥" in editor and "区间" in editor
    assert "BuildingDensityCode" in catalog
    assert "FloorAreaRatioCode" in catalog
    assert "GreenRateCode" in catalog
    assert "ValidatePlanningTargets" in validation
    assert "总平模型必填" in validation


def test_blocking_reasons_are_actionable_and_can_navigate_to_first_problem():
    attributes = read("src/BIMBaoGui.Stage01/UI/Stage01ComponentAttributes.cs")
    feedback = read("src/BIMBaoGui.Stage01/Core/Stage01Feedback.cs")
    assert "当前阻断原因" in attributes
    assert "定位首个问题" in attributes
    assert "Stage01Feedback.Build" in attributes
    assert "Stage01Feedback.FirstProblemGroup" in attributes
    assert "提交与校验" in feedback


def test_file_context_and_task_plan_are_strong_grasshopper_types():
    context_goo = read("src/BIMBaoGui.Stage01/GrasshopperTypes/HBRFileContextGoo.cs")
    context_param = read("src/BIMBaoGui.Stage01/GrasshopperTypes/HBRFileContextParam.cs")
    plan_goo = read("src/BIMBaoGui.Stage01/GrasshopperTypes/HBRTaskPlanGoo.cs")
    plan_param = read("src/BIMBaoGui.Stage01/GrasshopperTypes/HBRTaskPlanParam.cs")
    assert "GH_Goo<HBRFileContext>" in context_goo
    assert "GH_PersistentParam<HBRFileContextGoo>" in context_param
    assert "GH_Goo<HBRTaskPlan>" in plan_goo
    assert "GH_PersistentParam<HBRTaskPlanGoo>" in plan_param
    assert "HBR.FileContext.Json" in context_goo
    assert "HBR.TaskPlan.Json" in plan_goo


def test_stage02_requires_file_context_and_outputs_task_plan():
    component = read("src/BIMBaoGui.Stage01/Stage02TaskPlanComponent.cs")
    attributes = read("src/BIMBaoGui.Stage01/UI/Stage02ComponentAttributes.cs")
    service = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitContextService.cs")
    compiler = read("src/BIMBaoGui.Stage01/TaskPlanning/TaskPlanCompiler.cs")
    assert "class Stage02TaskPlanComponent : GH_Component" in component
    assert "new HBRFileContextParam()" in component
    assert "new HBRTaskPlanParam()" in component
    assert "TaskPlanCompiler.Compile" in component
    assert "RevitDocumentFingerprint" in component
    assert "模型任务与骨架分流" in component
    assert "class Stage02ComponentAttributes : GH_ComponentAttributes" in attributes
    assert "HBRDocumentFingerprint.Compute" in service
    assert "Stage01Storage.Read" in service
    assert "StoredFileGuid" in service
    assert "StoredPayloadHash" in service
    assert "StoredWorkflowVersion" in service
    assert "HBRLiveContextPolicy.Validate" in component
    assert "document.IsReadOnly" not in service
    assert "请连接 01 文件初始化" in compiler


def test_task_plan_compiler_routes_site_above_and_underground_paths():
    catalog = read("src/BIMBaoGui.Stage01/TaskPlanning/TaskRuleCatalog.cs")
    compiler = read("src/BIMBaoGui.Stage01/TaskPlanning/TaskPlanCompiler.cs")
    plan = read("src/BIMBaoGui.Stage01/TaskPlanning/HBRTaskPlan.cs")
    snapshot = load_json(
        "tests/BIMBaoGui.Stage01.Core.Tests/Snapshots/task-rules.v1.json"
    )
    partitions = {
        partition["modelFileType"]: partition["rules"]
        for partition in snapshot["partitions"]
    }
    rules = {
        rule["taskId"]: rule
        for partition_rules in partitions.values()
        for rule in partition_rules
    }

    assert {model: len(items) for model, items in partitions.items()} == {
        "总平模型": 15,
        "单体建筑—地上": 7,
        "单体建筑—地下": 6,
    }
    assert rules["SITE.TOTAL_LAND"]["modelFileType"] == "总平模型"
    assert rules["ABOVE.BODY"]["modelFileType"] == "单体建筑—地上"
    assert rules["UNDERGROUND.BODY"]["modelFileType"] == "单体建筑—地下"
    assert rules["SITE.GREEN"]["conditionKey"] == "site.green"

    assert "HbrRuleDatabase.Current" in catalog
    assert "database.Package.Tasks" in catalog
    assert ".Select(MapRule)" in catalog
    assert 'case "REQUIRED":' in catalog
    assert 'case "CONDITIONAL":' in catalog
    assert "GetManifestResourceStream" not in catalog
    assert all(f'"{task_id}"' not in catalog for task_id in rules)
    assert "ResolveSkeletonPath" in compiler
    assert "RequiresRecompile" in plan
    assert "FileContextHash" in plan
