from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
WORKFLOW = ROOT / "src" / "BIMBaoGui.RevitAddin" / "Workflow"


def test_workflow_result_storage_has_fixed_schema_and_independent_stage_fields():
    source = (WORKFLOW / "NativeWorkflowResultStorage.cs").read_text(encoding="utf-8")
    assert "9f1de04a-406b-4c15-b693-1f3b7f1ea043" in source
    assert '"HBR_NATIVE_WORKFLOW_RESULTS_V1"' in source
    assert '"HBR Native Workflow Results"' in source
    assert '"Stage01Json"' in source
    assert '"Stage02AJson"' in source
    assert '"Stage02BJson"' in source
    assert "static Document" not in source


def test_stage02_uses_the_shared_workflow_document_fingerprint():
    source = (
        ROOT
        / "src"
        / "BIMBaoGui.RevitAddin"
        / "Stage02"
        / "NativeStage02RevitService.cs"
    ).read_text(encoding="utf-8")
    assert "NativeWorkflowIdentityFactory.ComputeDocumentFingerprint" in source
    assert "private static string ComputeDocumentFingerprint" not in source
