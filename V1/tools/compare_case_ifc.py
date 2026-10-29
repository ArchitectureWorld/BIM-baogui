"""Read-only IFC case comparison. Expectations come from the reference IFC, never the export receipt."""
import argparse
from collections import Counter
import hashlib
import importlib.util
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / ".deps"))
import ifcopenshell
import ifcopenshell.validate

spec = importlib.util.spec_from_file_location("hifc_verify", ROOT / "tools/verify_ifc.py")
verify = importlib.util.module_from_spec(spec)
spec.loader.exec_module(verify)

def identity(owner):
    if owner.is_a("IfcBuilding"):
        return ("IfcBuilding",)  # Multiplicity is checked, not silently merged.
    if owner.is_a("IfcBuildingStorey"):
        return ("IfcBuildingStorey", owner.Name)
    tag = getattr(owner, "Tag", None)
    return ("Tag", str(tag)) if tag is not None else ("GlobalId", owner.GlobalId)

def floor(owner):
    if owner.is_a("IfcBuildingStorey"):
        return owner.Name
    relations = getattr(owner, "ContainedInStructure", ()) or getattr(owner, "Decomposes", ())
    for relation in relations:
        parent = getattr(relation, "RelatingStructure", None) or getattr(relation, "RelatingObject", None)
        if parent and parent.is_a("IfcBuildingStorey"):
            return parent.Name
    return None

def inventory(path):
    model = ifcopenshell.open(str(path))
    attached = verify.attached_property_sets(model)
    allowed = {p["property_set"] for p in json.loads((ROOT/"reference/gonggui/contract.json").read_text(encoding="utf-8"))["entries"] if not p["exporter_native_group"]}
    fields, owners, duplicates = {}, {}, []
    for owner in model.by_type("IfcProduct"):
        sets = [(b,p) for b,p in attached.get(owner.id(),[]) if p.Name in allowed]
        if not sets:
            continue
        key = identity(owner)
        if key in owners:
            duplicates.append(key)
        owners[key] = {"entity":owner.is_a(),"floor":floor(owner),"guid":owner.GlobalId,"step":owner.id()}
        for binding, pset in sets:
            for p in pset.HasProperties:
                k = key + (binding, pset.Name, p.Name)
                if k in fields:
                    duplicates.append(k)
                value = getattr(p,"NominalValue",None)
                fields[k] = {"type":value.is_a() if value else None,"value":value.wrappedValue if value else None}
    logger = ifcopenshell.validate.json_logger()
    ifcopenshell.validate.validate(model,logger)
    return {"fields":fields,"owners":owners,"duplicates":duplicates,"schema_errors":len(logger.statements),
            "entities":dict(Counter(o.is_a() for o in model.by_type("IfcProduct")))}

def compare(reference, output):
    paths = [Path(reference),Path(output)]
    hashes = [verify.hash_file(p) for p in paths]
    expected, actual = [inventory(p) for p in paths]
    differences=[]
    for key, value in expected["fields"].items():
        observed = actual["fields"].get(key)
        if observed is None:
            differences.append({"code":"MISSING_PROPERTY","key":key,"expected":value})
        elif value["type"] != observed["type"] or not (
            value["value"] is None and observed["value"] is None or
            value["value"] is not None and verify.same_value(value["value"],observed["value"],(value["type"] or "").removeprefix("Ifc"))):
            differences.append({"code":"VALUE_OR_UNIT_DIFFERENCE","key":key,"expected":value,"actual":observed})
    for key in actual["fields"].keys() - expected["fields"].keys():
        differences.append({"code":"ADDITIONAL_PROPERTY","key":key,"actual":actual["fields"][key]})
    for key, owner in expected["owners"].items():
        observed=actual["owners"].get(key)
        if observed and (owner["entity"],owner["floor"]) != (observed["entity"],observed["floor"]):
            differences.append({"code":"CARRIER_OR_STOREY_DIFFERENCE","key":key,"expected":owner,"actual":observed})
    unchanged = hashes == [verify.hash_file(p) for p in paths]
    return {"equivalent":not differences and not expected["duplicates"] and not actual["duplicates"] and not actual["schema_errors"] and unchanged,
            "reference":str(reference),"output":str(output),"read_only_hashes_unchanged":unchanged,
            "reference_sha256":hashes[0],"output_sha256":hashes[1],
            "reference_fields":len(expected["fields"]),"output_fields":len(actual["fields"]),
            "difference_counts":dict(Counter(d["code"] for d in differences)),
            "reference_duplicates":expected["duplicates"],"output_duplicates":actual["duplicates"],
            "reference_schema_errors":expected["schema_errors"],"output_schema_errors":actual["schema_errors"],
            "reference_entities":expected["entities"],"output_entities":actual["entities"],
            "differences":differences,"platform_verified":False,
            "note":"Differences identify case reproduction gaps; they are not new mandatory rules or proof of platform rejection. IFC values retain their declared types and raw units."}

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("reference",type=Path);parser.add_argument("output",type=Path)
    parser.add_argument("--report",type=Path,required=True)
    args=parser.parse_args()
    if args.report.resolve() in {args.reference.resolve(),args.output.resolve()}:
        parser.error("Report must not overwrite an IFC input.")
    report=compare(args.reference,args.output)
    args.report.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
    print(json.dumps({k:report[k] for k in ["equivalent","reference_fields","output_fields","difference_counts","output_schema_errors"]},ensure_ascii=False))
    return 0 if report["equivalent"] else 1

if __name__=="__main__":
    raise SystemExit(main())
