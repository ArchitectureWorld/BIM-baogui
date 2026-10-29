"""Read-only audit of both native IFC files from PlatformAcceptanceHost."""
import argparse
import hashlib
import json
import sys
import traceback
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / ".deps"))

import ifcopenshell
import ifcopenshell.guid
import ifcopenshell.util.element


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("run_directory", type=Path)
    args = parser.parse_args()
    run = args.run_directory.resolve()
    checks = {}

    def check(name, value):
        checks[name] = bool(value)
        if not value:
            raise AssertionError(name)

    def load(name):
        return json.loads((run / name).read_text(encoding="utf-8-sig"))

    def psets(element):
        return ifcopenshell.util.element.get_psets(element, should_inherit=False)

    def numeric(value, expected):
        try:
            return abs(float(value) - expected) < 1e-6
        except (TypeError, ValueError):
            return False

    try:
        fixture = load("test-inputs.json")
        check("host_assertions_passed", load("platform-host-result.json")["all_passed"])
        identities = {item["role"]: item for item in fixture["elements"]}
        for basename in ("platform", "platform-reopened"):
            receipt = load(basename + ".receipt.json")
            path = run / (basename + ".ifc")
            check(basename + ":receipt_hash", hashlib.sha256(path.read_bytes()).hexdigest() == receipt["ifc_sha256"])
            model = ifcopenshell.open(str(path))
            check(basename + ":native_ifc4", model.schema == "IFC4" and receipt["doc_export_calls"] == 1 and not receipt["ifc_postprocessed"])

            def find(role, entity):
                source = identities[role]
                if entity in ("IfcBuildingStorey", "IfcSpace"):
                    guid = source["native_global_id"] or ifcopenshell.guid.compress(source["export_id"])
                    result = model.by_guid(guid)
                    check(basename + ":identity:" + role, result is not None and result.is_a(entity))
                    return result
                candidates = [e for e in model.by_type(entity) if str(getattr(e, "Tag", "")) == str(source["id"])]
                check(basename + ":identity:" + role, len(candidates) == 1)
                return candidates[0]

            window = find("window", "IfcWindow")
            window_sets = psets(window)
            check(basename + ":window_instance_pset", "Pset_窗信息属性集" in window_sets)
            for name, value in fixture["expected_window_m"].items():
                check(basename + ":window_metric:" + name, numeric(window_sets["Pset_窗信息属性集"].get(name), value))
            peer = find("unclassified_window", "IfcWindow")
            check(basename + ":shared_type_does_not_classify_peer", "Pset_窗信息属性集" not in ifcopenshell.util.element.get_psets(peer))
            region = find("region", "IfcSlab")
            region_sets = psets(region)
            check(basename + ":region_has_only_its_business_pset", "Pset_建筑区域信息属性集" in region_sets and "Pset_总平构件信息属性集" not in ifcopenshell.util.element.get_psets(region))
            check(basename + ":region_function_values", region_sets["Pset_建筑区域信息属性集"].get("主功能类别") == "学校" and region_sets["Pset_建筑区域信息属性集"].get("子功能类别") == "教室")
            canopy = find("canopy", "IfcSlab")
            canopy_sets = psets(canopy)
            check(basename + ":canopy_exact_platform_name", "Pset_雨篷信息属性集" in canopy_sets and "Pset_雨蓬信息属性集" not in canopy_sets)
            check(basename + ":canopy_no_stale_general_site_pset", "Pset_总平构件信息属性集" not in ifcopenshell.util.element.get_psets(canopy))
            check(basename + ":canopy_zero_preserved", numeric(canopy_sets["Pset_雨篷信息属性集"].get("计容系数"), 0))
            site = find("general_site", "IfcSlab")
            check(basename + ":valid_general_site_source_exported", psets(site).get("Pset_总平构件信息属性集", {}).get("构件类型") == "风雨连廊")
            parking = find("indoor_parking", "IfcSpace")
            parking_sets = psets(parking)
            check(basename + ":indoor_parking_separate_pset", "Pset_室内停车场信息属性集" in parking_sets and "Pset_停车场信息属性集" not in parking_sets)
            check(basename + ":indoor_parking_values", numeric(parking_sets["Pset_室内停车场信息属性集"].get("投影面积"), 40) and parking_sets["Pset_室内停车场信息属性集"].get("公建小类") == "机动车库")
            green = find("incomplete_green", "IfcSlab")
            green_values = psets(green).get("Pset_绿地信息属性集", {})
            check(basename + ":green_defaults_not_exported", green_values.get("绿地类型") in (None, "") and green_values.get("折算系数") in (None, ""))
            for role in ("first_level", "second_level"):
                check(basename + ":actual_level_business_pset:" + role, "Pset_建筑楼层信息属性集" in psets(find(role, "IfcBuildingStorey")))
            auxiliary = find("auxiliary_level", "IfcBuildingStorey")
            check(basename + ":auxiliary_native_storey_without_business_pset", "Pset_建筑楼层信息属性集" not in psets(auxiliary))
            auxiliary_floor = find("unclassified_aux_floor", "IfcSlab")
            check(basename + ":auxiliary_geometry_keeps_native_storey", ifcopenshell.util.element.get_container(auxiliary_floor) == auxiliary)
            for role, element in (("window", window), ("region", region), ("canopy", canopy), ("general_site", site), ("indoor_parking", parking)):
                check(basename + ":native_body_present:" + role, getattr(element, "Representation", None) is not None)
        result = {"all_passed": True, "checks": checks, "scope": "Native IFC instance properties, identities and spatial hierarchy; no server calculation acceptance."}
    except Exception:
        result = {"all_passed": False, "checks": checks, "error": traceback.format_exc()}
    (run / "platform-ifc-result.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if result["all_passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
