import re
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def read(relative_path: str) -> str:
    path = ROOT / relative_path
    assert path.is_file(), f"missing Task 6 production file: {relative_path}"
    return path.read_text(encoding="utf-8")


def method_body(text: str, signature: str) -> str:
    start = text.index(signature)
    brace = text.index("{", start)
    depth = 0
    for index in range(brace, len(text)):
        if text[index] == "{":
            depth += 1
        elif text[index] == "}":
            depth -= 1
            if depth == 0:
                return text[brace : index + 1]
    raise AssertionError(f"unclosed method: {signature}")


def test_new_stage02_has_real_ports_and_legacy_is_hidden():
    new = read("src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs")
    old = read("src/BIMBaoGui.Stage01/Stage02TaskPlanComponent.cs")
    for label in (
        "文件上下文",
        "元素Id",
        "角色提示",
        "交互点选",
        "项目信息",
        "生成预览",
        "确认写入",
        "预览",
        "匹配载体",
        "字段明细",
        "阻断信息",
        "写入状态",
        "安装数量",
        "写入数量",
        "规则哈希",
        "报告路径",
        "总状态",
    ):
        assert label in new
    assert '"湖北BIM报规｜02 构件与属性准备"' in new
    assert "GH_Exposure.primary" in new
    assert "GH_Exposure.hidden" in old


def test_selection_contract_has_four_distinct_modes_and_frozen_id_evidence():
    component = read(
        "src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs"
    )
    policy = read(
        "src/BIMBaoGui.Stage01/Stage02/Stage02PreparationInputPolicy.cs"
    )
    selection = read(
        "src/BIMBaoGui.Stage01/Revit/Stage02RevitSelectionService.cs"
    )
    models = read("src/BIMBaoGui.Stage01/Stage02/Stage02Models.cs")
    preview = read(
        "src/BIMBaoGui.Stage01/Revit/Stage02RevitPreviewService.cs"
    )
    combined = component + policy + selection + models + preview
    for mode in (
        "ProjectInformation",
        "ExplicitIds",
        "ExplicitPick",
        "CurrentSelection",
    ):
        assert mode in combined
    assert 'ExplicitIds = "EXPLICIT_IDS"' in models
    assert "case Stage02SelectionModes.ExplicitIds:" in preview
    assert "ResolveElementIds" in selection
    assert "document.GetElement(new ElementId" in selection
    assert "CreateReference" in selection
    assert "DocumentFingerprint" in selection
    assert "UniqueId" in selection
    assert "RoleHint" in selection


def test_explicit_ids_is_a_first_class_preview_identity():
    selection = read(
        "src/BIMBaoGui.Stage01/Revit/Stage02RevitSelectionService.cs"
    )
    models = read("src/BIMBaoGui.Stage01/Stage02/Stage02Models.cs")
    preview = read(
        "src/BIMBaoGui.Stage01/Revit/Stage02RevitPreviewService.cs"
    )
    assert 'ExplicitIds = "EXPLICIT_IDS"' in models
    assert "Stage02SelectionModes.ExplicitIds" in selection
    assert "case Stage02SelectionModes.ExplicitIds:" in preview
    supported = preview[preview.index("IsSupportedSelectionMode") :]
    assert "Stage02SelectionModes.ExplicitIds" in supported


def test_selection_failures_preserve_the_requested_mode():
    text = read(
        "src/BIMBaoGui.Stage01/Revit/Stage02RevitSelectionService.cs"
    )
    for mode in (
        "CurrentSelection",
        "ExplicitPick",
        "ExplicitIds",
        "ProjectInformation",
    ):
        assert f"Failed(error, Stage02SelectionModes.{mode})" in text
    empty_selection = text.index('"当前 Revit 选择集中没有元素。"')
    assert "Stage02SelectionModes.CurrentSelection" in text[
        empty_selection : empty_selection + 160
    ]


def test_preview_and_confirmation_use_two_explicit_edge_gates():
    text = read("src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs")
    assert text.count("new ExplicitExecutionGate()") == 2
    assert "_previewGate.Observe(generatePreview)" in text
    assert "_confirmGate.Observe(confirmWrite)" in text
    assert "Stage02RevitWriteRequest.FromPreview" in text
    assert "_previewSelectionEvidence" in text
    assert "_previewInputSignature" in text
    assert "ClearPreviewLocked" in text


def test_component_uses_pure_edge_count_and_write_publication_policies():
    component = read(
        "src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs"
    )
    policy = read(
        "src/BIMBaoGui.Stage01/Stage02/Stage02PreparationInputPolicy.cs"
    )
    assert "Stage02PreparationExecutionPolicy.Evaluate" in component
    assert "edgeDecision.ShouldGeneratePreview" in component
    assert "edgeDecision.ShouldConfirmWrite" in component
    assert "Stage02PreparationWritePublicationPolicy.Evaluate" in component
    assert "_previewCountCache.Publish(_preview)" in component
    assert "_previewCountCache.Clear()" in component
    assert "_previewCountCache.Current" in component
    assert "Stage02PreparationPreviewCounts.Calculate" not in component
    assert "Stage02PreparationPreviewCounts.Calculate" in policy
    assert "_previewBlockers" in component


def test_component_has_no_private_preview_count_reimplementation():
    text = read("src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs")
    for forbidden in (
        "private static int GetPendingInstallCount",
        "private static int GetInstalledCount",
        "private static int GetTotalParameterCount",
        "private static int GetPendingWriteCount",
        "GetParameterActionGroups",
    ):
        assert forbidden not in text

    solve = method_body(text, "protected override void SolveInstance")
    complete_preview = method_body(text, "private void CompletePreview")
    ui_snapshot = method_body(
        text,
        "internal Stage02PreparationUiSnapshot GetUiSnapshot",
    )
    runtime_snapshot = method_body(
        text,
        "private Stage02PreparationRuntimeSnapshot CaptureRuntimeSnapshot",
    )
    for body in (solve, complete_preview, ui_snapshot, runtime_snapshot):
        assert "Stage02PreparationPreviewCounts.Calculate" not in body
    assert "_previewCountCache.Publish(_preview)" in complete_preview
    assert "_previewCountCache.Current" in ui_snapshot
    assert "_previewCountCache.Current" in runtime_snapshot


def test_input_conflicts_block_both_preview_and_confirmation_edges():
    text = read("src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs")
    solve = method_body(text, "protected override void SolveInstance")
    assert re.search(
        r"previewAllowed\s*=\s*previewEdge\s*&&\s*"
        r"inputBlockers\.Length\s*==\s*0",
        solve,
    )
    assert re.search(
        r"confirmAllowed\s*=\s*confirmEdge\s*&&\s*"
        r"inputBlockers\.Length\s*==\s*0",
        solve,
    )


def test_modal_pick_is_only_reached_from_preview_edge_handler():
    component = read(
        "src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs"
    )
    selection = read(
        "src/BIMBaoGui.Stage01/Revit/Stage02RevitSelectionService.cs"
    )
    solve = method_body(component, "protected override void SolveInstance")
    begin_preview = method_body(component, "private void BeginPreview")
    assert "PickObjects" not in component
    assert "PickObjects(ObjectType.Element)" in selection
    assert "Stage02RevitSelectionService.PickElements" not in solve
    assert "Stage02RevitSelectionService.PickElements" in begin_preview
    assert "previewEdge" in solve


def test_new_preview_cannot_start_while_confirmation_is_pending():
    text = read("src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs")
    begin_preview = method_body(text, "private void BeginPreview")
    pending_guard = begin_preview.index("if (_writeAttemptState.IsPending)")
    clear = begin_preview.index("ClearPreviewLocked")
    assert pending_guard < clear


def test_cancel_and_stale_callbacks_are_structured_and_scheduled_to_gh():
    text = read("src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs")
    assert "selection.Cancelled" in text
    assert '"选择取消"' in text
    assert "InputSignature" in text
    assert "IsInputSignatureCurrent" in text
    assert '"结果过期"' in text
    assert text.count("ScheduleSolution") >= 2
    assert "ExpireSolution(false)" in text
    assert "lock (_stateLock)" in text
    for forbidden in ("Task.Run", "Thread.Sleep", "while ("):
        assert forbidden not in text


def test_scheduled_solve_does_not_erase_a_preview_failure_without_new_input():
    text = read("src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs")
    solve = method_body(text, "protected override void SolveInstance")
    assert "string.Equals(_status, PreviewBlocked" not in solve


def test_valid_first_context_transitions_to_waiting_preview():
    text = read("src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs")
    solve = method_body(text, "protected override void SolveInstance")
    assert "else if (string.Equals(_status, WaitingContext" in solve
    waiting = solve.index("else if (string.Equals(_status, WaitingContext")
    assert "_status = WaitingPreview;" in solve[waiting:]


def test_preview_invalidation_covers_input_drift_and_write_outcomes():
    text = read("src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs")
    solve = method_body(text, "protected override void SolveInstance")
    callback = method_body(text, "private void CompleteWrite")
    assert "InputSignature" in solve
    assert "ClearPreviewLocked" in solve
    assert "completed.Success" in callback
    assert "completed.RequiresNewPreview" in callback
    assert "ClearPreviewLocked" in callback
    assert "_previewNonce" in text
    assert "_previewSelectionEvidence" in text


def test_write_callbacks_and_enqueue_failures_are_bound_to_attempt_tokens():
    text = read("src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs")
    policy = read(
        "src/BIMBaoGui.Stage01/Stage02/Stage02PreparationInputPolicy.cs"
    )
    begin = method_body(text, "private void BeginWrite")
    callback = method_body(text, "private void CompleteWrite")
    clear = method_body(text, "private void ClearPreviewLocked")
    assert "Stage02PreparationWriteAttemptState" in policy
    assert "Stage02PreparationWriteCompletionDisposition" in policy
    assert "_writeAttemptState.BeginAttempt" in begin
    assert "attemptToken" in begin
    assert "_writeAttemptState.CompleteAttempt" in begin
    assert "Stage02PreparationWriteCompletionDisposition.Ignored" in begin
    assert "attemptToken" in callback
    assert "_writeAttemptState.CompleteAttempt" in callback
    assert "Stage02PreparationWriteCompletionDisposition.Publish" in callback
    publication = callback.index(
        "Stage02PreparationWritePublicationPolicy.Evaluate"
    )
    assert callback.index(
        "Stage02PreparationWriteCompletionDisposition.Ignored"
    ) < publication
    assert callback.index(
        "Stage02PreparationWriteCompletionDisposition.Discarded"
    ) < publication
    assert "_writeAttemptState.MarkActiveAttemptStale" in clear
    assert "_writePending" not in text


def test_stale_pending_status_survives_repeated_drift_and_enqueue_failure():
    text = read("src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs")
    solve = method_body(text, "protected override void SolveInstance")
    begin = method_body(text, "private void BeginWrite")

    input_changed_start = solve.index("if (inputChanged)")
    input_blockers_start = solve.index("if (inputBlockers.Length > 0)")
    input_changed = solve[input_changed_start:input_blockers_start]
    clear = input_changed.index("ClearPreviewLocked")
    stale_phase = input_changed.index(
        "Stage02PreparationWriteAttemptPhase.StalePending"
    )
    stale_status = input_changed.index("SetStalePendingStatusLocked")
    assert clear < stale_phase < stale_status

    ignored = begin.index(
        "Stage02PreparationWriteCompletionDisposition.Ignored"
    )
    discarded = begin.index(
        "Stage02PreparationWriteCompletionDisposition.Discarded"
    )
    stale_status = begin.index("SetStalePendingStatusLocked", discarded)
    discarded_return = begin.index("return;", discarded)
    assert ignored < discarded < stale_status < discarded_return


def test_write_attempt_phase_and_discarded_paths_replace_legacy_state():
    component = read(
        "src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs"
    )
    policy = read(
        "src/BIMBaoGui.Stage01/Stage02/Stage02PreparationInputPolicy.cs"
    )
    combined = component + policy
    for token in (
        "Stage02PreparationWriteAttemptPhase",
        "StalePending",
        "Stage02PreparationWriteCompletionDisposition.Discarded",
        "SetStalePendingStatusLocked",
    ):
        assert token in combined
    for forbidden in (
        "_writeResult",
        "IsInputSignatureCurrent()",
        "IsPublishable(",
        "AllowRetry",
        "Stage02PreparationWriteOutcomePolicy",
    ):
        assert forbidden not in combined


def test_last_failure_report_path_is_owned_by_attempt_state_and_not_cleared():
    text = read("src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs")
    snapshot = method_body(
        text,
        "private Stage02PreparationRuntimeSnapshot CaptureRuntimeSnapshot",
    )
    assert "_writeAttemptState.LastFailureReportPath" in snapshot
    assert "_reportPath" not in text


def test_write_status_output_preserves_backend_status_text():
    text = read("src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs")
    callback = method_body(text, "private void CompleteWrite")
    assert "completed.Status" in callback
    assert "WriteStatusText" in callback


def test_outputs_include_stable_element_and_property_data_tree():
    component = read(
        "src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs"
    )
    formatter = read(
        "src/BIMBaoGui.Stage01/Stage02/Stage02PreparationFieldDetailFormatter.cs"
    )
    assert "GH_Structure<GH_String>" in component
    assert "new GH_Path(elementIndex)" in component
    assert "tree.EnsurePath(path)" in component
    assert re.search(r"OrderBy\([^\n]*UniqueId", component)
    assert re.search(r"OrderBy\([^\n]*PropertyId", component)
    details = method_body(component, "private static GH_Structure<GH_String> BuildFieldDetails")
    assert "Stage02PreparationFieldDetailFormatter.Format" in details
    for token in (
        '"elementId"',
        '"uniqueId"',
        '"elementName"',
        '"category"',
        '"role"',
        '"scope"',
        '"propertyId"',
        '"parameterGuid"',
        '"parameterName"',
        '"oldValue"',
        '"suggestedValue"',
        '"source"',
        '"requirementLevel"',
        '"applicability"',
        '"bindingAction"',
        '"valueAction"',
        '"blockers"',
    ):
        assert token in formatter
    assert "dataAccess.SetDataTree" in component


def test_preview_goo_is_runtime_only_and_param_has_no_manual_persistence():
    goo = read(
        "src/BIMBaoGui.Stage01/GrasshopperTypes/HBRStage02PreviewGoo.cs"
    )
    param = read(
        "src/BIMBaoGui.Stage01/GrasshopperTypes/HBRStage02PreviewParam.cs"
    )
    for token in ("Duplicate", "ToString", "CastFrom", "CastTo"):
        assert token in goo
    for forbidden in (
        "GH_IWriter",
        "GH_IReader",
        "SetString",
        "GetString",
        "SetByteArray",
        "GetByteArray",
        "override bool Write",
        "override bool Read",
    ):
        assert forbidden not in goo
    assert "GH_GetterResult.cancel" in param
    assert "Prompt_Singular" in param
    assert "Prompt_Plural" in param


def test_preparation_ui_lays_out_and_renders_real_ports_around_card():
    text = read("src/BIMBaoGui.Stage01/UI/Stage02PreparationAttributes.cs")
    assert "componentBox.Height" in text
    assert "LayoutInputParams(Owner, componentBox)" in text
    assert "LayoutOutputParams(Owner, componentBox)" in text
    assert "RenderComponentParameters" in text
    assert "_contentBounds" in text
    assert "Owner.Params.Input" in text
    assert "Owner.Params.Output" in text
    assert "_cardBounds = _contentBounds" in text


def test_card_text_exposes_identity_counts_and_all_distinct_states():
    component = read(
        "src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs"
    )
    ui = read("src/BIMBaoGui.Stage01/UI/Stage02PreparationAttributes.cs")
    combined = component + ui
    for token in (
        "RevitVersion",
        "DocumentTitle",
        "RulePackageId",
        "RulePackageVersion",
        "SelectionMode",
        "SelectedCount",
        "MatchedCount",
        "PreviewHash",
        "PendingInstallCount",
        "InstalledCount",
        "PendingWriteCount",
        "WrittenCount",
        "FirstBlocker",
    ):
        assert token in combined
    for state in (
        "等待上下文",
        "等待预览",
        "选择取消",
        "预览阻断",
        "预览就绪",
        "确认中",
        "写入成功",
        "写入失败",
        "结果过期",
    ):
        assert state in combined


def test_card_maps_each_actual_selection_mode_to_simplified_chinese():
    text = read("src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs")
    for mode, label in (
        ("ProjectInformation", "项目信息"),
        ("ExplicitIds", "元素Id"),
        ("ExplicitPick", "交互点选"),
        ("CurrentSelection", "当前选择"),
    ):
        assert f"Stage02PreparationSelectionMode.{mode}" in text
        assert f'return "{label}"' in text


def test_task6_production_files_do_not_use_backup_or_hidden_business_storage():
    paths = (
        "src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs",
        "src/BIMBaoGui.Stage01/UI/Stage02PreparationAttributes.cs",
        "src/BIMBaoGui.Stage01/GrasshopperTypes/HBRStage02PreviewGoo.cs",
        "src/BIMBaoGui.Stage01/GrasshopperTypes/HBRStage02PreviewParam.cs",
        "src/BIMBaoGui.Stage01/Stage02/Stage02PreparationInputPolicy.cs",
        "src/BIMBaoGui.Stage01/Stage02/Stage02PreparationFieldDetailFormatter.cs",
        "src/BIMBaoGui.Stage01/Revit/Stage02RevitSelectionService.cs",
    )
    combined = "\n".join(read(path) for path in paths)
    for forbidden in (
        ".bak",
        ".backup",
        "Task.Run",
        "Thread.Sleep",
        "while (",
    ):
        assert forbidden not in combined
    assert not re.search(r"[A-Za-z]:\\Users\\[^\\]+", combined)
