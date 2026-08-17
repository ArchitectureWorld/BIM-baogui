import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "src" / "BIMBaoGui.RevitAddin"
STAGE02 = PROJECT / "Stage02"
WORKFLOW = ROOT / ".github" / "workflows" / "build-revit-mcp.yml"


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


def test_stage02_current_selection_is_decoupled_from_auto_role_whitelist():
    inventory = read_stage02("NativeStage02Inventory.cs")
    selection = read_stage02("NativeStage02SelectionInventoryPolicy.cs")
    assignments = read_stage02("NativeStage02RoleAssignmentPolicy.cs")
    assert "NativeStage02SelectionInventoryPolicy.Resolve" in inventory
    assert "IsAutomaticInventoryEligible" in inventory
    assert "allowedCategories.Contains(element.Category)" in inventory
    assert "allowedCategories" not in selection
    assert '"SELECTION_EMPTY"' in inventory
    assert '"SELECTION_ELEMENT_MISSING"' in inventory
    assert '"SELECTION_ELEMENT_NOT_ELIGIBLE"' in inventory
    assert '"AUTO_ROLE_UNSUPPORTED"' in inventory
    assert "ROLE_ASSIGNMENT_CONFLICT" in read_stage02(
        "NativeStage02SemanticAssignmentModels.cs"
    )
    assert "NativeStage02IdentificationMode.Automatic" in assignments
    assert "NativeStage02IdentificationMode.Manual" in assignments


def test_stage02_requires_explicit_project_condition_declaration():
    source = read_stage02("NativeStage02RevitService.cs")
    assert "NativeProjectConditionDeclarationPolicy.Evaluate" in source
    assert "Stage02 等待项目条件声明" in source
    assert "无上述项目条件（已确认）" in source


def test_stage02_manual_roles_are_persisted_in_one_fixed_revit_storage_contract():
    storage = read_stage02("NativeStage02SemanticAssignmentStorage.cs")
    reader = read_stage02("NativeStage02SemanticAssignmentRevitService.cs")
    write = read_stage02("NativeStage02RevitWriteService.cs")
    write_policy = read_stage02(
        "NativeStage02SemanticAssignmentWritePolicy.cs"
    )
    preview = read_stage02("NativeStage02RevitService.cs")
    assert '"6f0ab4a7-0e0f-46d9-a31e-1f7615a4f2e3"' in storage
    assert '"HBR_BIMBAOGUI_STAGE02_ASSIGNMENTS"' in storage
    for field in (
        "SchemaVersion",
        "RulePackageId",
        "RulePackageVersion",
        "CanonicalJson",
        "PayloadSha256",
        "UpdatedUtc",
    ):
        assert f'"{field}"' in storage
    assert "DataStorage.Create(document)" in storage
    assert "storage.SetEntity(entity)" in storage
    assert "NativeStage02SemanticAssignmentStoragePolicy.Evaluate" in reader
    assert "NativeStage02SemanticAssignmentStorage.Write" in write
    assert "NativeStage02SemanticAssignmentStorage.Read" in write
    assert "NativeStage02SemanticAssignmentWritePolicy.Apply" in write
    assert "NativeStage02SemanticAssignmentWritePolicy.Verify" in write
    assert '"SEMANTIC_ASSIGNMENT_READBACK_FAILED"' in write_policy
    assert "NativeStage02SemanticAssignmentWritePolicy.ReadbackFailed" in write
    assert "document.Regenerate()" in write
    assert "get_Parameter(" in write
    assert "AssignedElementCount" in write
    assert "RemovedAssignmentCount" in write
    assert "FailedAssignmentCount" in write
    assert "NativeStage02SemanticAssignmentRevitService.Read" in preview
    assert '"PersistedManual"' in preview


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
    source = (PROJECT / "RevitExternalEventDispatcher.cs").read_text(encoding="utf-8")
    assert "RequestStage02Preview" in source
    assert "RequestStage02Write" in source
    assert "NativeStage02RevitService.CreatePreview" in source
    assert "NativeStage02RevitWriteService.Execute" in source


def test_stage02_pick_and_issue_navigation_are_external_event_requests():
    source = (PROJECT / "RevitExternalEventDispatcher.cs").read_text(encoding="utf-8")
    assert "RequestStage02PickElements" in source
    assert "NativeStage02InteractionService.PickElements" in source
    assert "RequestIssueNavigation" in source
    assert "NativeRevitIssueNavigationService.Execute" in source


def test_issue_navigation_resolves_unique_id_before_checking_element_id():
    source = (
        PROJECT / "Issues" / "NativeRevitIssueNavigationService.cs"
    ).read_text(encoding="utf-8")
    unique_lookup = source.index("document.GetElement(reference.UniqueId)")
    integer_check = source.index("live.Id.IntegerValue != reference.ElementId")
    assert unique_lookup < integer_check
    assert '"ISSUE_ELEMENT_STALE"' in source
    assert "document.GetElement(reference.ElementId)" not in source
    assert "Selection.SetElementIds" in source
    assert "ShowElements" in source
    assert "IsolateElementsTemporary" in source
    assert "DisableTemporaryViewMode" in source
    assert source.count("new Transaction(") >= 2


def test_stage02_workspace_is_real_and_not_a_placeholder():
    view = read_stage02("NativeStage02View.cs")
    request_policy = read_stage02("NativeStage02WorkbenchRequestPolicy.cs")
    workspace = (PROJECT / "WorkspaceControl.cs").read_text(encoding="utf-8")
    for text in (
        "全模型",
        "当前 Revit 选择",
        "自动识别",
        "手动指定",
        "批量语义类型",
        "继承批量选择",
        "恢复自动识别",
        "生成预览",
        "确认写入",
    ):
        assert text in view
    assert "NativeStage02ManualRoleCatalog.Current" in view
    assert "NativeStage02WorkbenchRequestPolicy.Build" in view
    assert "RoleOverrides" in request_policy
    assert "NativeStage02RoleAssignmentPolicy.AutoOverrideRoleId" in view
    assert "_previewStale = true" in view
    assert "_resolvedRequest = null" in view
    assert "NativeStage02FieldStatus" in view
    assert "new ScrollViewer" in view
    assert "CUSTOM_ELEMENT_UNAVAILABLE" not in view
    assert "new NativeStage02View" in workspace
    assert "Stage02 等待开发" not in workspace


def test_stage02_detailed_status_is_fixed_height_and_scrollable():
    view = read_stage02("NativeStage02View.cs")
    assert "Height = new GridLength(96)" in view
    assert "Content = _statusText" in view
    assert "VerticalScrollBarVisibility = ScrollBarVisibility.Auto" in view
    assert "HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled" in view


def test_unified_ci_runs_stage02_revit_contract():
    workflow = WORKFLOW.read_text(encoding="utf-8")
    assert "Verify native and MCP contracts" in workflow
    assert "tests/test_revit_addin_stage02_revit_contract.py" in workflow


def test_stage02a_captures_world_geometry_without_bbox_area_fallback():
    capture = read_stage02("NativeStage02RevitGeometryEvidenceService.cs")
    service = read_stage02("NativeStage02RevitService.cs")
    for token in (
        "get_BoundingBox(null)",
        "BoundingBoxXYZ.Transform",
        "LocationPoint",
        "LocationCurve",
        "GeometryInstance",
        "GetInstanceGeometry()",
        "PlanarFace",
        "GetEdgesAsCurveLoops()",
        "Tessellate()",
        "HOST_AREA_COMPUTED",
        "ShortCurveTolerance",
        "GEOMETRY_CAPTURE_AMBIGUOUS",
        "GEOMETRY_CAPTURE_UNSUPPORTED",
        "GEOMETRY_AREA_SOURCE_MISMATCH",
    ):
        assert token in capture
    assert "NativeStage02RevitGeometryEvidenceService.Capture" in service
    assert "ApprovedProjectedAreaSquareMetres" in service
    assert "MaxXFeet -" not in capture
    assert "MaxYFeet -" not in capture


def test_stage02a_confirmation_manual_review_and_workflow_results_are_persisted():
    service = read_stage02("NativeStage02RevitService.cs")
    write = read_stage02("NativeStage02RevitWriteService.cs")
    manual = read_stage02("NativeStage02ManualReviewStorage.cs")
    assignment = read_stage02("NativeStage02SemanticAssignmentCanonicalizer.cs")
    assert '"1.1.0"' in assignment
    for field in ("RulePackageSha256", "ElementSnapshotHash", "ConfirmedUtc"):
        assert field in assignment
    assert "Confirmations" in service
    assert "NativeStage02RoleConfirmationPolicy.Resolve" in service
    assert "NativeStage02GeometryEvidencePolicy.Evaluate" in service
    assert '"HBR_NATIVE_GEOMETRY_REVIEW_V1"' in manual
    assert "DataStorage.Create(document)" in manual
    assert "NativeStage02ManualReviewPolicy.Seal" in manual
    assert "NativeWorkflowResultCanonicalizer.Build" in write
    assert '"STAGE02A"' in write
    assert '"ELEMENT_PREPARATION"' in write
    assert "NativeWorkflowResultStorage.Write" in write
    assert "ScopeComplete" in write
    for forbidden in (
        "总建筑面积",
        "建筑密度",
        "容积率",
        "绿地率",
        "停车位汇总",
    ):
        assert forbidden not in read_stage02("NativeStage02GeometryEvidence.cs")


def test_stage02a_keeps_one_transaction_per_element_and_independent_outcomes():
    source = read_stage02("NativeStage02RevitWriteService.cs")
    assert "NativeStage02ElementWriteOutcome" in source
    assert "ElementOutcomes" in source
    assert "GeometryOutcomes" in source
    assert "FieldOutcomes" in source
    assert re.search(
        r"foreach\s*\(NativeStage02ElementPlan.*?new\s+Transaction\s*\(",
        source,
        re.S,
    )
    assert "outcomes.Add" in source
    assert "continue;" in source
