"""Read-only independent audit of the generated property-data acceptance IFC."""
import argparse
import hashlib
import json
import math
import sys
import traceback
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / ".deps"))
sys.path.insert(0, str(ROOT / "tools"))
from verify_ifc import verify_export
import ifcopenshell
import ifcopenshell.guid
import ifcopenshell.util.element
import ifcopenshell.validate


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("run_directory", type=Path)
    run = parser.parse_args().run_directory.resolve()
    checks = {}

    def load(name):
        return json.loads((run / name).read_text(encoding="utf-8-sig"))

    def check(name, ok):
        checks[name] = bool(ok)
        if not ok:
            raise AssertionError(name)

    def near(value, expected):
        return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isclose(value, expected, rel_tol=1e-9, abs_tol=1e-7)

    try:
        check("native_host_completed", load("result.json")["all_passed"])
        launch, loaded, targets = load("launch.json"), load("loaded.json"), load("export-targets.json")
        check("exact_candidate_loaded", loaded["sha256"].lower() == launch["plugin_sha256"].lower())
        check("exact_core_and_test_host_loaded", all(loaded[key].lower() == launch[key].lower() for key in ("core_sha256", "host_sha256")))
        check("frozen_initial_test_host_matches_launch", hashlib.sha256((run / "host" / "Hifc.Native.Tests.dll").read_bytes()).hexdigest() == launch["host_sha256"].lower())
        check("candidate_files_still_match_loaded_hashes", hashlib.sha256(Path(loaded["plugin"]).read_bytes()).hexdigest() == loaded["sha256"].lower() and hashlib.sha256(Path(loaded["core"]).read_bytes()).hexdigest() == loaded["core_sha256"].lower())
        if (run / "continuation.json").exists():
            continuation = load("continuation.json")
            check("continuation_test_host_hash_verified", hashlib.sha256(Path(continuation["assembly"]).read_bytes()).hexdigest() == continuation["sha256"] == continuation["expected_sha256"])
            check("continuation_kept_same_candidate_and_core", continuation["plugin_sha256"] == loaded["sha256"] and continuation["core_sha256"] == loaded["core_sha256"])
            check("continuation_stayed_in_generated_fixture", Path(continuation["document"]).resolve().is_relative_to(run / "models"))
        path = Path(targets["ifc"])
        before = hashlib.sha256(path.read_bytes()).hexdigest()
        receipt = json.loads(path.with_suffix(".receipt.json").read_text(encoding="utf-8-sig"))
        check("complete_receipt_matches_ifc", before == receipt["ifc_sha256"].lower())
        check("full_project_single_native_export", receipt["export_scope"] == "CURRENT_PROJECT" and receipt["selected_elements"] is None and receipt["doc_export_calls"] == 1 and receipt["ifc_postprocessed"] is False)
        check("model_rollback_and_sources_unchanged", receipt["model_transaction"] == "RolledBack" and len(receipt["source_snapshot_before"]) == 64 and receipt["source_snapshot_before"] == receipt["source_snapshot_after"])
        check("temporary_routing_restored", receipt["temporary_type_schedules_rolled_back"] and receipt["type_routing_callback_restored"] and receipt["original_export_server_restored"])
        model = ifcopenshell.open(str(path))
        check("native_ifc4", model.schema == "IFC4")
        logger = ifcopenshell.validate.json_logger()
        ifcopenshell.validate.validate(model, logger)
        check("ifc_schema_valid", not logger.statements)
        check("no_temporary_bridge_properties", not any((p.Name or "").startswith("__HIFCV1_") for p in model.by_type("IfcProperty")))
        mapping = verify_export(path, path.with_suffix(".receipt.json"))
        (run / "property-data-mapping-audit.json").write_text(json.dumps(mapping, ensure_ascii=False, indent=2, default=str), encoding="utf-8")
        # The small generated fixture deliberately leaves unrelated business
        # inputs empty. Retain that full failed-completeness audit verbatim.
        # For the native export gate, run the same strict verifier on every
        # materialized property, without suppressing any of its failure codes.
        # An entirely empty source set may correctly be absent from IFC.
        scoped_receipt = dict(receipt)
        scoped_receipt["materialization"] = [r for r in receipt["materialization"] if r["status"] == "MATERIALIZED"]
        scoped_receipt["audit_scope"] = "MATERIALIZED_ONLY; generated fixture intentionally has unfilled business inputs"
        scoped_receipt["original_receipt_sha256"] = hashlib.sha256(path.with_suffix(".receipt.json").read_bytes()).hexdigest()
        scoped_path = run / "materialized-only.audit-receipt.json"
        scoped_path.write_text(json.dumps(scoped_receipt, ensure_ascii=False, indent=2), encoding="utf-8")
        materialized_mapping = verify_export(path, scoped_path)
        (run / "property-data-materialized-mapping-audit.json").write_text(json.dumps(materialized_mapping, ensure_ascii=False, indent=2, default=str), encoding="utf-8")
        check("every_materialized_property_passes_strict_mapping_audit", materialized_mapping["all_passed"] and materialized_mapping["verified_properties"] == len(scoped_receipt["materialization"]))
        spatial = {str(i["element_id"]): i for i in receipt["spatial_export_identities"]}

        def owner(element_id):
            key = str(element_id)
            if key in spatial:
                identity = spatial[key]
                return model.by_guid(identity.get("native_global_id") or ifcopenshell.guid.compress(identity["export_id"]))
            matches = [e for e in model.by_type("IfcElement") if str(getattr(e, "Tag", "")) == key]
            check("unique_owner:" + key, len(matches) == 1)
            return matches[0]

        def property_value(element, set_name, name):
            sets = ifcopenshell.util.element.get_psets(element, should_inherit=False)
            check("instance_set:" + str(element.id()) + ":" + set_name, set_name in sets)
            pset = model.by_id(sets[set_name]["id"])
            props = [p for p in pset.HasProperties if p.Name == name]
            check("unique_property:" + str(element.id()) + ":" + name, len(props) == 1 and props[0].is_a("IfcPropertySingleValue") and props[0].NominalValue is not None)
            return props[0].NominalValue

        floor = owner(targets["first_level_id"])
        check("floor_identity_is_storey", floor.is_a("IfcBuildingStorey"))
        floor_area = property_value(floor, "Pset_建筑楼层信息属性集", "楼层面积")
        check("manual_floor_area_is_metric_123_45", near(floor_area.wrappedValue, targets["expected_floor_area"]))
        base = property_value(floor, "Pset_建筑楼层信息属性集", "建筑基底")
        check("explicit_first_floor_false_is_ifc_boolean", base.is_a("IfcBoolean") and base.wrappedValue is False)
        region = owner(targets["region_id"])
        area = property_value(region, "Pset_建筑区域信息属性集", "投影面积")
        check("region_area_uses_manual_floor_total", near(area.wrappedValue, targets["expected_region_area"]))
        buildings = model.by_type("IfcBuilding")
        check("one_fixture_building", len(buildings) == 1)
        height = property_value(buildings[0], "Pset_建筑技术信息属性集", "规划高度")
        check("building_height_is_three_meters", near(height.wrappedValue, targets["expected_building_height"]))
        density = property_value(owner(targets["alias_carrier_id"]), "Pset_总平构件信息属性集", "建筑密度")
        check("mixed_boolean_keeps_ifc_name_and_manual_false", density.is_a("IfcBoolean") and density.wrappedValue is False)
        if load("input.json").get("classification_inputs"):
            classifications = targets.get("classification_targets", [])
            check("five_custom_classification_targets_recorded", len(classifications) == 5)
            for row in classifications:
                value = property_value(owner(row["element_id"]), row["property_set"], row["property"])
                check("custom_classification:" + row["property"], value.is_a(row["ifc_type"]) and value.wrappedValue == row["value"])
        booleans = [r for r in receipt["materialization"] if r.get("export_type", r["raw_type"]) == "Boolean" and r["status"] == "MATERIALIZED"]
        check("boolean_observations_are_nonempty", bool(booleans))
        for row in booleans:
            value = property_value(owner(row["element_id"]), row["property_set"], row["property"])
            check("typed_boolean:" + str(row["element_id"]) + ":" + row["property"], value.is_a("IfcBoolean") and value.wrappedValue is (row["value"] == "1"))
        check("ifc_unchanged_during_audit", before == hashlib.sha256(path.read_bytes()).hexdigest())
        result = {"all_passed": True, "checks": checks, "ifc_sha256": before, "boolean_properties": len(booleans), "unfilled_fixture_properties": sum("SOURCE_VALUE_MISSING" in f.get("problems", []) for f in mapping["failures"]), "scope": "Generated fixture and native IFC only; no customer migration or platform acceptance."}
    except Exception:
        result = {"all_passed": False, "checks": checks, "error": traceback.format_exc()}
    (run / "property-data-ifc-result.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if result["all_passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
