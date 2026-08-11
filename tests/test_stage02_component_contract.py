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
    operation_results = read(
        "src/BIMBaoGui.Stage01/Revit/Stage02RevitOperationResults.cs"
    )
    models = read("src/BIMBaoGui.Stage01/Stage02/Stage02Models.cs")
    preview = read(
        "src/BIMBaoGui.Stage01/Revit/Stage02RevitPreviewService.cs"
    )
    combined = component + policy + selection + operation_results + models + preview
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
    assert "DocumentFingerprint" in operation_results
    assert "UniqueId" in operation_results
    assert "RoleHint" in operation_results


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
        assert (
            "Stage02RevitHostFailurePolicy.ForSelection(\n"
            f"        Stage02SelectionModes.{mode},"
        ) in text
    empty_selection = text.index('"当前 Revit 选择集中没有元素。"')
    assert "Stage02SelectionModes.CurrentSelection" in text[
        empty_selection : empty_selection + 160
    ]


def test_host_selection_technical_failures_use_typed_exception_wiring():
    host = read("src/BIMBaoGui.Stage01/Revit/RevitHost.cs")
    selection = read(
        "src/BIMBaoGui.Stage01/Revit/Stage02RevitSelectionService.cs"
    )
    results = read(
        "src/BIMBaoGui.Stage01/Revit/Stage02RevitOperationResults.cs"
    )
    component = read(
        "src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs"
    )
    begin = method_body(component, "private void BeginPreview")

    assert "out Exception exception" in host
    assert "RevitHostReadOperation.Capture(read)" in host
    assert "Stage02RevitSelectionDisposition" in results
    for disposition in (
        "Success",
        "BusinessBlocked",
        "Cancelled",
        "TechnicalFailure",
    ):
        assert disposition in results
    assert "internal Exception Exception" in results
    for signature in (
        "internal static Stage02RevitSelectionResult ReadCurrentSelection(\n"
        "      HBRFileContext context,\n"
        "      string roleHint)",
        "internal static Stage02RevitSelectionResult PickElements(\n"
        "      HBRFileContext context,\n"
        "      string roleHint)",
        "internal static Stage02RevitSelectionResult ResolveElementIds(",
        "internal static Stage02RevitSelectionResult SelectProjectInformation(",
    ):
        entry = method_body(selection, signature)
        assert "out Exception exception" in entry
        assert "Stage02RevitHostFailurePolicy.ForSelection(" in entry
    host_policy = results[results.index("class Stage02RevitHostFailurePolicy") :]
    assert "exception == null" in host_policy
    assert "Stage02RevitSelectionResult.BusinessBlocked(" in host_policy
    assert "Stage02RevitSelectionResult.TechnicalFailure(" in host_policy
    assert "Stage02RevitFailureReportPolicy.ForSelection(selection)" in begin
    policy_position = begin.index(
        "Stage02RevitFailureReportPolicy.ForSelection(selection)"
    )
    assert policy_position < begin.index("if (selection.Cancelled)")
    assert "selection.Exception.Message" not in begin
    assert ".Contains(\"" not in begin


def test_preview_outcomes_use_executable_typed_classification_and_policy():
    preview = read(
        "src/BIMBaoGui.Stage01/Revit/Stage02RevitPreviewService.cs"
    )
    results = read(
        "src/BIMBaoGui.Stage01/Revit/Stage02RevitOperationResults.cs"
    )
    component = read(
        "src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs"
    )
    begin = method_body(component, "private void BeginPreview")
    create = method_body(
        preview,
        "internal Stage02RevitPreviewResult CreatePreview",
    )
    core = method_body(
        preview,
        "private Stage02RevitPreviewResult CreatePreviewCore",
    )

    assert "Stage02RevitPreviewDisposition" in results
    for disposition in (
        "Success",
        "BusinessBlocked",
        "TechnicalFailure",
        "NoResult",
    ):
        assert disposition in results
    assert "exception is Stage02ContractException" in results
    assert "internal static Stage02RevitPreviewResult FromException" in results
    assert "out Exception exception" in create
    assert "Stage02RevitHostFailurePolicy.ForPreview(" in create
    host_policy = results[results.index("class Stage02RevitHostFailurePolicy") :]
    assert "Stage02RevitPreviewResult.TechnicalFailure(" in host_policy
    assert core.count("Stage02RevitPreviewResult.FromException(exception)") == 2
    assert "Stage02RevitPreviewResult.FromException(hostException)" in core
    assert "Stage02RevitFailureReportPolicy.ForPreview(result)" in begin
    assert begin.index(
        "Stage02RevitFailureReportPolicy.ForPreview(result)"
    ) < begin.index("CompletePreview(", begin.index("_previewService.CreatePreview"))
    assert "result == null || result.Preview == null" not in begin


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


def test_write_failure_report_is_finalized_after_attempt_identity_decision():
    component = read(
        "src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs"
    )
    write_service = read(
        "src/BIMBaoGui.Stage01/Revit/Stage02RevitWriteService.cs"
    )
    begin = method_body(component, "private void BeginWrite")
    callback = method_body(component, "private void CompleteWrite")
    build_failure = method_body(
        write_service,
        "private Stage02RevitWriteResult BuildFailureResult",
    )
    request_model = write_service[
        write_service.index("internal sealed class Stage02RevitWriteRequest") :
        write_service.index("internal sealed class Stage02RevitWriteResult")
    ]

    assert begin.index("_writeAttemptState.BeginAttempt") < begin.index(
        "Stage02RevitWriteRequest.FromPreview"
    )
    assert "InputSignature" in request_model
    assert "AttemptToken" in request_model
    assert "PreviewHash" in request_model
    assert "inputSignature" in begin[begin.index("FromPreview") :]
    assert "attemptToken" in begin[begin.index("FromPreview") :]

    assert "Stage02FailureReportDraft.Capture" in build_failure
    assert "InputSignature = _request.InputSignature" in build_failure
    assert "AttemptToken = _request.AttemptToken" in build_failure
    assert "PreviewHash = _request.PreviewHash" in build_failure
    assert "Stage02FailureReportWriter.TryWrite" not in build_failure

    complete_attempt = callback.index("_writeAttemptState.CompleteAttempt")
    ignored = callback.index(
        "Stage02PreparationWriteCompletionDisposition.Ignored"
    )
    finalize = callback.index("Stage02FailureReportFinalizer.TryPublish")
    publish_path = callback.index("_failureReportState.TryPublish")
    assert complete_attempt < ignored < finalize < publish_path
    assert "Stage02FailureReportPublicationDisposition.DiscardedStale" in callback
    assert "Stage02FailureReportPublicationDisposition.PublishedCurrent" in callback


def test_write_enqueue_failure_creates_and_publishes_typed_report():
    component = read(
        "src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs"
    )
    operation_results = read(
        "src/BIMBaoGui.Stage01/Revit/Stage02RevitOperationResults.cs"
    )
    begin = method_body(component, "private void BeginWrite")
    draft_builder = method_body(
        component,
        "private static Stage02FailureReportDraft BuildEnqueueFailureReportDraft",
    )
    catch = begin[begin.index("catch (Exception exception)") :]
    failure = begin[begin.index("if (enqueued) return;") :]

    assert "enqueueException = exception" in catch
    assert "Stage02RevitWriteEnqueueFailurePolicy.ForFailure" in failure
    assert "BuildEnqueueFailureReportDraft" in failure
    assert "Stage02FailureReportFinalizer.TryPublish" in failure
    assert "_failureReportState.TryPublish" in failure
    assert "reportPublication.ShouldPublishCurrent" in failure
    assert failure.index(
        "Stage02PreparationWriteCompletionDisposition.Ignored"
    ) < failure.index("Stage02FailureReportFinalizer.TryPublish")
    assert "STAGE02_WRITE_ENQUEUE_EXCEPTION" in operation_results
    assert "STAGE02_WRITE_ENQUEUE_REJECTED" in operation_results
    assert 'OperationStage = "WRITE_ENQUEUE"' in draft_builder
    assert "InputSignature = request.InputSignature" in draft_builder
    assert "AttemptToken = request.AttemptToken" in draft_builder
    assert "PreviewHash = request.PreviewHash" in draft_builder


def test_write_post_enqueue_callback_failure_completes_once_with_full_identity():
    write_service = read(
        "src/BIMBaoGui.Stage01/Revit/Stage02RevitWriteService.cs"
    )
    operation_results = read(
        "src/BIMBaoGui.Stage01/Revit/Stage02RevitOperationResults.cs"
    )
    enqueue = method_body(write_service, "internal bool EnqueueWrite(")
    failure = method_body(
        write_service,
        "private static Stage02RevitWriteResult BuildHostCallbackFailure",
    )

    assert "Stage02PreparationCompletionGate<Stage02RevitWriteResult>" in enqueue
    assert "RevitHost.EnqueueAction(" in enqueue
    assert enqueue.count("completionGate.TryComplete") == 2
    assert "Stage02RevitWriteHostCallbackFailurePolicy.ForFailure" in failure
    assert '"WRITE_HOST_CALLBACK"' in operation_results
    for mapping in (
        "InputSignature = request.InputSignature",
        "AttemptToken = request.AttemptToken",
        "FileGuid = request.Preview.FileGuid",
        "DocumentFingerprint = request.DocumentFingerprint",
        "DocumentTitle = request.Preview.DocumentTitle",
        "RulePackageId = request.Preview.RulePackageId",
        "RulePackageVersion = request.Preview.RulePackageVersion",
        "RulePackageSha256 = request.Preview.RulePackageSha256",
        "PreviewHash = request.PreviewHash",
        "UniqueIds = request.Targets",
        "PropertyIds = request.Preview.Elements",
        "OperationStage = decision.OperationStage",
        "RootCauseStage = decision.OperationStage",
        "Exception = decision.Exception",
        "FailureReportDraft = reportDraft",
    ):
        assert mapping in failure


def test_write_completion_consumer_failure_has_independent_terminal_route():
    component = read(
        "src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs"
    )
    write_service = read(
        "src/BIMBaoGui.Stage01/Revit/Stage02RevitWriteService.cs"
    )
    enqueue = method_body(write_service, "internal bool EnqueueWrite(")
    complete = method_body(
        write_service,
        "internal void Complete(Stage02RevitWriteResult result)",
    )
    begin = method_body(component, "private void BeginWrite")

    assert "consumerFailureTerminal" in enqueue
    assert "consumerFailureRecorder" in enqueue
    assert "consumerFailureRefresh" in enqueue
    assert "Stage02PreparationCompletionGate<Stage02RevitWriteResult>" in enqueue
    assert "_completed(result);" in complete
    assert "_completionIssued" not in complete
    assert "catch" not in complete
    for callback in (
        "TerminateWriteCompletionConsumerFailure",
        "RecordWriteCompletionConsumerFailure",
        "RefreshAfterWriteCompletionConsumerFailure",
    ):
        assert callback in begin

    terminal = method_body(
        component,
        "private void TerminateWriteCompletionConsumerFailure",
    )
    recorder = method_body(
        component,
        "private void RecordWriteCompletionConsumerFailure",
    )
    report_builder = method_body(
        component,
        "private static Stage02FailureReportDraft\n"
        "      BuildCompletionConsumerFailureReportDraft",
    )
    assert "_writeAttemptState.CompleteAttempt" in terminal
    assert "_writeStatus = WriteFailed" in terminal
    assert "_status = WriteFailed" in terminal
    assert "ClearPreviewLocked" in terminal
    assert "Stage02FailureReportFinalizer.TryPublish" in recorder
    assert "STAGE02_WRITE_COMPLETION_CONSUMER_FAILED" in report_builder
    assert 'OperationStage = "WRITE_COMPLETION_CONSUMER"' in report_builder


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


def test_failure_report_path_is_owned_by_current_input_identity_state():
    text = read("src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs")
    snapshot = method_body(
        text,
        "private Stage02PreparationRuntimeSnapshot CaptureRuntimeSnapshot",
    )
    assert "_failureReportState.ReportPath" in snapshot
    assert "_writeAttemptState.LastFailureReportPath" not in snapshot
    assert "_reportPath" not in text


def test_preview_technical_failures_publish_only_current_identity_report():
    component = read(
        "src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs"
    )
    solve = method_body(component, "protected override void SolveInstance")
    begin = method_body(component, "private void BeginPreview")
    failure = method_body(
        component,
        "private void CompleteTechnicalPreviewFailure",
    )
    operation_results = read(
        "src/BIMBaoGui.Stage01/Revit/Stage02RevitOperationResults.cs"
    )
    snapshot = method_body(
        component,
        "private Stage02PreparationRuntimeSnapshot CaptureRuntimeSnapshot",
    )

    assert "using BIMBaoGui.Stage01.Diagnostics;" in component
    assert "_failureReportState.ObserveCurrent" in solve
    assert "_failureReportState.BeginPreview" in begin
    assert "Stage02FailureReportWriter.TryWrite" in failure
    assert "Stage02FailureReportContext" in failure
    assert "DIAG_STAGE02_PREVIEW_FAILED" in failure
    assert "PREVIEW_SELECTION" in begin
    assert "PREVIEW_BUILD" in begin
    assert "STAGE02_SELECTION_SERVICE_EXCEPTION" in operation_results
    assert "STAGE02_PREVIEW_SERVICE_EXCEPTION" in operation_results
    assert "STAGE02_PREVIEW_NO_RESULT" in operation_results
    assert "IsInputSignatureCurrentLocked" in failure
    assert "_failureReportState.TryPublish" in failure
    assert "_failureReportState.ReportPath" in snapshot
    assert "_writeAttemptState.LastFailureReportPath" not in snapshot

    cancelled = begin[begin.index("if (selection.Cancelled)") :]
    cancelled = cancelled[: cancelled.index("if (!selection.Success)")]
    assert "CompleteTechnicalPreviewFailure" not in cancelled

    create_preview = begin.index("_previewService.CreatePreview")
    preview_try = begin.rfind("try", 0, create_preview)
    preview_catch = begin.index("catch (Exception exception)", create_preview)
    assert preview_try >= 0
    assert preview_try < create_preview < preview_catch


def test_technical_preview_failure_has_distinct_status_from_business_blockers():
    component = read(
        "src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs"
    )
    ui = read("src/BIMBaoGui.Stage01/UI/Stage02PreparationAttributes.cs")
    technical_failure = method_body(
        component,
        "private void CompleteTechnicalPreviewFailure",
    )
    business_failure = method_body(
        component,
        "private void CompletePreviewFailure(\n"
        "      string inputSignature,\n"
        "      string hostFingerprint,\n"
        "      IEnumerable<string> messages)",
    )
    complete_preview = method_body(component, "private void CompletePreview")

    assert 'PreviewTechnicalFailed = "预览技术失败"' in component
    assert "_writeStatus = PreviewTechnicalFailed" in technical_failure
    assert "_status = PreviewTechnicalFailed" in technical_failure
    assert "_writeStatus = PreviewBlocked" not in technical_failure
    assert "_status = PreviewBlocked" not in technical_failure
    assert "_writeStatus = PreviewBlocked" in business_failure
    assert "PreviewBlocked" in complete_preview
    assert 'string.Equals(status, "预览技术失败"' in ui


def test_null_selection_is_reported_before_any_result_dereference():
    component = read(
        "src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs"
    )
    operation_results = read(
        "src/BIMBaoGui.Stage01/Revit/Stage02RevitOperationResults.cs"
    )
    begin = method_body(component, "private void BeginPreview")
    technical_failure = method_body(
        component,
        "private void CompleteTechnicalPreviewFailure",
    )
    policy = begin.index("Stage02RevitFailureReportPolicy.ForSelection")
    cancelled = begin.index("selection.Cancelled")
    success = begin.index("selection.Success")
    report_branch = begin[policy:cancelled]

    assert policy < cancelled < success
    assert "selection?.Messages" in report_branch
    assert "STAGE02_SELECTION_NO_RESULT" in operation_results
    assert "Stage02 元素选择服务未返回结果。" in operation_results
    assert "_previewPending = false" in technical_failure


def test_stage02_report_output_describes_current_preview_or_write_failure():
    component = read(
        "src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs"
    )
    assert "当前输入签名的预览或写入失败报告路径；无报告时为空。" in component
    assert "最近一次写入失败报告路径；无报告时为空。" not in component


def test_write_status_output_preserves_backend_status_text():
    text = read("src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs")
    callback = method_body(text, "private void CompleteWrite")
    assert "completed.Status" in callback
    assert "WriteStatusText" in callback


def test_field_detail_outputs_include_stable_element_and_property_data_tree():
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
        '"documentFingerprint"',
        '"documentTitle"',
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
        '"runtimeStatus"',
        '"runtimeBlockCode"',
        '"runtimeBlockReason"',
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
        "RuntimeNotImplementedCount",
        "RuntimeUnclassifiedRequirementCount",
        "FirstRuntimeBlockReason",
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


def test_runtime_support_card_consumes_only_projected_operation_snapshot():
    component = read(
        "src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs"
    )
    ui = read("src/BIMBaoGui.Stage01/UI/Stage02PreparationAttributes.cs")
    snapshot = method_body(
        component,
        "internal Stage02PreparationUiSnapshot GetUiSnapshot",
    )

    for token in (
        "RuntimeStatus",
        "RuntimeBlockCode",
        "RuntimeBlockReason",
        "RuntimeNotImplementedCount",
        "RuntimeUnclassifiedRequirementCount",
        "FirstRuntimeBlockReason",
    ):
        assert token in snapshot
    for label in ("运行支持", "未实现", "需求待定", "首条运行原因"):
        assert label in ui
    for forbidden in (
        "HbrRuleDatabase",
        "OwnerStrategy",
        "RequirementLevel",
        "GetRuntimeStatusDecision",
        "GetEffectiveRuntimeStatus",
    ):
        assert forbidden not in snapshot
        assert forbidden not in ui
    assert "runtimeOperations.Count" in snapshot
    assert re.search(r"CardHeight\s*=\s*470f", ui)
    assert "308f" in ui
    assert "_contentBounds.Bottom - 42f" in ui


def test_stage02_card_displays_deterministic_matched_roles():
    component = read(
        "src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs"
    )
    ui = read("src/BIMBaoGui.Stage01/UI/Stage02PreparationAttributes.cs")
    snapshot = method_body(
        component,
        "internal Stage02PreparationUiSnapshot GetUiSnapshot",
    )

    assert "MatchedRoles" in component
    assert ".Select(element => element.RoleId)" in snapshot
    assert ".Distinct(StringComparer.Ordinal)" in snapshot
    assert ".OrderBy(value => value, StringComparer.Ordinal)" in snapshot
    assert '"｜角色 "' in ui
    assert "snapshot.MatchedRoles" in ui


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
