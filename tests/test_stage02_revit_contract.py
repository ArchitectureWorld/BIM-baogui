from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def read(relative_path: str) -> str:
    path = ROOT / relative_path
    assert path.is_file(), f"missing Task 5 production file: {relative_path}"
    return path.read_text(encoding="utf-8")


def test_stage02_request_uses_document_fingerprint_and_unique_id():
    text = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitWriteService.cs")
    assert "DocumentFingerprint" in text
    assert "UniqueId" in text
    assert "GetElement(request.UniqueId)" in text
    assert "ElementId" in text


def test_selection_supports_current_selection_explicit_pick_and_cancel():
    text = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitSelectionService.cs")
    results = read(
        "src/BIMBaoGui.Stage01/Revit/Stage02RevitOperationResults.cs"
    )
    assert "Selection.GetElementIds()" in text
    assert "PickObjects(ObjectType.Element)" in text
    assert "OperationCanceledException" in text
    assert "Cancelled" in results
    assert "Task.Run" not in text


def test_host_unavailable_is_typed_without_synthetic_technical_exception():
    host = read("src/BIMBaoGui.Stage01/Revit/RevitHost.cs")
    selection = read(
        "src/BIMBaoGui.Stage01/Revit/Stage02RevitSelectionService.cs"
    )
    preview = read(
        "src/BIMBaoGui.Stage01/Revit/Stage02RevitPreviewService.cs"
    )
    results = read(
        "src/BIMBaoGui.Stage01/Revit/Stage02RevitOperationResults.cs"
    )
    run_read = host[
        host.index("public static bool RunReadInHostContext<T>(") :
        host.index("public static bool EnqueueAction(")
    ]
    policy = results[results.index("class Stage02RevitHostFailurePolicy") :]

    assert "?? new InvalidOperationException(error)" not in run_read
    assert "throw exception ?? new InvalidOperationException(error);" not in selection
    assert "private static bool TryRequireHost(" in selection
    assert selection.count("TryRequireHost(") == 5
    for core_method, mode in (
        ("ReadCurrentSelectionCore", "CurrentSelection"),
        ("PickElementsCore", "ExplicitPick"),
        ("ResolveElementIdsCore", "ExplicitIds"),
        ("SelectProjectInformationCore", "ProjectInformation"),
    ):
        core_start = selection.index(
            f"private static Stage02RevitSelectionResult {core_method}("
        )
        require_start = selection.index("TryRequireHost(", core_start)
        require_call = selection[require_start : require_start + 320]
        assert f"Stage02SelectionModes.{mode}" in require_call
    require_host = selection[selection.index("private static bool TryRequireHost(") :]
    assert "Stage02RevitHostFailurePolicy.ForSelection(" in require_host
    assert "return false;" in require_host
    assert selection.count("Stage02RevitHostFailurePolicy.ForSelection") >= 4
    assert "Stage02RevitHostFailurePolicy.ForPreview" in preview
    assert "exception == null" in policy
    assert ".Contains(" not in policy
    assert ".IndexOf(" not in policy


def test_project_information_has_a_non_pickable_role_hint_entrypoint():
    text = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitSelectionService.cs")
    assert "ProjectInformation" in text
    for role_id in ("PROJECT", "SITE", "BUILDING"):
        assert role_id in text


def test_preview_enumerates_complete_stage02_role_contract_from_database():
    text = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitPreviewService.cs")
    assert "HbrRuleDatabase" in text
    assert ".Package.Properties" in text
    assert "CarrierRoleIds" in text
    assert '"STAGE02"' in text
    assert "Stage02PreviewCompiler" in text
    assert "HBRDocumentFingerprint" in text
    assert "HBRLiveContextPolicy" in text
    assert "new Transaction" not in text
    assert "Task.Run" not in text


def test_document_identity_rejects_corrupt_stage01_storage_state():
    text = read("src/BIMBaoGui.Stage01/Revit/RevitDocumentIdentityService.cs")
    integrity = read(
        "src/BIMBaoGui.Stage01/Core/Stage01StoredPayloadIntegrityPolicy.cs"
    )
    assert "Stage01StorageStatePolicy.Evaluate" in text
    assert "StorageDecision.IsInitialized" in text
    assert "Stage01StoredPayloadIntegrityPolicy.Evaluate" in text
    assert "PayloadIntegrityDecision" in text
    assert "CORRUPT_STAGE01_STORAGE" in text + integrity
    assert "PAYLOAD_HASH_MISMATCH" in text + integrity


def test_read_only_document_is_blocked_before_preview_consumption():
    identity = read("src/BIMBaoGui.Stage01/Revit/RevitDocumentIdentityService.cs")
    preview = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitPreviewService.cs")
    models = read("src/BIMBaoGui.Stage01/Stage02/Stage02Models.cs")
    assert "identity.IsReadOnly" in identity
    assert "DocumentReadOnly" in models
    assert "Stage02Codes.DocumentReadOnly" in preview
    assert preview.index("Stage02Codes.DocumentReadOnly") < preview.index(
        "Stage02PreviewCompiler"
    )


def test_revit_category_identity_is_builtin_enum_not_localized_name():
    text = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitSelectionService.cs")
    assert "BuiltInCategory" in text
    assert "Category.Name" not in text


def test_shared_parameter_installer_is_visible_editable_and_restores_global_path():
    text = read(
        "src/BIMBaoGui.Stage01/Revit/Parameters/HbrSharedParameterInstaller.cs"
    )
    cleanup = read(
        "src/BIMBaoGui.Stage01/Revit/Parameters/HbrTemporaryFileCleanup.cs"
    )
    for token in (
        "SharedParametersFilename",
        "Visible = true",
        "UserModifiable = true",
        "HideWhenNoValue = false",
        "SharedParameterElement.Lookup",
        "BindingMap",
        "ReInsert",
        "Insert",
    ):
        assert token in text
    assert text.count("finally") >= 1
    assert "HbrTemporaryFileCleanup.Complete" in text
    assert "Path.GetTempPath()" in text
    assert "HbrSharedParameterDefinitionText.WriteRevitFile" in text
    assert "File.Delete" in cleanup
    assert cleanup.count("try") >= 2
    assert cleanup.count("catch") >= 2
    assert cleanup.count("cleanupFailures.Add") >= 2
    assert "new AggregateException" in cleanup
    assert ".Remove(" not in text
    assert "LookupParameter" not in text


def test_existing_binding_preserves_group_and_skips_satisfied_reinsert():
    text = read(
        "src/BIMBaoGui.Stage01/Revit/Parameters/HbrSharedParameterInstaller.cs"
    )
    assert "existingCategoryIds" in text
    assert "requiresCategoryMerge" in text
    assert "internalDefinition.ParameterGroup" in text


def test_value_sources_are_unambiguous_and_guid_readback_is_strict():
    preview = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitPreviewService.cs")
    verifier = read(
        "src/BIMBaoGui.Stage01/Revit/Parameters/HbrParameterReadbackVerifier.cs"
    )
    assert "get_Parameter(property.Revit.ParameterGuid)" in preview
    assert "GetParameters(name)" in preview
    assert "Stage01" in preview
    assert "Stage01.FieldRefs" in preview
    assert ".FieldKey" in preview
    assert "LookupParameter" not in preview
    assert "get_Parameter(operation.ParameterGuid)" in verifier
    assert "AsValueString" not in preview + verifier


def test_invalid_typed_suggestion_becomes_property_blocker_not_preview_exception():
    preview = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitPreviewService.cs")
    converter = read(
        "src/BIMBaoGui.Stage01/Revit/Parameters/HbrParameterValueConverter.cs"
    )
    assert "TryToInternalRawString" in converter
    assert "HbrParameterConversionDecision" in converter
    assert "INVALID_VALUE" in preview
    assert "conversion.Success" in preview
    assert "property.PropertyId" in preview
    assert "suggestion.ValueSource" in preview


def test_revit_2020_parameter_api_contract_uses_no_forge_type_ids():
    paths = (
        "src/BIMBaoGui.Stage01/Revit/Parameters/HbrSharedParameterInstaller.cs",
        "src/BIMBaoGui.Stage01/Revit/Parameters/HbrParameterValueConverter.cs",
        "src/BIMBaoGui.Stage01/Revit/Parameters/HbrParameterReadbackVerifier.cs",
    )
    text = "\n".join(read(path) for path in paths)
    for required in ("ParameterType", "DisplayUnitType", "BuiltInParameterGroup"):
        assert required in text
    for forbidden in ("ForgeTypeId", "SpecTypeId", "GroupTypeId"):
        assert forbidden not in text


def test_write_rebuilds_live_plan_then_consumes_before_atomic_transaction():
    text = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitWriteService.cs")
    rebuild = text.index("BuildLiveConfirmationSnapshot")
    consume = text.index("ValidateAndConsumeForExecution")
    group_start = text.index("execution.StartGroup")
    tx_start = text.index("execution.StartTransaction")
    ensure = text.index("EnsureBindings")
    write = text.index("WriteNonBlankSuggestions")
    regenerate = text.index("document.Regenerate()")
    verify = text.index("Verify")
    metadata = text.index("WriteAuditOnly")
    tx_commit = text.index("execution.Commit")
    assimilate = text.index("_group.Assimilate()")
    assert rebuild < consume < group_start < tx_start
    assert tx_start < ensure < write < regenerate < verify < metadata
    assert metadata < tx_commit < assimilate
    assert text.index("_transaction.Start()") < text.index(
        "SetFailuresPreprocessor"
    )
    assert "TransactionStatus.Committed" in text
    assert "_transaction.RollBack()" in text
    assert "_transaction.GetStatus()" in text
    assert "_group.RollBack()" in text
    assert text.index("_transaction.RollBack()") < text.index("_group.RollBack()")
    assert "Task.Run" not in text


def test_confirmation_rejection_uses_blocker_to_ui_repreview_policy():
    text = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitWriteService.cs")
    assert "Stage02ConfirmationUiPolicy.Decide" in text
    assert "uiDecision.RequiresNewPreview" in text
    assert "uiDecision.Status" in text


def test_preview_result_aggregates_operation_blockers():
    text = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitPreviewService.cs")
    assert "CollectOperationBlockers" in text
    assert "new Stage02RevitPreviewResult(preview, operationBlockers)" in text


def test_revit_preview_uses_shared_requirement_condition_decision():
    preview = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitPreviewService.cs")
    compiler = read("src/BIMBaoGui.Stage01/Stage02/Stage02PreviewCompiler.cs")
    models = read("src/BIMBaoGui.Stage01/Stage02/Stage02Models.cs")
    assert "Stage02RequirementDecisionPolicy.Resolve" in preview
    assert "Stage02RequirementDecisionPolicy.Resolve" in compiler
    assert "ValueActionOverride" in preview
    assert "CONDITION_STATE_MISSING" in preview + compiler + models


def test_stage01_organization_projection_is_explicit_and_hash_bound():
    preview = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitPreviewService.cs")
    models = read("src/BIMBaoGui.Stage01/Stage02/Stage02Models.cs")
    canonicalizer = read("src/BIMBaoGui.Stage01/Stage02/Stage02PreviewCompiler.cs")
    confirmation = read(
        "src/BIMBaoGui.Stage01/Stage02/Stage02ConfirmationPolicy.cs"
    )
    assert "organizations" in preview
    assert "Stage02Stage01ProjectionPolicy.Resolve" in preview
    assert "AMBIGUOUS_STAGE01_ORGANIZATION" in preview + models
    assert "Stage01RecordIdentity" in models
    assert '"stage01RecordIdentity"' in canonicalizer
    assert "pair.Value.Stage01RecordIdentity" in confirmation
    assert "currentElement.Stage01RecordIdentity" in confirmation


def test_live_confirmation_uses_independent_current_selection_evidence():
    selection = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitSelectionService.cs")
    preview = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitPreviewService.cs")
    write = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitWriteService.cs")
    models = read("src/BIMBaoGui.Stage01/Stage02/Stage02Models.cs")
    for mode in ("CURRENT_SELECTION", "EXPLICIT_PICK", "PROJECT_INFORMATION"):
        assert mode in selection + preview + write + models
    assert "ReadCurrentSelectionInHostContext" in preview
    assert "CurrentSelectionEvidence" in write
    assert "BuildLiveConfirmationSnapshot" in write
    assert "request.CurrentSelectionEvidence" in write
    live_rebuild = preview[
        preview.index("BuildLiveConfirmationSnapshot") : preview.index(
            "private Stage02RevitPreviewResult CreatePreviewCore"
        )
    ]
    assert live_rebuild.count("preview.Elements.Select") == 1
    selection_set = live_rebuild.index("Stage02SelectionSetPolicy.Evaluate")
    expected_ids = live_rebuild.index("preview.Elements.Select")
    assert selection_set < expected_ids
    assert "ReadCurrentSelectionInHostContext" in live_rebuild


def test_transaction_failure_handling_forces_terminal_state_and_guards_pending():
    write = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitWriteService.cs")
    for token in (
        "IFailuresPreprocessor",
        "SetFailuresPreprocessor",
        "SetClearAfterRollback(true)",
        "SetForcedModalHandling(true)",
        "ITransactionFinalizer",
        "SetTransactionFinalizer",
        "OnCommitted",
        "OnRolledBack",
        "FailureProcessingResult.ProceedWithRollBack",
        "TransactionStatus.Pending",
        "Stage02TransactionStatePolicy.CanRollbackGroup",
        "Stage02TransactionStatePolicy.CanDispose",
    ):
        assert token in write
    pending = write.index("TransactionStatus.Pending")
    assert "DeferredToFinalizer" in write[pending:]


def test_preconsumption_selection_rejection_stays_out_of_failure_reporting():
    write = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitWriteService.cs")
    contract_catch = write.index("catch (Stage02ContractException exception)")
    general_catch = write.index("catch (Exception exception)", contract_catch)
    assert contract_catch < general_catch
    assert "Stage02PreConsumptionUiPolicy.Decide" in write[
        contract_catch:general_catch
    ]
    handled = write.index(
        "if (decision.Handled && !decision.ShouldWriteFailureReport)",
        contract_catch,
    )
    handled_return = write.index("return;", handled)
    assert "execution.Complete" in write[handled:handled_return]
    assert "execution.Fail" not in write[handled:handled_return]


def test_nonterminal_transaction_or_group_defers_to_revit_idling():
    write = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitWriteService.cs")
    assert "_uiApplication.Idling += OnDeferredCompletionIdling" in write
    assert "_uiApplication.Idling -= OnDeferredCompletionIdling" in write
    assert "Interlocked.CompareExchange(ref _idlingScheduled, 1, 0)" in write
    assert "ScheduleDeferredCompletion(terminalStatus, false, false)" in write
    assert "ScheduleDeferredCompletion(string.Empty, false, true)" in write
    group_defer = write.index("if (groupDecision.ShouldDefer)")
    deferred_return = write.index("return;", group_defer)
    assert "ScheduleDeferredCompletion" in write[group_defer:deferred_return]
    assert "Complete(" not in write[group_defer:deferred_return]
    assert "if (groupDecision.ShouldFailClosed)" in write
    for forbidden in ("Thread.Sleep", "Task.Run", "while ("):
        assert forbidden not in write


def test_each_idling_callback_unsubscribes_before_one_terminal_advance():
    write = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitWriteService.cs")
    callback = write[
        write.index("private void OnDeferredCompletionIdling") :
        write.index("private void DisposeTerminalObjects")
    ]
    unsubscribe = callback.index(
        "_uiApplication.Idling -= OnDeferredCompletionIdling"
    )
    reset_gate = callback.index(
        "Interlocked.Exchange(ref _idlingScheduled, 0)"
    )
    advance = callback.index("Stage02DeferredTransactionPolicy.Advance")
    assert callback.count(
        "_uiApplication.Idling -= OnDeferredCompletionIdling"
    ) == 1
    assert unsubscribe < reset_gate < advance
    assert callback.count("Stage02DeferredTransactionPolicy.Advance") == 1


def test_fatal_unknown_transaction_status_reports_once_without_unsafe_cleanup():
    write = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitWriteService.cs")
    callback = write[
        write.index("private void OnDeferredCompletionIdling") :
        write.index("private void DisposeTerminalObjects")
    ]
    assert "decision.ShouldFailClosed" in callback
    assert "CompleteFatalUnknownTransactionStatus" in callback
    fatal = write[
        write.index("private void CompleteFatalUnknownTransactionStatus") :
        write.index("private void DisposeTerminalObjects")
    ]
    assert "CompleteFailureWithoutUnsafeCleanup" in fatal
    assert "ScheduleDeferredCompletion" not in fatal
    assert "Dispose" not in fatal
    assert "_transaction.RollBack" not in fatal
    assert "TRANSACTION_STATUS_FATAL_UNKNOWN" in fatal
    complete = write[
        write.index("internal void Complete(Stage02RevitWriteResult result)") :
        write.index("public FailureProcessingResult PreprocessFailures")
    ]
    enqueue = write[
        write.index("internal bool EnqueueWrite(") :
        write.index("private static Stage02RevitWriteResult", write.index("internal bool EnqueueWrite("))
    ]
    assert "Stage02PreparationCompletionGate<Stage02RevitWriteResult>" in enqueue
    assert enqueue.count("completionGate.TryComplete") == 2
    assert "_completed(result);" in complete
    assert "_completionIssued" not in complete
    assert "catch" not in complete


def test_terminal_handoff_conflict_rolls_back_group_and_reports_both_statuses():
    write = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitWriteService.cs")
    handler = write[
        write.index("private void HandleEndCallReturn") :
        write.index("private void FinalizeFromCallback")
    ]
    assert "decision.TerminalConflict" in handler
    assert "decision.FinalizerTerminalStatus" in handler
    assert "decision.EndCallTerminalStatus" in handler
    conflict = handler[
        handler.index("if (decision.TerminalConflict)") :
        handler.index("ScheduleDeferredCompletion")
    ]
    assert 'FinalizeTerminalNoThrow("RolledBack")' in conflict
    assert "FinalizeTerminalNoThrow(\"Committed\")" not in conflict


def test_deferred_exception_budget_stops_transaction_and_group_requeue():
    write = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitWriteService.cs")
    assert "Stage02DeferredFailureBudget(3)" in write
    assert "_transactionDeferredFailureBudget" in write
    assert "_groupDeferredFailureBudget" in write
    callback = write[
        write.index("private void OnDeferredCompletionIdling") :
        write.index("private void CompleteFatalUnknownTransactionStatus")
    ]
    assert "RegisterFailure" in callback
    assert "decision.ShouldFailClosed" in callback
    fatal = callback[callback.index("decision.ShouldFailClosed") :]
    assert "CompleteFatalDeferredException" in fatal
    assert "ScheduleDeferredCompletion" in callback


def test_group_unknown_is_fatal_once_without_dispose_or_requeue():
    write = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitWriteService.cs")
    assert "Stage02DeferredGroupPolicy.Advance" in write
    method = write[
        write.index("private void CompleteFatalUnknownGroupStatus") :
        write.index("private void CompleteFatalUnknownTransactionStatus")
    ]
    assert "CompleteFailureWithoutUnsafeCleanup" in method
    assert "ScheduleDeferredCompletion" not in method
    assert "Dispose" not in method
    assert "RollBack" not in method


def test_group_unknown_preserves_nonblank_observed_transaction_status():
    write = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitWriteService.cs")
    method = write[
        write.index("private void CompleteFatalUnknownGroupStatus") :
        write.index("private void CompleteFatalUnknownTransactionStatus")
    ]
    assert "if (!string.IsNullOrWhiteSpace(transactionStatus))" in method
    assert "_lastObservedTransactionStatus = transactionStatus;" in method
    assert (
        "_lastObservedTransactionStatus = transactionStatus ?? string.Empty;"
        not in method
    )


def test_failure_report_separates_transaction_and_group_rollback():
    report = read("src/BIMBaoGui.Stage01/Diagnostics/Stage02FailureReportWriter.cs")
    write = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitWriteService.cs")
    assert '"transactionRolledBack"' in report
    assert '"groupRolledBack"' in report
    assert "bool transactionRolledBack" in write
    assert "bool groupRolledBack" in write


def test_failure_report_preserves_root_cause_and_records_cleanup_stage():
    report = read("src/BIMBaoGui.Stage01/Diagnostics/Stage02FailureReportWriter.cs")
    write = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitWriteService.cs")
    for token in (
        "RootCauseStage",
        "CleanupStage",
        '["rootCauseStage"]',
        '["cleanupStage"]',
    ):
        assert token in report

    assert 'private string _rootCauseStage = "TRANSACTION_SETUP";' in write
    assert "private string _cleanupStage = string.Empty;" in write
    assert "_operationStage" not in write

    fail = write[
        write.index("internal void Fail(") :
        write.index("internal void Complete(Stage02RevitWriteResult result)")
    ]
    assert "string failureStage = operationStage ?? string.Empty;" in fail
    assert "RecordFailure(exception, failureStage, false);" in fail
    assert "_rootCauseStage =" not in fail
    failure_record = fail.index("RecordFailure(exception, failureStage, false);")
    cleanup_start = fail.index('RecordCleanupStage("TRANSACTION_ROLLBACK");')
    assert failure_record < cleanup_start

    close_group = write[
        write.index("private string CloseStartedGroup") :
        write.index("private void CompleteTerminalTransactionGroup")
    ]
    assert (
        'RecordCleanupStage("TRANSACTION_GROUP_ASSIMILATE");'
        in close_group
    )
    assert 'RecordCleanupStage("TRANSACTION_GROUP_ROLLBACK");' in close_group

    fatal_cleanup = write[
        write.index("private void CompleteFatalDeferredException") :
        write.index("private void DisposeTerminalObjects")
    ]
    for stage in (
        "TRANSACTION_DEFERRED_FATAL_EXCEPTION",
        "TRANSACTION_GROUP_DEFERRED_FATAL_EXCEPTION",
        "TRANSACTION_GROUP_STATUS_FATAL_UNKNOWN",
        "TRANSACTION_STATUS_FATAL_UNKNOWN",
    ):
        assert stage in fatal_cleanup
    assert "RecordFailure" in fatal_cleanup
    assert fatal_cleanup.count("true);") >= 3

    tracker = write[
        write.index("private void RecordCleanupStage") :
        write.index("private static string StatusName")
    ]
    assert "_cleanupStage = cleanupStage ?? string.Empty;" in tracker
    cleanup_tracker = tracker[
        tracker.index("private void RecordCleanupStage") :
        tracker.index("private void RecordFailure")
    ]
    assert "_rootCauseStage" not in cleanup_tracker
    failure_tracker = tracker[tracker.index("private void RecordFailure") :]
    assert "if (_failure == null)" in failure_tracker
    assert "_rootCauseStage = normalizedStage;" in failure_tracker
    assert "_failure = Combine(_failure, exception);" in failure_tracker

    build = write[
        write.index("private Stage02RevitWriteResult BuildFailureResult") :
        write.index("private string ReadTransactionStatus")
    ]
    assert "OperationStage = _rootCauseStage" in build
    assert "RootCauseStage = _rootCauseStage" in build
    assert "CleanupStage = _cleanupStage" in build


def test_assimilate_first_failure_locks_root_to_assimilate_stage():
    write = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitWriteService.cs")
    close_group = write[
        write.index("private string CloseStartedGroup") :
        write.index("private void CompleteTerminalTransactionGroup")
    ]
    enter_assimilate = close_group.index(
        'RecordCleanupStage("TRANSACTION_GROUP_ASSIMILATE");'
    )
    assimilate_call = close_group.index("_group.Assimilate()")
    assert enter_assimilate < assimilate_call

    finalize = write[
        write.index("private void FinalizeTerminal(string terminalStatus)") :
        write.index("private string CloseStartedGroup")
    ]
    noncommit = finalize[
        finalize.index("if (mayAssimilate") :
        finalize.index("if (!mayAssimilate")
    ]
    assert "RecordFailure(" in noncommit
    assert "_cleanupStage" in noncommit
    assert "true" in noncommit


def test_group_status_read_failure_replaces_stale_transaction_cleanup_stage():
    write = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitWriteService.cs")
    finalize = write[
        write.index("private void FinalizeTerminal(string terminalStatus)") :
        write.index("private string CloseStartedGroup")
    ]
    group_stage = finalize.index(
        'RecordCleanupStage("TRANSACTION_GROUP_FINALIZE");'
    )
    group_advance = finalize.index("Stage02DeferredGroupPolicy.Advance")
    group_status_read = finalize.index("ReadGroupStatus", group_advance)
    assert group_stage < group_advance < group_status_read

    group_only = write[
        write.index("private void CloseGroupWithoutStartedTransactionCore") :
        write.index("private void ScheduleDeferredCompletion")
    ]
    group_only_stage = group_only.index(
        'RecordCleanupStage("TRANSACTION_GROUP_FINALIZE");'
    )
    group_only_advance = group_only.index("Stage02DeferredGroupPolicy.Advance")
    group_only_status_read = group_only.index(
        "ReadGroupStatus",
        group_only_advance,
    )
    assert group_only_stage < group_only_advance < group_only_status_read


def test_dispose_failure_uses_dispose_stage_not_successful_assimilate():
    write = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitWriteService.cs")
    dispose = write[
        write.index("private void DisposeTerminalObjects") :
        write.index("private void CompleteFailureWithoutUnsafeCleanup")
    ]
    assert "RecordFailure(" in dispose
    assert '"TRANSACTION_DISPOSE"' in dispose
    assert '"TRANSACTION_GROUP_DISPOSE"' in dispose
    assert dispose.count("true);") >= 2


def test_start_failure_reports_last_observed_transaction_and_group_statuses():
    write = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitWriteService.cs")
    group_only = write[
        write.index("private void CloseGroupWithoutStartedTransactionCore") :
        write.index("private void ScheduleDeferredCompletion")
    ]
    assert "_lastObservedTransactionStatus" in group_only[
        group_only.index("Complete(BuildFailureResult") :
    ]
    fatal = write[
        write.index("private void CompleteFailureWithoutUnsafeCleanup") :
        write.index("private Stage02RevitWriteResult BuildFailureResult")
    ]
    assert "observedTransactionStatus" in fatal
    assert "observedGroupStatus" in fatal
    assert "_lastObservedTransactionStatus" in fatal
    assert "_lastObservedGroupStatus" in fatal


def test_live_domain_drift_is_structured_before_nonce_consumption():
    preview = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitPreviewService.cs")
    write = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitWriteService.cs")
    policy = read("src/BIMBaoGui.Stage01/Stage02/Stage02PreConsumptionUiPolicy.cs")
    assert "Stage02SelectionSetPolicy.Evaluate" in preview
    selection_check = preview.index("Stage02SelectionSetPolicy.Evaluate")
    rebuild = preview.index("BuildPreviewOrThrow", selection_check)
    assert selection_check < rebuild
    for code in (
        "Stage02Codes.FileContextChanged",
        "Stage02Codes.DocumentFingerprintChanged",
        "Stage02Codes.ElementSetChanged",
    ):
        assert code in preview
    assert "Stage02MetadataStorage.ReadSavedRole" in preview
    assert "ResolveSavedRole(expectedPreview" not in preview
    assert "consumed" in policy
    assert "InvalidSelectionEvidence" not in policy[
        policy.index("if (exception == null") : policy.index(
            "IReadOnlyList<Stage02Blocker> blockers"
        )
    ]
    contract_catch = write.index("catch (Stage02ContractException exception)")
    general_catch = write.index("catch (Exception exception)", contract_catch)
    handled = write[contract_catch:general_catch]
    assert "execution.Complete" in handled
    assert handled.index("execution.Complete") < handled.index("execution.Fail")


def test_preview_rebuild_reserves_plain_exceptions_for_technical_faults():
    preview = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitPreviewService.cs")
    rebuild = preview[
        preview.index("private Stage02Preview BuildPreviewOrThrow") :
        preview.index("private static ElementBinding FindBinding")
    ]
    assert rebuild.count("throw new InvalidOperationException") == 1
    assert "Stage02 实时文档指纹计算结果不一致" in rebuild
    assert "Stage02Codes.ElementSnapshotChanged" in rebuild
    assert "Stage02Codes.RulePackageIdentityMismatch" in rebuild


def test_terminal_group_checks_disposability_before_claiming_gate():
    write = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitWriteService.cs")
    method = write[
        write.index("private void CompleteTerminalTransactionGroup") :
        write.index("private void CloseGroupWithoutStartedTransaction")
    ]
    assert method.index("Stage02TransactionStatePolicy.CanDispose") < method.index(
        "_cleanupGate.TryClaimTerminal"
    )


def test_fatal_outcome_allows_late_terminal_cleanup_without_second_outcome():
    write = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitWriteService.cs")
    callback = write[
        write.index("private void FinalizeFromCallback") :
        write.index("private void FinalizeTerminalNoThrow")
    ]
    finalize = write[
        write.index("private void FinalizeTerminal(string terminalStatus)") :
        write.index("private string CloseStartedGroup")
    ]
    completion = write[
        write.index("private void CompleteTerminalTransactionGroup") :
        write.index("private void CloseGroupWithoutStartedTransaction")
    ]
    schedule = write[
        write.index("private void ScheduleDeferredCompletion") :
        write.index("private void OnDeferredCompletionIdling")
    ]
    idling = write[
        write.index("private void OnDeferredCompletionIdling") :
        write.index("private void HandleDeferredException")
    ]

    assert "_outcomeGate.IsClaimed) return" not in callback
    assert "_outcomeGate.IsClaimed) return" not in finalize
    assert "&& !_outcomeGate.IsClaimed" in finalize
    assert "Stage02ExecutionCleanupGate" in write
    assert "Stage02LateCleanupCoordinator" in write
    assert "_lateCleanup.ObserveTerminal" in callback
    assert "_cleanupGate.TryClaimTerminal" in completion
    assert completion.index("Stage02TransactionStatePolicy.CanDispose") < completion.index(
        "_cleanupGate.TryClaimTerminal"
    )
    assert completion.index("_cleanupGate.TryClaimTerminal") < completion.index(
        "DisposeTerminalObjects"
    )
    assert completion.index("DisposeTerminalObjects") < completion.index(
        "_outcomeGate.TryClaim"
    )
    assert "_outcomeGate.IsClaimed) return" not in schedule
    assert "_outcomeGate.IsClaimed) return" not in idling
    fatal = write[
        write.index("private void CompleteFailureWithoutUnsafeCleanup") :
        write.index("private Stage02RevitWriteResult BuildFailureResult")
    ]
    assert "_lateCleanup.DeclareFailureOutcome" in fatal
    assert "FinalizeTerminalNoThrow" in fatal


def test_known_terminal_cleanup_is_attempted_before_failure_callback():
    write = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitWriteService.cs")
    fatal = write[
        write.index("private void CompleteFailureWithoutUnsafeCleanup") :
        write.index("private Stage02RevitWriteResult BuildFailureResult")
    ]
    decision = fatal.index("if (lateCleanupDecision.ShouldAttemptCleanup)")
    cleanup = fatal.index("FinalizeTerminalNoThrow", decision)
    completed = fatal.index("Complete(BuildFailureResult")
    assert decision < cleanup < completed


def test_late_cleanup_report_uses_post_cleanup_status_and_rollback_evidence():
    write = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitWriteService.cs")
    fatal = write[
        write.index("private void CompleteFailureWithoutUnsafeCleanup") :
        write.index("private Stage02RevitWriteResult BuildFailureResult")
    ]
    cleanup = fatal.index("FinalizeTerminalNoThrow")
    refreshed_transaction = fatal.index("_lastObservedTransactionStatus", cleanup)
    refreshed_group = fatal.index("_lastObservedGroupStatus", cleanup)
    build = fatal.index("Complete(BuildFailureResult")
    assert cleanup < refreshed_transaction < build
    assert cleanup < refreshed_group < build
    assert "bool transactionRolledBack" in fatal
    assert "bool groupRolledBack" in fatal
    assert "transactionRolledBack," in fatal[build:]
    assert "groupRolledBack," in fatal[build:]


def test_all_terminal_paths_record_late_cleanup_before_group_work():
    write = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitWriteService.cs")
    finalize = write[
        write.index("private void FinalizeTerminal(string terminalStatus)") :
        write.index("private string CloseStartedGroup")
    ]
    terminal_guard = finalize.index(
        "Stage02TransactionStatePolicy.IsTerminal(terminalStatus)"
    )
    observe = finalize.index("_lateCleanup.ObserveTerminal(terminalStatus)")
    group_work = finalize.index("Stage02DeferredGroupPolicy.Advance")
    assert terminal_guard < observe < group_work


def test_unknown_binding_scope_is_never_treated_as_instance():
    installer = read(
        "src/BIMBaoGui.Stage01/Revit/Parameters/HbrSharedParameterInstaller.cs"
    )
    assert installer.count("HbrBindingScopePolicy.RequiresTypeBinding") >= 2
    assert 'property.Revit.BindingScope,\n        "TYPE"' not in installer


def test_initial_failure_records_second_observed_transaction_status():
    write = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitWriteService.cs")
    fail = write[
        write.index("internal void Fail(") :
        write.index("internal void Complete(Stage02RevitWriteResult result)")
    ]
    second_read = fail.index(
        "TransactionStatus liveStatus = _transaction.GetStatus();",
        fail.index("catch (Exception rollbackException)"),
    )
    schedule = fail.index("ScheduleDeferredCompletion", second_read)
    assert "_lastObservedTransactionStatus = StatusName(liveStatus)" in fail[
        second_read:schedule
    ]


def test_rejected_start_disposes_only_explicit_nonactive_wrappers():
    write = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitWriteService.cs")
    start_group = write[
        write.index("internal void StartGroup") :
        write.index("internal void StartTransaction")
    ]
    start_transaction = write[
        write.index("internal void StartTransaction") :
        write.index("internal void Commit")
    ]
    fail = write[
        write.index("internal void Fail(") :
        write.index("internal void Complete(Stage02RevitWriteResult result)")
    ]
    assert "CanDisposeAfterRejectedStart" in start_group
    assert "_group.Dispose()" in start_group
    assert "_transaction.Dispose()" not in start_transaction
    unknown_transaction = fail.index("if (_transaction != null")
    guard_end = fail.index("return;", unknown_transaction)
    assert "!Stage02TransactionStatePolicy.CanDisposeAfterRejectedStart" in fail[
        unknown_transaction:guard_end
    ]
    group_close = fail.index("CloseGroupWithoutStartedTransaction()")
    assert unknown_transaction < group_close
    group_only = write[
        write.index("private void CloseGroupWithoutStartedTransactionCore") :
        write.index("private void ScheduleDeferredCompletion")
    ]
    terminal_cleanup = group_only[group_only.index(
        "_cleanupGate.TryClaimGroupOnlyTerminal"
    ) :]
    assert "CanDisposeAfterRejectedStart" in terminal_cleanup
    transaction_dispose = terminal_cleanup.index("_transaction.Dispose()")
    group_dispose = terminal_cleanup.index("_group.Dispose()")
    assert transaction_dispose < group_dispose
    assert terminal_cleanup.count("catch (Exception disposeException)") >= 2


def test_rejected_group_start_dispose_failure_records_cleanup_stage():
    write = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitWriteService.cs")
    start_group = write[
        write.index("internal void StartGroup") :
        write.index("internal void StartTransaction")
    ]
    dispose_catch = start_group[
        start_group.index("catch (Exception disposeException)") :
    ]
    cleanup_stage = dispose_catch.index(
        'RecordCleanupStage("TRANSACTION_GROUP_DISPOSE");'
    )
    aggregate = dispose_catch.index(
        "throw new AggregateException(startFailure, disposeException);"
    )
    assert cleanup_stage < aggregate


def test_handoff_conflict_report_status_is_not_synthetic_rollback():
    write = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitWriteService.cs")
    handler = write[
        write.index("if (decision.TerminalConflict)") :
        write.index("if (isPending && decision.DeferredToFinalizer)")
    ]
    assert '_transactionStatusForReport = "CONFLICT"' in handler
    report = write[
        write.index("private Stage02RevitWriteResult BuildFailureResult") :
        write.index("private string ReadTransactionStatus")
    ]
    assert "_transactionStatusForReport" in report


def test_internal_yesno_raw_zero_one_is_reusable_but_external_stays_strict():
    parser = read(
        "src/BIMBaoGui.Stage01/Revit/Parameters/HbrInvariantValueParser.cs"
    )
    converter = read(
        "src/BIMBaoGui.Stage01/Revit/Parameters/HbrParameterValueConverter.cs"
    )
    preview = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitPreviewService.cs")
    assert "bool sourceAlreadyUsesInternalUnits" in parser
    assert "sourceAlreadyUsesInternalUnits" in converter
    parser_call = converter[
        converter.index("HbrInvariantValueParser") :
        converter.index("if (!parsed.Success)")
    ]
    assert "sourceAlreadyUsesInternalUnits" in parser_call
    assert "HbrSuggestionSources.Stage01Projection" in preview
    assert "!string.Equals(" in preview


def test_named_suggestion_captures_and_validates_source_parameter_contract():
    policy = read(
        "src/BIMBaoGui.Stage01/Revit/Parameters/HbrParameterSuggestionPolicy.cs"
    )
    preview = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitPreviewService.cs")
    for token in (
        "SourceStorageType",
        "SourceParameterType",
        "SourceParameterGuid",
        "SourceTypeCompatible",
        "SourceAlreadyUsesInternalUnits",
        "RuleAliasPropertyCount",
        "SUGGESTION_SOURCE_TYPE_MISMATCH",
        "AMBIGUOUS_SUGGESTION_ALIAS_RULE",
    ):
        assert token in policy
    assert "parameter.StorageType.ToString()" in preview
    assert "parameter.Definition.ParameterType.ToString()" in preview
    assert "HbrNamedParameterCompatibilityPolicy.Evaluate" in preview
    converter_call = preview[
        preview.index("HbrParameterValueConverter") :
        preview.index("string suggestedInternalRaw")
    ]
    assert "suggestion.SourceAlreadyUsesInternalUnits" in converter_call
    assert "HbrSuggestionSources.Stage01Projection" not in converter_call
    assert "GetSuggestionAliasPropertyIds" in preview
    assert 'valueAction = "NO_WRITE"' in preview


def test_assimilate_rollback_records_failure_before_terminal_completion():
    write = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitWriteService.cs")
    finalize = write.index("private void FinalizeTerminal(string terminalStatus)")
    completion = write.index("private void CompleteTerminalTransactionGroup")
    terminal_handling = write[finalize:completion]
    assert "Stage02DeferredGroupPolicy.Advance" in terminal_handling
    assert "if (mayAssimilate" in terminal_handling
    assert 'groupStatus,\n          "Committed"' in terminal_handling
    assert "RecordFailure(" in terminal_handling
    assert "_cleanupStage" in terminal_handling
    assert "Assimilate 未提交" in terminal_handling


def test_temporary_shared_parameter_cleanup_is_reportable_not_swallowed():
    installer = read(
        "src/BIMBaoGui.Stage01/Revit/Parameters/HbrSharedParameterInstaller.cs"
    )
    cleanup = read(
        "src/BIMBaoGui.Stage01/Revit/Parameters/HbrTemporaryFileCleanup.cs"
    )
    report = read("src/BIMBaoGui.Stage01/Diagnostics/Stage02FailureReportWriter.cs")
    assert "DeleteTemporaryFile" in installer + cleanup
    assert "AggregateException" in installer + cleanup
    assert "Flatten()" in report


def test_metadata_is_audit_only_and_excludes_business_values():
    text = read("src/BIMBaoGui.Stage01/Revit/Stage02MetadataStorage.cs")
    for required in (
        "SchemaGuid",
        "RoleId",
        "RulePackageId",
        "RulePackageVersion",
        "RulePackageSha256",
        "PreviewHash",
        "UniqueId",
        "PropertyId",
    ):
        assert required in text
    for forbidden in (
        "SuggestedValue",
        "OldValue",
        "BusinessValues",
        "RawValue",
        "CanonicalPayload",
        "PayloadJson",
    ):
        assert forbidden not in text


def test_failure_report_is_same_assembly_directory_atomic_and_redacted():
    text = read("src/BIMBaoGui.Stage01/Diagnostics/Stage02FailureReportWriter.cs")
    assert ".Assembly.Location" in text
    assert "BIMBaoGui.Stage02.failure-" in text
    assert '"yyyyMMdd-HHmmss-fff"' in text
    assert '".tmp"' in text
    assert "File.Move" in text
    assert "new UTF8Encoding(false)" in text
    assert "REPORT_WRITE_FAILED" in text
    for forbidden in (
        ".bak",
        ".backup",
        "SuggestedValue",
        "OldValue",
        "RawValue",
        "CanonicalPayload",
        "PayloadJson",
        "SourceValue",
        "Alias",
    ):
        assert forbidden not in text


def test_family_documents_are_rejected_before_parameter_bindings_access():
    preview = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitPreviewService.cs")
    installer = read(
        "src/BIMBaoGui.Stage01/Revit/Parameters/HbrSharedParameterInstaller.cs"
    )
    assert "IsFamilyDocument" in preview
    assert "IsFamilyDocument" in installer
    assert preview.index("IsFamilyDocument") < preview.index("ParameterBindings")
    assert installer.index("IsFamilyDocument") < installer.index("ParameterBindings")
