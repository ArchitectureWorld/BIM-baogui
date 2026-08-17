import hashlib
import json
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "src" / "BIMBaoGui.RevitAddin"
ACCEPTANCE = PROJECT / "Acceptance"
TOOLS = ROOT / "tools" / "acceptance"

PROPERTY_IDS = (
    "ca21e324-046b-5bfd-84c8-0d3470082303",
    "93e51676-237e-56a8-8f28-2da845422e2e",
    "201a00ac-3672-5ded-83d2-ed96f81bfabf",
    "f630ad47-b006-5127-badd-b1660cf996c3",
    "c62cfd5f-2a50-5230-9c5d-4037c39061bf",
    "84df74c2-a7e5-5a98-a5e0-4458e49a3973",
)


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def test_normal_product_has_no_probe_button_or_mcp_tool_without_context():
    app = read(PROJECT / "App.cs")
    assert "CanRegisterAtStartup" in app
    assert "BIMBAOGUI_ACCEPTANCE_PROBE_CONTEXT" in read(
        ACCEPTANCE / "NativeOfficialCarrierProbePolicy.cs"
    )
    probe_button = app.index("验收载体探针")
    guard = app.rfind("CanRegisterAtStartup", 0, probe_button)
    assert guard >= 0
    mcp = "\n".join(read(path) for path in (PROJECT / "McpBridge").glob("*.cs"))
    assert "OfficialCarrierProbe" not in mcp
    assert "验收载体探针" not in read(PROJECT / "WorkspaceControl.cs")


def test_probe_service_authorizes_before_transaction_and_saves_only_copy():
    source = read(ACCEPTANCE / "NativeOfficialCarrierProbeService.cs")
    authorize = source.index("NativeOfficialCarrierProbePolicy.Authorize")
    transaction = source.index("new TransactionGroup")
    assert authorize < transaction
    assert "document.Save()" in source
    assert "SourceGoldenRvtPath" in source
    assert "SaveAs" not in source
    assert "OFFICIAL_SOURCE_NAME_AMBIGUOUS" in source
    assert "OFFICIAL_SOURCE_NAME_CONTRACT_MISMATCH" in source
    assert "group.RollBack()" in source


def test_probe_requires_committed_inner_transaction_and_assimilated_group():
    source = read(ACCEPTANCE / "NativeOfficialCarrierProbeService.cs")
    assert "NativeTransactionCommitPolicy.RequireCommitted(" in source
    assert "transaction.Commit().ToString()," in source
    assert "group.Assimilate().ToString()," in source
    assert source.index("transaction.Commit().ToString(),") < source.index(
        "group.Assimilate().ToString(),"
    )
    assert source.index("group.Assimilate().ToString(),") < source.index(
        "document.Save()"
    )
    assert "if (group.GetStatus() == TransactionStatus.Started)" in source


def test_probe_resolves_an_immutable_live_candidate_plan_before_any_transaction():
    source = read(ACCEPTANCE / "NativeOfficialCarrierProbeService.cs")
    execute = source[source.index("internal static string Execute") : source.index(
        "private static NativeOfficialCarrierProbeResolvedPlan ResolvePreflightPlan"
    )]
    preflight = execute.index("ResolvePreflightPlan")
    group = execute.index("new TransactionGroup")
    assert preflight < group
    assert execute.index("ValidateExistingSourceParameters") < preflight
    transaction_scope = execute[group:]
    assert "ResolveCandidate(document" not in transaction_scope
    assert "CreateSourceProperty(" not in transaction_scope
    assert "ReadExistingSourceParameters" not in transaction_scope
    assert execute.index("group.Assimilate().ToString(),") < execute.index(
        "CreateSeedItem"
    )
    models = read(ACCEPTANCE / "NativeOfficialCarrierProbeModels.cs")
    assert "NativeOfficialCarrierProbeResolvedPlan" in models
    assert "ReadOnlyCollection<NativeOfficialCarrierProbeResolved" in models


def test_context_script_creates_a_new_copy_and_never_changes_source(tmp_path: Path):
    source = tmp_path / "golden.rvt"
    source.write_bytes(b"golden-rvt-content")
    acceptance_root = tmp_path / "acceptance"
    candidates_path = tmp_path / "candidates.json"
    candidates = []
    for index, property_id in enumerate(PROPERTY_IDS):
        candidates.append(
            {
                "propertyId": property_id,
                "uniqueId": "PROJECT_INFORMATION" if index == 0 else f"uid-{index}",
                "categoryBuiltInId": (
                    "OST_ProjectInformation" if index == 0 else "OST_Areas"
                ),
                "elementClass": (
                    "Autodesk.Revit.DB.ProjectInfo"
                    if index == 0
                    else "Autodesk.Revit.DB.SpatialElement"
                ),
            }
        )
    candidates_path.write_text(json.dumps(candidates), encoding="utf-8")
    before = sha256(source)
    result = subprocess.run(
        [
            "powershell.exe",
            "-NoLogo",
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            str(TOOLS / "New-NativeOfficialCarrierProbeContext.ps1"),
            "-SourceGoldenRvtPath",
            str(source),
            "-AcceptanceRoot",
            str(acceptance_root),
            "-CommitSha",
            "a" * 40,
            "-RulePackageSha256",
            "b" * 64,
            "-CandidatesJsonPath",
            str(candidates_path),
        ],
        cwd=ROOT,
        text=True,
        capture_output=True,
        check=False,
    )
    assert result.returncode == 0, result.stderr
    created = json.loads(result.stdout)
    context_path = Path(created["contextPath"])
    probe_path = Path(created["probeCopyPath"])
    assert context_path.is_file()
    assert probe_path.is_file()
    assert "__HIFC_CARRIER_PROBE__" in probe_path.name
    assert probe_path.is_relative_to(acceptance_root)
    assert context_path.is_relative_to(acceptance_root)
    assert sha256(source) == before
    assert sha256(probe_path) == before
    context = json.loads(context_path.read_text(encoding="utf-8-sig"))
    assert context["sourceGoldenRvtSha256"] == before
    assert context["probeCopyPreSeedSha256"] == before
    assert len(context["metrics"]) == 6
    assert [item["propertyId"] for item in context["metrics"]] == list(PROPERTY_IDS)


def test_final_readback_script_has_no_property_id_injection_surface():
    source = read(TOOLS / "Resolve-NativeOfficialPropertyReadback.ps1")
    parameter_block = source[source.index("param(") : source.index(")", source.index("param("))]
    assert "PropertyId" not in parameter_block
    assert "official_acceptance_manifest" in source
    assert "official_acceptance_revit_readbacks" in source
    assert "ResolveFinalReadback" in source
