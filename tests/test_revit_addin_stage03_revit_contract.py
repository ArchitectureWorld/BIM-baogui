from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "src" / "BIMBaoGui.RevitAddin"
STAGE03 = PROJECT / "Stage03"
HIFC = ROOT / "src" / "BIMBaoGui.HifcCore"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def test_stage03_live_scan_reuses_stage01_stage02_and_fixed_rule_database():
    source = read(STAGE03 / "NativeStage03Scanner.cs")
    assert "NativeStage01RevitReadService.Read" in source
    assert "NativeProjectConditionDeclarationPolicy.Evaluate" in source
    assert "NativeStage02RevitService.CreatePreview" in source
    assert "NativeStage02ScopeMode.FullModel" in source
    assert "NativeStage02RuleCatalog.Current" in source
    assert "ExportUtils.GetExportId" in source
    assert "IfcGlobalId.Encode" in source
    assert "StageOwnership.Contains" in source
    assert "NativeStage03Canonicalizer.ComputeHash" in source


def test_stage03_raw_export_is_ifc4_and_rolls_back_revit_transaction():
    source = read(STAGE03 / "NativeStage03RawIfcExporter.cs")
    assert "new IFCExportOptions" in source
    assert "IFCVersion.IFC4" in source
    assert "document.Export" in source
    assert "transaction.RollBack()" in source
    assert 'TransactionStrategy = "ROLLBACK_AFTER_EXPORT"' in source
    assert "HifcCoreService.ComputeSha256" in source
    assert "File.Exists(path)" in source
    assert "info.Length <= 0" in source


def test_stage03_translation_preserves_raw_and_exactly_reopens_candidate():
    source = read(HIFC / "HifcCoreService.cs")
    assert "StrictUtf8" in source
    assert "IfcStepDocument.Parse" in source
    assert "HbrIfcEnricher" in source
    assert "HbrIfcFieldInspector" in source
    assert "ValidateFile(candidatePath" in source
    assert "EnsureRawUnchanged" in source
    assert "File.Move(candidatePath, finalPath)" in source
    assert "IFCFLUX_MANUAL_PENDING" in source.upper()


def test_stage03_workflow_writes_all_manual_ifcflux_artifacts():
    workflow = read(STAGE03 / "NativeStage03WorkflowService.cs")
    reports = read(STAGE03 / "NativeStage03ReportWriter.cs")
    assert "NativeStage03Scanner.Scan" in workflow
    assert "NativeStage03RawIfcExporter" in workflow
    assert "HifcCoreService.Translate" in workflow
    assert "WriteSuccess" in workflow
    assert "WriteFailure" in workflow
    assert "IFCFlux" in reports
    assert "fields.json" in reports
    assert "validation.json" in reports
    assert "IFCFLUX_MANUAL_PENDING" in reports.upper()
    assert "SHA-256" in reports


def test_strict_and_forced_modes_are_explicit_and_never_claim_ifcflux_pass():
    models = read(STAGE03 / "NativeStage03Models.cs")
    reports = read(STAGE03 / "NativeStage03ReportWriter.cs")
    assert "Strict" in models
    assert "ForcedTest" in models
    assert "FORCE_REASON_REQUIRED" in models
    assert "FORCED_TEST_HIFC.ifc" in models
    assert "INTERNAL_VALIDATED" in reports
    assert "IFCFLUX_MANUAL_PENDING" in reports
    assert "IFCFLUX_PASS" not in reports
