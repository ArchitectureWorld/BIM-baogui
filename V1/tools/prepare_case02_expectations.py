"""Prepare explicit case-derived window selections before running the Revit test host."""
import argparse
import hashlib
import json
from pathlib import Path
import sys
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/".deps"))
import ifcopenshell

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("reference_directory",type=Path)
    parser.add_argument("baseline_directory",type=Path)
    parser.add_argument("output",type=Path)
    args=parser.parse_args()
    cases={}
    for path in args.reference_directory.glob("*.ifc"):
        model=ifcopenshell.open(str(path))
        sources=json.loads((args.baseline_directory/(path.stem+"-sources.json")).read_text(encoding="utf-8-sig"))
        by_id={str(e["id"]):e for e in sources["elements"]}
        windows=[]
        for relation in model.by_type("IfcRelDefinesByType"):
            if any(p.Name=="Pset_窗信息属性集" for p in relation.RelatingType.HasPropertySets or ()):
                for owner in relation.RelatedObjects:
                    source=by_id[owner.Tag]
                    if not owner.is_a("IfcWindow") or source["ifc_entity_candidate"]!="IfcWindow":
                        raise ValueError("Reference and RVT carrier differ")
                    windows.append({"id":source["id"],"unique_id":source["unique_id"],"name":source["name"]})
        cases[path.stem]={"reference_sha256":hashlib.sha256(path.read_bytes()).hexdigest(),"windows":windows}
    args.output.write_text(json.dumps(cases,ensure_ascii=False,indent=2),encoding="utf-8")
    print(json.dumps({n:len(c["windows"]) for n,c in cases.items()},ensure_ascii=False))

if __name__=="__main__":
    main()
