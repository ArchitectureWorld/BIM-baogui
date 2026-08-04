from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def read(relative_path: str) -> str:
    path = ROOT / relative_path
    assert path.is_file(), f"missing Task 9 production file: {relative_path}"
    return path.read_text(encoding="utf-8")


def test_export_is_explicit_ifc4_inside_transaction():
    text = read("src/BIMBaoGui.Stage01/Revit/AutodeskIfcExportService.cs")
    for token in (
        "new IFCExportOptions",
        "FileVersion = IFCVersion.IFC4",
        "new Transaction",
        "document.Export",
        "File.Exists",
        "new FileInfo",
    ):
        assert token in text


def test_export_validates_an_unused_direct_ifc_path_and_rolls_back():
    text = read("src/BIMBaoGui.Stage01/Revit/AutodeskIfcExportService.cs")
    for token in (
        "Path.GetFullPath",
        "Path.IsPathRooted",
        "Directory.Exists",
        "Path.GetFileNameWithoutExtension",
        "TransactionStatus.Started",
        "RollBack",
        "TransactionStatus.RolledBack",
    ):
        assert token in text
    assert "File.Delete" not in text
    assert "Task.Run" not in text


def test_export_preserves_export_and_rollback_failures_separately():
    text = read("src/BIMBaoGui.Stage01/Revit/AutodeskIfcExportService.cs")
    for token in (
        "Exception exportFailure = null",
        "Exception rollbackFailure = null",
        "exportFailure = exception",
        "rollbackFailure = exception",
        "AutodeskIfcExportFailurePolicy.Combine",
        "ExceptionDispatchInfo.Capture(combinedFailure).Throw()",
    ):
        assert token in text
    assert "originalFailure" not in text


def test_export_captures_transaction_dispose_before_combining_failures():
    text = read("src/BIMBaoGui.Stage01/Revit/AutodeskIfcExportService.cs")
    compact = " ".join(text.split())
    for token in (
        "Transaction transaction = null",
        "Exception disposeFailure = null",
        "transaction = new Transaction",
        "transaction.Dispose()",
        "disposeFailure = exception",
    ):
        assert token in text
    assert "using (var transaction" not in text
    assert (
        "AutodeskIfcExportFailurePolicy.Combine( exportFailure, "
        "rollbackFailure, disposeFailure)"
        in compact
    )
    assert text.index("transaction.Dispose()") < text.index(
        "AutodeskIfcExportFailurePolicy.Combine"
    )


def test_scanner_uses_export_id_and_visible_parameter_guid():
    text = read("src/BIMBaoGui.Stage01/Revit/Stage03ModelScanService.cs")
    for token in (
        "ExportUtils.GetExportId",
        "SharedParameterElement.Lookup",
        "RulePackageSha256",
        "get_Parameter(property.Revit.ParameterGuid)",
        "HbrParameterValueConverter",
    ):
        assert token in text
    assert "LookupParameter" not in text
    assert "DataStorage" not in text
    assert "Task.Run" not in text


def test_parameter_reader_converts_internal_double_to_canonical_external_unit():
    text = read(
        "src/BIMBaoGui.Stage01/Revit/Parameters/HbrParameterValueConverter.cs"
    )
    for token in (
        "TryReadCanonicalValue",
        "TryFromInternalDouble",
        "TryFromInternalInteger",
        "parameter.AsDouble()",
        'ToString("R", CultureInfo.InvariantCulture)',
    ):
        assert token in text
    assert "AsValueString" not in text


def test_string_reader_preserves_text_but_treats_whitespace_as_empty_required():
    converter = read(
        "src/BIMBaoGui.Stage01/Revit/Parameters/HbrParameterValueConverter.cs"
    )
    for token in (
        "HbrParameterTextValuePolicy.Evaluate",
        "textDecision.RawValue",
        "textDecision.CanonicalValue",
        "textDecision.HasBusinessValue",
    ):
        assert token in converter

    scanner = read("src/BIMBaoGui.Stage01/Revit/Stage03ModelScanService.cs")
    empty_gate = "if (!read.HasValue || read.CanonicalValue.Length == 0)"
    canonical_validation = "HbrIfcCanonicalValuePolicy.Validate"
    enrichment = "var enrichment = new HbrIfcEnrichmentValue"
    assert empty_gate in scanner
    assert "Stage03FieldStatus.EmptyRequiredValue" in scanner
    assert scanner.index(empty_gate) < scanner.index(canonical_validation)
    assert scanner.index(canonical_validation) < scanner.index(enrichment)


def test_scanner_indexes_profile_roles_categories_and_type_parameters():
    text = read("src/BIMBaoGui.Stage01/Revit/Stage03ModelScanService.cs")
    for token in (
        "ProfilesByModelFileType",
        "ProjectInformation",
        "FilteredElementCollector",
        "BuiltInCategory",
        "GetTypeId()",
        "document.GetElement",
        "IfcGuidCodec.Encode",
        "HbrIfcCanonicalValuePolicy.Validate",
        "Stage03IfcOwnerStrategyPolicy.Evaluate",
        "Stage03RequirementApplicabilityPolicy.Evaluate",
        "Stage03FieldStatusPolicy.Resolve",
        "HbrIfcEnrichmentValue",
        "TechnicalFatalCodes",
        "DocumentFingerprint",
        "RawIfcStatus = Stage03FieldStatus.NotEvaluated",
        "FinalIfcStatus = Stage03FieldStatus.NotEvaluated",
        '"UNCLASSIFIED"',
    ):
        assert token in text
    assert "IsImplementedOwnerStrategy" not in text
    assert text.index("if (!strategy.Implemented)") < text.index(
        "ExportUtils.GetExportId"
    )


def test_scanner_uses_stage03_context_identity_without_official_protocol_gate():
    text = read("src/BIMBaoGui.Stage01/Revit/Stage03ModelScanService.cs")
    assert "Stage03ContextIdentityPolicy.Evaluate" in text
    assert "Stage03ActivationStatePolicy.Evaluate" in text
    assert "Stage02FileContextPolicy.IsVerified" not in text
    assert ".Except(profile.ActivationRuleIds" not in text


def test_scanner_applies_saved_role_and_alias_carrier_match_policy():
    text = read("src/BIMBaoGui.Stage01/Revit/Stage03ModelScanService.cs")
    for token in (
        "Stage03CarrierMatchPolicy.Evaluate",
        "Stage03CarrierScanAggregationPolicy.ShouldReportAlongsideAccepted",
        "Stage02MetadataStorage.ReadSavedRoles",
        "Stage02RevitSelectionService.CreateReference",
        "CarrierNameMismatch",
        "AmbiguousCarrier",
    ):
        assert token in text
    metadata = read("src/BIMBaoGui.Stage01/Revit/Stage02MetadataStorage.cs")
    for token in (
        "Stage03SavedRoleAuditSnapshot",
        "Stage03SavedRoleAuditPolicy.Select",
        'Get(entity, schema, "RulePackageId")',
        'Get(entity, schema, "RulePackageVersion")',
        'Get(entity, schema, "RulePackageSha256")',
    ):
        assert token in metadata


def test_scanner_scopes_saved_roles_to_current_rule_package():
    text = read("src/BIMBaoGui.Stage01/Revit/Stage03ModelScanService.cs")
    compact = " ".join(text.split())
    assert (
        "BuildCandidateSnapshots( uiApplication, document, package, "
        "elementsByCategory.Values.SelectMany(values => values))"
        in compact
    )
    assert (
        "Stage02MetadataStorage.ReadSavedRoles( document, "
        "uniqueElements.Select(element => element.UniqueId), "
        "package.PackageId, package.PackageVersion, package.RulePackageSha256)"
        in compact
    )


def test_revit_phase_keeps_scan_and_export_as_separate_host_seams():
    text = read("src/BIMBaoGui.Stage01/Revit/Stage03RevitPhaseService.cs")
    for token in (
        "RevitHost.EnqueueAction",
        "Stage03ModelScanService",
        "AutodeskIfcExportService",
        "ScanInHostContext",
        "ExportInHostContext",
        "TaskCompletionSource",
        "RunContinuationsAsynchronously",
        "TrySetResult",
        "TrySetException",
        "Stage03RevitRequestIdentityPolicy.Evaluate",
        "Stage03RevitRequestRulePackagePolicy.Evaluate",
        "if (!enqueued)",
    ):
        assert token in text
    assert "Task.Run" not in text
    assert "Task.FromResult" not in text


def test_revit_phase_faults_when_host_callback_never_starts():
    text = read("src/BIMBaoGui.Stage01/Revit/Stage03RevitPhaseService.cs")
    for token in (
        "Stage03HostCallbackStartGate",
        "CallbackStartTimeout",
        "new Timer",
        "TryStart",
        "TryAbandon",
        "TimeoutException",
        "hostFailure",
        "RevitHost.EnqueueAction",
    ):
        assert token in text
    assert "Task.Run" not in text


def test_revit_host_forwards_current_and_legacy_callback_failures():
    text = read("src/BIMBaoGui.Stage01/Revit/RevitHost.cs")
    compact = " ".join(text.split())
    for token in (
        "Action<Exception> callbackFailure",
        "TryInvokeLegacyQueue",
        "Action<Document>",
        "Action<UIApplication>",
        "callbackFailure(Unwrap(exception))",
    ):
        assert token in text
    assert "return EnqueueAction(uiAction, null, out error);" in compact
    assert text.count("InvokeUiAction(") >= 5


def test_revit_host_defers_document_validation_to_business_callback():
    text = read("src/BIMBaoGui.Stage01/Revit/RevitHost.cs")
    compact = " ".join(text.split())
    start = text.index("private static void InvokeUiAction(")
    end = text.index("private static Type ResolveRhinoInsideType", start)
    invoke_ui_action = text[start:end]

    assert "RevitHostCallbackInvoker.Invoke" in invoke_ui_action
    assert "ReadStaticProperty<UIApplication>" in invoke_ui_action
    assert "ActiveDBDocument" not in invoke_ui_action
    assert "currentDocument" not in invoke_ui_action
    assert "ActiveUIDocument?.Document" not in invoke_ui_action
    assert "return EnqueueAction(uiAction, null, out error);" in compact
    assert compact.count("uiAction, callbackFailure") >= 4


def test_revit_host_resolves_uiapplication_inside_failure_seam():
    text = read("src/BIMBaoGui.Stage01/Revit/RevitHost.cs")
    start = text.index("private static void InvokeUiAction(")
    end = text.index("private static Type ResolveRhinoInsideType", start)
    invoke_ui_action = text[start:end]
    compact = " ".join(invoke_ui_action.split())

    assert "UIApplication current =" not in invoke_ui_action
    assert (
        "RevitHostCallbackInvoker.Invoke( () => uiApplication ?? "
        "ReadStaticProperty<UIApplication>( hostType, \"ActiveUIApplication\"),"
        in compact
    )
    assert "callbackFailure(Unwrap(exception))" in invoke_ui_action


def test_export_result_carries_verified_raw_path_size_hash_and_transaction():
    text = read("src/BIMBaoGui.Stage01/Revit/AutodeskIfcExportService.cs")
    for token in (
        "RawIfcPath",
        "RawIfcLength",
        "RawIfcSha256",
        "TransactionStrategy",
        "SHA256.Create",
    ):
        assert token in text
