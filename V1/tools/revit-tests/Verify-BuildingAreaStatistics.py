"""Verify native ownership from the disposable area-statistics Revit fixture."""
import json
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(root / ".deps"))
sys.path.insert(0, str(root / "tools"))
import ifcopenshell
from verify_ifc import attached_property_sets

folder = Path(sys.argv[1]).resolve()
expected = json.loads((folder / "fixture-result.json").read_text(encoding="utf-8-sig"))
model = ifcopenshell.open(str(folder / "open-owner-test.ifc"))
attached = attached_property_sets(model)
results = []
for name, owner_id in [("Pset_架空信息属性集", expected["open_owner"]), ("Pset_阳台信息属性集", expected["balcony_owner"])]:
    owners = [e for e in model.by_type("IfcProduct") if any(p.Name == name for _, p in attached.get(e.id(), []))]
    assert len(owners) == 1, (name, [(e.is_a(), getattr(e, "Tag", None)) for e in owners])
    owner = owners[0]
    assert owner.Tag == str(owner_id)
    pset = next(p for _, p in attached[owner.id()] if p.Name == name)
    properties = {p.Name: {"type": p.NominalValue.is_a(), "value": p.NominalValue.wrappedValue} for p in pset.HasProperties}
    for field in ("投影面积", "计算系数", "计容系数"):
        assert properties[field]["type"] == "IfcReal", (field, properties)
    if name == "Pset_架空信息属性集":
        assert len(properties) == 5 and properties["投影面积"]["value"] == 100
        assert properties["公建大类"]["type"] == "IfcLabel" and properties["公建小类"]["type"] == "IfcLabel"
    results.append({"set": name, "owner": owner_id, "properties": properties})
unrelated = next(e for e in model.by_type("IfcSlab") if e.Tag == str(expected["unrelated"]))
assert not any(p.Name in ("Pset_架空信息属性集", "Pset_阳台信息属性集") for _, p in attached.get(unrelated.id(), []))
result = {"success": True, "owners": results, "unclassified_geometry_retained": True, "unclassified_psets_absent": True}
(folder.parent / "ifc-owner-proof.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps({"success": True, "owners": [r["owner"] for r in results], "unclassified_geometry_retained": True}))
