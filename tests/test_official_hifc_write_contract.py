from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "src" / "BIMBaoGui.Stage01" / "BIMBaoGui.Stage01.csproj"
SERVICE = ROOT / "src" / "BIMBaoGui.Stage01" / "Revit" / "OfficialHifcWriteService.cs"
COMPONENT = ROOT / "src" / "BIMBaoGui.Stage01" / "Stage03OfficialHifcWriteComponent.cs"


def test_mapping_resources_are_embedded():
    text = PROJECT.read_text(encoding="utf-8")
    assert "GH_HIFC_ParameterBindings.json" in text
    assert "GH_HIFC_SharedParameters.txt" in text
    assert "<Version>0.6.0</Version>" in text


def test_writer_uses_atomic_revit_transaction_and_readback():
    text = SERVICE.read_text(encoding="utf-8")
    assert "TransactionGroup" in text
    assert "document.Regenerate()" in text
    assert "VerifyReadback" in text
    assert "get_Parameter(mapping.ParameterGuid)" in text
    assert "group.RollBack()" in text
    assert "UnitUtils.ConvertToInternalUnits" in text


def test_component_keeps_transaction_logic_out_of_solve_instance():
    text = COMPONENT.read_text(encoding="utf-8")
    solve = text.split("protected override void SolveInstance", 1)[1]
    assert "OfficialHifcWriteService.Enqueue" in solve
    assert "new Transaction(" not in solve
    assert "Parameter.Set" not in solve
