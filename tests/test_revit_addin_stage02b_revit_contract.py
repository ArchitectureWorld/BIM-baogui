import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "src" / "BIMBaoGui.RevitAddin"
STAGE02B = PROJECT / "Stage02B"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def test_stage02b_uses_one_fixed_extensible_storage_contract():
    source = read(STAGE02B / "NativeStage02BStorage.cs")
    assert '"420ba043-1d47-4f29-a97e-f33c75e18385"' in source
    assert '"HBR_NATIVE_STAGE02B_METRICS_V1"' in source
    assert '"HBR Native Stage02B Metrics"' in source
    assert '"SchemaVersion"' in source
    assert '"CanonicalJson"' in source
    assert "NativeStage02BStoragePolicy.Merge" in source
    assert "document.IsModifiable" in source


def test_stage02b_write_is_metric_transactional_and_audits_failure_separately():
    source = read(STAGE02B / "NativeStage02BRevitWriteService.cs")
    assert re.search(
        r"foreach\s*\(NativeStage02BMetricInput.*?new\s+Transaction\s*\(",
        source,
        re.S,
    )
    assert "HBR Stage02B 指标" in source
    assert "HBR Stage02B 失败审计" in source
    assert "transaction.RollBack()" in source
    assert "NativeStage02BStorage.WriteMetric" in source
    assert "NativeStage02ValueCodec.WriteAndVerify" in source
    assert "document.Regenerate()" in source
    assert "continue;" in source
    assert re.search(r"NativeStage02BWriteBatchPolicy\s*\.Merge", source)


def test_stage02b_requires_committed_status_before_emitting_any_success_evidence():
    source = read(STAGE02B / "NativeStage02BRevitWriteService.cs")
    assert re.search(
        r"NativeTransactionCommitPolicy\.RequireCommitted\s*\(\s*"
        r"transaction\.Commit\(\)\.ToString\(\)",
        source,
    )
    assert re.search(
        r"NativeTransactionCommitPolicy\.RequireCommitted\s*\(\s*"
        r"auditTransaction\.Commit\(\)\.ToString\(\)",
        source,
    )
    assert re.search(
        r"NativeTransactionCommitPolicy\.RequireCommitted\s*\(\s*"
        r"envelopeTransaction\.Commit\(\)\.ToString\(\)",
        source,
    )
    assert source.index("transaction.Commit()") < source.index(
        "outcomes.Add(new NativeStage02BMetricOutcome"
    )
    assert source.index("envelopeTransaction.Commit()") < source.index(
        "result.WorkflowResult = envelope"
    )


def test_stage02b_pending_site_and_spatial_zone_never_bind_project_information():
    source = read(STAGE02B / "NativeStage02BRevitWriteService.cs")
    owner = read(STAGE02B / "NativeStage02BOwnerPolicy.cs")
    assert '"BLOCKED_PENDING_GOLDEN_RVT"' in source
    assert "NativeStage02BProjectionMode.InternalStorageOnly" in owner
    assert 'string.Equals(entity, "IfcSite"' in owner
    assert 'string.Equals(entity, "IfcSpatialZone"' in owner
    assert 'new[] { "OST_ProjectInformation" }' in source
    assert "owner.ProjectionMode == NativeStage02BProjectionMode.ProjectInformation" in source


def test_verified_ifc_project_routes_through_the_structural_resolver():
    source = read(STAGE02B / "NativeStage02BRevitWriteService.cs")
    owner = read(STAGE02B / "NativeStage02BOwnerPolicy.cs")
    project_branch = owner[
        owner.index('string.Equals(entity, "IfcProject"') :
        owner.index('string.Equals(entity, "IfcSite"')
    ]
    assert "status == NativeOfficialCarrierEvidenceStatus.Verified" in project_branch
    assert "NativeStage02BProjectionMode.VerifiedElementParameter" in project_branch
    resolver_branch = source.index(
        "NativeStage02BProjectionMode.VerifiedElementParameter"
    )
    assert source.index("NativeStage02BProjectionCarrierResolver.Resolve", resolver_branch) > resolver_branch
    assert source.index("NativeStage02ParameterBindingService.Ensure", resolver_branch) > resolver_branch


def test_stage02b_result_is_rebuilt_from_full_six_metric_readback():
    source = read(STAGE02B / "NativeStage02BRevitWriteService.cs")
    canonicalizer = read(STAGE02B / "NativeStage02BWriteBatchPolicy.cs")
    assert "NativeStage02BStorage.Read(document)" in source
    assert "NativeStage02BResultCanonicalizer.Build" in source
    assert '"RESULT_ENVELOPE_WRITE_FAILED"' in source
    assert "NativeWorkflowResultStorage.Write" in source
    assert "metrics.Length != 6" in canonicalizer
    assert '"STAGE02B_FULL_READBACK_REQUIRED"' in canonicalizer
    assert '"PROJECT_ACTUAL_METRICS"' in canonicalizer


def test_stage02b_resolver_uses_only_structural_assignment_and_live_unique_id():
    source = read(STAGE02B / "NativeStage02BProjectionCarrierResolver.cs")
    assert '"PROJECT_INFORMATION"' in source
    assert '"CONFIRMED_SEMANTIC_ROLE"' in source
    assert "document.GetElement(assignment.ElementUniqueId)" in source
    assert "definition.RoleId" in source
    assert "definition.CategoryBuiltInId" in source
    assert "definition.ElementClass" in source
    assert "definition.ParameterGuid" in source
    assert "LookupParameter" not in source
    assert "ElementId" not in source
    assert "ElementName" not in source
    assert "Legacy" not in source


def test_stage02b_assignment_freshness_uses_persisted_document_and_task5_fact_hash():
    source = read(STAGE02B / "NativeStage02BRevitWriteService.cs")
    policy = read(STAGE02B / "NativeStage02BAssignmentFreshnessPolicy.cs")
    task5_capture = read(PROJECT / "Stage02" / "NativeStage02RevitService.cs")
    assert "storage?.Payload?.DocumentFingerprint" in policy
    assert "NativeStage02ElementSnapshotCanonicalizer.Sha256" in policy
    assert "NativeStage02RevitService.CreateSnapshot" in source
    assert "NativeStage02BAssignmentFreshnessPolicy.Evaluate" in source
    assert "internal static NativeStage02ElementSnapshot CreateSnapshot" in task5_capture
    assert "AssignmentDocumentFingerprint = current" not in source


def test_stage02b_ui_dispatches_deep_cloned_requests_only_through_external_event():
    dispatcher = read(PROJECT / "RevitExternalEventDispatcher.cs")
    assert "RequestStage02BRead" in dispatcher
    assert "RequestStage02BWrite" in dispatcher
    assert "NativeStage02BRevitReadService.Read" in dispatcher
    assert "NativeStage02BRevitWriteService.Execute" in dispatcher
    assert "request.Clone()" in dispatcher
    view = read(STAGE02B / "NativeStage02BView.cs")
    assert "RequestStage02BRead" in view
    assert "RequestStage02BWrite" in view
    assert "Autodesk.Revit.DB" not in view


def test_stage02b_does_not_scan_geometry_or_compute_actual_metrics():
    all_source = "\n".join(
        read(path)
        for path in STAGE02B.glob("*.cs")
        if path.name not in {"NativeStage02BStorage.cs"}
    )
    for forbidden in (
        "FilteredElementCollector",
        "GeometryElement",
        "BoundingBoxXYZ",
        "PlanarFace",
        "Tessellate",
        "ComputeArea",
        "get_BoundingBox",
    ):
        assert forbidden not in all_source
