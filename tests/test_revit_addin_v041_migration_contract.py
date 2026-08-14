from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
STAGE01 = ROOT / "src" / "BIMBaoGui.RevitAddin" / "Stage01"
RULES = ROOT / "src" / "BIMBaoGui.RevitAddin" / "Rules"
WORKFLOW = ROOT / ".github" / "workflows" / "build-revit-mcp.yml"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def test_payload_protocol_advances_to_091_without_mutating_decode():
    canonicalizer = read(STAGE01 / "NativeStage01Canonicalizer.cs")
    codec = read(STAGE01 / "NativeStage01PayloadCodec.cs")

    assert 'PayloadSchemaVersion = "0.9.1"' in canonicalizer
    assert "ToJson(NativeStage01Model model, string schemaVersion)" in canonicalizer
    for forbidden in (
        "NativeStage01MigrationService",
        "NativeStage01ConditionSchemaPolicy",
        "SetCondition(",
        "SetValue(",
        "model.Organizations.Add(",
    ):
        assert forbidden not in codec


def test_migration_is_explicitly_limited_to_090_to_091():
    migration = read(STAGE01 / "NativeStage01MigrationService.cs")

    assert 'SupportedSourceVersion = "0.9.0"' in migration
    assert "NativeStage01Canonicalizer.PayloadSchemaVersion" in migration
    assert "NativeStage01ConditionSchemaPolicy.Reconcile" in migration
    assert "NativeProjectConditionDeclarationPolicy.NoneConditionId" in migration
    assert "SetCondition(" in migration
    assert "false" in migration
    assert "SourcePayloadHash" in migration
    assert "TargetPayloadHash" in migration
    assert "candidate.Organizations.Add" not in migration


def test_storage_validates_legacy_canonical_bytes_before_classifying_migration():
    storage = read(STAGE01 / "NativeStage01StoragePolicy.cs")

    assert "UnsupportedLegacyVersion" in storage
    assert "NonCanonicalLegacyPayload" in storage
    assert "NativeStage01MigrationService.SupportedSourceVersion" in storage
    assert "NativeStage01Canonicalizer.ToJson(" in storage
    assert "MigratableLegacy" in storage


def test_read_builds_migration_candidate_without_writing_storage():
    read_service = read(STAGE01 / "NativeStage01RevitReadService.cs")

    assert "NativeStage01MigrationService.Migrate" in read_service
    assert "RequiresMigrationConfirmation" in read_service
    assert "SourcePayloadVersion" in read_service
    assert "等待用户确认迁移" in read_service
    assert "NativeStage01Storage.Write" not in read_service


def test_new_models_materialize_explicit_false_none_declaration_key():
    catalog = read(RULES / "NativeRuleCatalog.cs")

    assert "NativeProjectConditionDeclarationPolicy.NoneConditionId" in catalog
    assert "model.SetCondition" in catalog
    assert "false" in catalog


def test_revit_ci_runs_migration_contract_and_domain_tests():
    workflow = read(WORKFLOW)
    assert "tests/test_revit_addin_v041_migration_contract.py" in workflow
    assert "Run native domain tests" in workflow


def test_current_091_storage_requires_complete_condition_schema():
    storage = read(STAGE01 / "NativeStage01StoragePolicy.cs")

    assert "ConditionSchemaMismatch" in storage
    assert "NativeStage01ConditionSchemaPolicy.IsComplete" in storage
    assert storage.index("NativeStage01ConditionSchemaPolicy.IsComplete") < storage.index(
        "State = NativeStage01StorageState.Current"
    )


def test_corrupt_or_future_reads_do_not_fall_back_to_business_defaults():
    read_service = read(STAGE01 / "NativeStage01RevitReadService.cs")

    assert "CreateBlockedModel" in read_service
    blocked_section = read_service[
        read_service.index("case NativeStage01StorageState.Corrupt") :
        read_service.index("NativeStage01ValidationResult validation")
    ]
    assert "catalog.CreateDefaultStage01Model()" not in blocked_section


def test_mcp_read_never_canonicalizes_blocked_or_noncurrent_models_blindly():
    adapter = read(
        ROOT
        / "src"
        / "BIMBaoGui.RevitAddin"
        / "McpBridge"
        / "McpStage01Adapter.cs"
    )

    assert "TryProjectPayload" in adapter
    helper = adapter[adapter.index("private static string TryProjectPayload") :]
    assert "result.StorageDecision == null" in helper
    assert "NativeStage01StorageState.Corrupt" in helper
    assert "NativeStage01StorageState.UnsupportedFuture" in helper
    read_method = adapter[
        adapter.index("internal async Task<string> ReadAsync") :
        adapter.index("internal string Validate")
    ]
    assert "NativeStage01Canonicalizer.ToJson(result.Model)" not in read_method
    assert "NativeStage01Keys.WorkflowVersion" in adapter
    assert "NativeStage01Canonicalizer.PayloadSchemaVersion" in adapter


def test_manual_workspace_preserves_explicitly_empty_legacy_organization_array():
    view_model = read(STAGE01 / "NativeStage01ViewModel.cs")
    view = read(STAGE01 / "NativeStage01View.cs")

    load_core = view_model[
        view_model.index("private void LoadModelCore") :
        view_model.index("internal void AddOrganization")
    ]
    assert "Organizations.Add" not in load_core
    assert "if (_model.Organizations.Count == 0) return;" in view_model
    assert "OrganizationDisplayIndex" in view_model
    assert "_viewModel.OrganizationDisplayIndex" in view
