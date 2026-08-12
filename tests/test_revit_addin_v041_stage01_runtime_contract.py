from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
STAGE01 = ROOT / "src/BIMBaoGui.RevitAddin/Stage01"
STAGE02 = ROOT / "src/BIMBaoGui.RevitAddin/Stage02/NativeStage02RevitService.cs"
STAGE03 = ROOT / "src/BIMBaoGui.RevitAddin/Stage03/NativeStage03Scanner.cs"
RULES = ROOT / "src/BIMBaoGui.RevitAddin/Rules/NativeRuleCatalog.cs"
MCP = ROOT / "src/BIMBaoGui.RevitAddin/McpBridge/McpStage01Adapter.cs"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def test_payload_091_has_an_explicit_versioned_migration_boundary():
    canonicalizer = read(STAGE01 / "NativeStage01Canonicalizer.cs")
    storage = read(STAGE01 / "NativeStage01StoragePolicy.cs")
    migration = read(STAGE01 / "NativeStage01MigrationService.cs")
    codec = read(STAGE01 / "NativeStage01PayloadCodec.cs")

    assert 'PayloadSchemaVersion = "0.9.1"' in canonicalizer
    assert "ToJson(NativeStage01Model model, string schemaVersion)" in canonicalizer
    assert 'SupportedSourceVersion = "0.9.0"' in migration
    assert "NativeStage01ConditionSchemaPolicy.Reconcile" in migration
    assert "NativeProjectConditionDeclarationPolicy.NoneConditionId" in migration
    assert "NativeStage01Keys.WorkflowVersion" in migration
    assert "NonCanonicalLegacyPayload" in storage
    assert "UnsupportedLegacyVersion" in storage
    compact_storage = "".join(storage.split())
    assert "ToJson(payload.Model,payload.SchemaVersion)" in compact_storage
    assert compact_storage.index(
        "ToJson(payload.Model,payload.SchemaVersion)"
    ) < compact_storage.index(
        "State=NativeStage01StorageState.MigratableLegacy"
    )
    for forbidden in (
        "NativeStage01MigrationService",
        "NativeStage01ConditionSchemaPolicy",
        "ApplyMissingDefaults",
    ):
        assert forbidden not in codec


def test_new_models_explicitly_start_with_an_unselected_none_declaration():
    rules = read(RULES)
    conditions = read(STAGE01 / "NativeStage01ConditionSchemaPolicy.cs")

    assert "NativeProjectConditionDeclarationPolicy.NoneConditionId" in rules
    assert "model.SetCondition" in rules
    assert "NativeProjectConditionDeclarationPolicy.NoneConditionId" in conditions
    assert "false" in conditions
    assert "defaultToNoneWhenEmpty" not in conditions


def test_read_path_uses_live_evidence_and_drift_without_silent_overwrite():
    read_service = read(STAGE01 / "NativeStage01RevitReadService.cs")
    authority = read(STAGE01 / "NativeStage01FieldAuthorityPolicy.cs")

    assert "NativeStage01LiveEvidence" in read_service
    assert "IReadOnlyList<NativeStage01Drift> Drifts" in read_service
    assert "RequiresMigrationConfirmation" in read_service
    assert "SourcePayloadVersion" in read_service
    assert "CaptureLiveEvidence" in read_service
    assert "ApplyInitialValues" in read_service
    assert "Compare" in read_service
    assert "NativeStage01MigrationService" in read_service
    assert "PopulateMissingDocumentValues" not in read_service
    assert "SetIfBlank" not in read_service
    assert "REVIT_LIVE" in authority
    assert "PAYLOAD_CONFIRMED" in authority
    assert "NumericEquivalent" in authority


def test_downstream_stages_wait_until_legacy_payload_is_explicitly_migrated():
    stage02 = read(STAGE02)
    stage03 = read(STAGE03)

    assert "NativeStage01StorageState.Current" in stage02
    assert "NativeStage01StorageState.Current" in stage03
    assert "等待 Stage01 数据迁移确认" in stage02
    assert "Stage01 数据迁移尚未确认" in stage03


def test_mcp_and_ui_expose_migration_live_evidence_and_drift_state():
    mcp = read(MCP)
    view = read(STAGE01 / "NativeStage01View.cs")

    for field in (
        '"payload_schema_version"',
        '"source_payload_version"',
        '"requires_migration_confirmation"',
        '"live_evidence"',
        '"drifts"',
    ):
        assert field in mcp
    assert "等待迁移确认" in view
    assert "现场值漂移" in view


def test_ui_only_renders_positive_read_evidence_for_current_or_migratable_storage():
    view_model = read(STAGE01 / "NativeStage01ViewModel.cs")
    view = read(STAGE01 / "NativeStage01View.cs")

    assert "NativeStage01StorageState StorageState" in view_model
    assert "StorageState = result?.StorageDecision?.State" in view_model
    assert "StorageState = NativeStage01StorageState.Current" in view_model
    assert "_viewModel.StorageState" in view
    assert "NativeStage01StorageState.Current" in view
    assert "NativeStage01StorageState.MigratableLegacy" in view
