from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
STAGE02 = ROOT / "src" / "BIMBaoGui.RevitAddin" / "Stage02"
WORKFLOW = ROOT / ".github" / "workflows" / "build-revit-addin.yml"


def read(name: str) -> str:
    return (STAGE02 / name).read_text(encoding="utf-8")


def test_inventory_is_read_only_full_model_scan_and_freezes_required_identity():
    source = read("NativeStage02InventoryService.cs")
    assert "new FilteredElementCollector(document)" in source
    assert "WhereElementIsNotElementType" in source
    assert "ImportInstance" in source
    assert "RevitLinkInstance" in source
    assert "ViewSpecific" in source
    assert "DocumentFingerprint" in source
    assert "UniqueId" in source
    assert "FamilyName" in source
    assert "TypeName" in source
    assert "new Transaction(" not in source
    assert "document.Delete" not in source


def test_role_matcher_uses_exact_deterministic_normalization_without_fuzzy_guessing():
    source = read("NativeStage02RoleMatcher.cs")
    assert "NormalizationForm.FormKC" in source
    assert "CollapseWhitespace" in source
    assert "Ordinal" in source
    for forbidden in (
        "Contains(",
        "StartsWith(",
        "EndsWith(",
        "Levenshtein",
        "EditDistance",
        "Regex",
        "LLM",
    ):
        assert forbidden not in source


def test_native_ci_runs_stage02_inventory_contract():
    workflow = (ROOT / ".github" / "workflows" / "build-revit-addin.yml").read_text(
        encoding="utf-8"
    )
    assert "Verify native Stage02 inventory contract" in workflow
    assert (
        "python -m pytest tests/test_revit_addin_stage02_inventory_contract.py -q"
        in workflow
    )
