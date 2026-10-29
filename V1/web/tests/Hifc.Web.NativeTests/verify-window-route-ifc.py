"""Independently audit NativeWindowRouteFixture outputs using IfcOpenShell.

Usage: python verify-window-route-ifc.py <run>/window-route-results.json
No model or IFC is modified. The JSON audit is a separate evidence artifact.
"""
import hashlib
import json
import pathlib
import sys

import ifcopenshell
import ifcopenshell.util.element


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def direct_sets(element, name):
    if element.is_a("IfcTypeObject"):
        return [p for p in (element.HasPropertySets or ()) if p.is_a("IfcPropertySet") and p.Name == name]
    return [rel.RelatingPropertyDefinition for rel in (element.IsDefinedBy or ())
            if rel.is_a("IfcRelDefinesByProperties") and rel.RelatingPropertyDefinition.is_a("IfcPropertySet")
            and rel.RelatingPropertyDefinition.Name == name]


def values(pset):
    return {p.Name: p.NominalValue.wrappedValue for p in pset.HasProperties if p.is_a("IfcPropertySingleValue")}


def audit(manifest):
    require(manifest["nativeExportCompleted"], "Native export did not complete")
    results = []
    for mode in ("instance", "typed"):
        spec = manifest[mode]
        require(spec["SourceRestored"], "Native model restoration did not pass")
        path = pathlib.Path(spec["path"])
        require(hashlib.sha256(path.read_bytes()).hexdigest() == spec["sha256"], "IFC hash differs from the native receipt")
        model = ifcopenshell.open(str(path))
        all_windows = model.by_type("IfcWindow")
        require(len(all_windows) == 3, "Expected exactly three complete native windows")
        windows = {str(w.Tag): w for w in all_windows}
        require(len(windows) == 3, "Native window Tag identities are duplicated")
        selected = [windows[str(manifest[key]["elementId"])] for key in ("first", "second")]
        peer = windows[str(manifest["peer"]["elementId"])]
        owners = selected if mode == "instance" else [ifcopenshell.util.element.get_type(w) for w in selected]
        require(all(owners), "One exported window has no intended type")
        require(ifcopenshell.util.element.get_type(selected[0]) == ifcopenshell.util.element.get_type(peer), "The shared-type peer fixture was not preserved")
        require(ifcopenshell.util.element.get_type(selected[0]) != ifcopenshell.util.element.get_type(selected[1]), "The two native window types collapsed")
        for index, owner in enumerate(owners):
            sets = direct_sets(owner, spec["expectedSet"])
            require(len(sets) == 1, f"{mode} owner {index} has {len(sets)} copies of the routed Pset")
            actual = values(sets[0])
            typed_properties = {p.Name: p.NominalValue.is_a() for p in sets[0].HasProperties if p.is_a("IfcPropertySingleValue")}
            require(typed_properties.get("Marker") == "IfcText" and typed_properties.get("ProbeWidth") == "IfcReal", f"Wrong IFC property types: {typed_properties}")
            require(actual.get("Marker") == spec["expectedMarkers"][index], f"{mode} marker leaked from the other owner: {actual}")
            require(abs(actual.get("ProbeWidth", -1) - (1.2 if index == 0 else 1.8)) < 1e-9, f"{mode} width leaked or was scaled: {actual}")
        if mode == "instance":
            require(not direct_sets(peer, spec["expectedSet"]), "Unclassified shared-type peer acquired the business Pset")
            require(all(not direct_sets(t, spec["expectedSet"]) for t in model.by_type("IfcWindowType")), "Instance business Pset leaked onto a window type")
        else:
            require(all(not direct_sets(w, spec["expectedSet"]) for w in selected + [peer]), "Type Pset was emitted again on instances")
            require(all(not direct_sets(t, spec["expectedSet"]) for t in model.by_type("IfcWindowType") if t not in owners), "Type Pset leaked onto an unselected or unused window type")
        results.append({"mode": mode, "ifc": str(path), "passed": True})
    return {"passed": True, "checks": results, "audit": "independent IfcOpenShell relationships and property ownership"}


if __name__ == "__main__":
    source = pathlib.Path(sys.argv[1]).resolve()
    result = audit(json.loads(source.read_text(encoding="utf-8-sig")))
    destination = source.with_name("window-route-ifc-audit.json")
    destination.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False))
