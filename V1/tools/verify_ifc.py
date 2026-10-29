"""Read-only reference mapping checks for native export receipts."""
import argparse
import hashlib
import json
import math
from pathlib import Path
import sys
import uuid

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / ".deps"))
import ifcopenshell
import ifcopenshell.guid
import ifcopenshell.validate

IFC_TYPES = {"Real": "IfcReal", "Integer": "IfcInteger", "Boolean": "IfcBoolean", "Label": "IfcLabel", "Text": "IfcText", "Duration": "IfcDuration"}


def hash_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def same_value(expected, actual, raw_type: str) -> bool:
    if raw_type == "Boolean":
        literal = str(expected).lower()
        return literal in {"true", "1", "false", "0"} and isinstance(actual, bool) and actual == (literal in {"true", "1"})
    if raw_type in {"Real", "Integer"}:
        try:
            return math.isclose(float(actual), float(expected), rel_tol=1e-10, abs_tol=1e-9)
        except (TypeError, ValueError):
            return False
    return str(actual) == str(expected)

def attached_property_sets(model):
    """Return explicit binding paths; never infer a type set from its name."""
    result = {}
    for relation in model.by_type("IfcRelDefinesByProperties"):
        definition = relation.RelatingPropertyDefinition
        if definition.is_a("IfcPropertySet"):
            for owner in relation.RelatedObjects:
                result.setdefault(owner.id(), []).append(("实例", definition))
    for relation in model.by_type("IfcRelDefinesByType"):
        for definition in relation.RelatingType.HasPropertySets or ():
            if definition.is_a("IfcPropertySet"):
                for owner in relation.RelatedObjects:
                    result.setdefault(owner.id(), []).append(("类型", definition))
    return result


def verify_export(ifc_path: Path, receipt_path: Path) -> dict:
    before = hash_file(ifc_path)
    receipt = json.loads(receipt_path.read_text(encoding="utf-8-sig"))
    contract = json.loads((ROOT / "reference/gonggui/contract.json").read_text(encoding="utf-8"))
    reference_psets = {p["property_set"] for p in contract["entries"] if not p["exporter_native_group"]}
    failures = []
    checks = []
    if receipt.get("ifc_sha256") != before:
        failures.append({"code": "RECEIPT_HASH_MISMATCH"})
    if receipt.get("doc_export_calls") != 1 or receipt.get("ifc_postprocessed") is not False:
        failures.append({"code": "EXPORT_STRATEGY_MISMATCH"})
    snapshot = receipt.get("source_snapshot_before")
    if not isinstance(snapshot, str) or len(snapshot) != 64 or receipt.get("model_transaction") != "RolledBack" or snapshot != receipt.get("source_snapshot_after"):
        failures.append({"code": "MODEL_ROLLBACK_NOT_CONFIRMED"})
    model = ifcopenshell.open(str(ifc_path))
    if model.schema != "IFC4":
        failures.append({"code": "WRONG_IFC_SCHEMA", "schema": model.schema})
    logger = ifcopenshell.validate.json_logger()
    ifcopenshell.validate.validate(model, logger)
    schema_errors = logger.statements
    if schema_errors:
        failures.append({"code": "IFC_SCHEMA_ERRORS", "count": len(schema_errors)})
    leaked_bridges = [p for p in model.by_type("IfcProperty") if p.Name and p.Name.startswith("__HIFCV1_")]
    if leaked_bridges:
        failures.append({"code": "TEMPORARY_BRIDGE_LEAK", "count": len(leaked_bridges)})
    owners = {}
    owners_by_guid = {}
    spatial_owner_checks = []
    attached = attached_property_sets(model)
    quantity_checks = []
    if receipt.get("export_base_quantities"):
        quantity_owners = {}
        for relation in model.by_type("IfcRelDefinesByProperties"):
            definition = relation.RelatingPropertyDefinition
            if definition.is_a("IfcElementQuantity") and definition.Name == "Qto_SlabBaseQuantities":
                for owner in relation.RelatedObjects:
                    quantity_owners.setdefault(owner.id(), []).append(definition)
        for element_id in receipt.get("quantity_floor_ids", []):
            slabs = [s for s in model.by_type("IfcSlab") if s.Tag == str(element_id)]
            sets = quantity_owners.get(slabs[0].id(), []) if len(slabs) == 1 else []
            values = {}
            if len(sets) == 1:
                values = {q.Name: q[3] for q in sets[0].Quantities if q.is_a("IfcPhysicalSimpleQuantity")}
            # Standard quantities describe geometry, independently from HIFC manual areas.
            passed = len(sets) == 1 and bool(values) and all(
                isinstance(v, (float, int)) and math.isfinite(v) and v >= 0 for v in values.values())
            quantity_checks.append({"element_id": element_id, "sets": len(sets), "values": values, "passed": passed})
            if not passed:
                failures.append({"code": "SLAB_BASE_QUANTITIES_MISSING" if len(sets) != 1 or not values else "INVALID_BASE_QUANTITY",
                                 "element_id": element_id, "values": values})
    for item in model.by_type("IfcProduct"):
        owners_by_guid.setdefault(item.GlobalId, []).append(item)
        # Native DTV openings may inherit their host's Tag. They are voids,
        # not additional business carriers. Real duplicated slabs remain errors.
        if item.is_a("IfcFeatureElement"):
            mapped = sorted({p.Name for _, p in attached.get(item.id(), []) if p.Name in reference_psets})
            if mapped:
                failures.append({"code": "MAPPED_PROPERTY_SET_ON_FEATURE", "step_id": item.id(),
                                 "entity": item.is_a(), "property_sets": mapped})
            continue
        tag = getattr(item, "Tag", None)
        if tag is not None:
            owners.setdefault(str(tag), []).append(item)
    spatial_ids = set()
    for identity in receipt.get("spatial_export_identities", []):
        element_id = str(identity["element_id"])
        # A declared spatial identity must never fall back to a matching Tag.
        owners[element_id] = []
        try:
            if element_id in spatial_ids or identity.get("ifc_entity") not in {"IfcSpace", "IfcBuilding", "IfcBuildingStorey"}:
                raise ValueError("duplicate identity or unsupported spatial entity")
            spatial_ids.add(element_id)
            override = identity.get("native_global_id") or identity.get("ifc_guid_override")
            if override:
                global_id = override
                uuid.UUID(hex=ifcopenshell.guid.expand(global_id))
            else:
                global_id = ifcopenshell.guid.compress(uuid.UUID(identity["export_id"]).hex)
        except (KeyError, TypeError, ValueError):
            failures.append({"code": "INVALID_SPATIAL_EXPORT_IDENTITY", "element_id": element_id})
            continue
        candidates = owners_by_guid.get(global_id, [])
        expected_entity = identity["ifc_entity"]
        valid_entity = len(candidates) == 1 and candidates[0].is_a() == expected_entity
        spatial_owner_checks.append({"element_id": element_id, "global_id": global_id,
                                     "expected_entity": expected_entity, "matches": len(candidates), "passed": valid_entity})
        if len(candidates) == 1 and not valid_entity:
            failures.append({"code": "OWNER_ENTITY_MISMATCH", "element_id": element_id,
                             "expected": expected_entity, "actual": candidates[0].is_a()})
        elif valid_entity:
            owners[element_id] = candidates
    rows = receipt.get("materialization", [])
    if not rows:
        failures.append({"code": "NO_MAPPING_OBSERVATIONS"})
    expected_psets = {}
    for row in rows:
        expected_psets.setdefault(str(row["element_id"]), set()).add((row.get("binding", "实例"), row["property_set"]))
    for tag, names in expected_psets.items():
        candidates = owners.get(tag, [])
        if len(candidates) != 1:
            failures.append({"code": "OWNER_NOT_UNIQUE", "element_id": tag, "matches": len(candidates)})
            continue
        actual = {(binding, p.Name) for binding, p in attached.get(candidates[0].id(), []) if p.Name in reference_psets}
        if actual != names:
            failures.append({"code": "PROPERTY_SET_ROUTING_MISMATCH", "element_id": tag, "expected": sorted(names), "actual": sorted(actual)})
    verified = 0
    for row in rows:
        tag = str(row["element_id"])
        entry = {"element_id": tag, "property_set": row["property_set"], "property": row["property"], "source_parameter": row.get("source_parameter")}
        problems = []
        candidates = owners.get(tag, [])
        if row.get("status") != "MATERIALIZED":
            problems.append("SOURCE_VALUE_MISSING")
        if len(candidates) != 1:
            problems.append("OWNER_NOT_UNIQUE")
        else:
            sets = [p for binding, p in attached.get(candidates[0].id(), []) if p.Name == row["property_set"] and binding == row.get("binding", "实例")]
            if len(sets) != 1:
                problems.append("PROPERTY_SET_NOT_UNIQUE")
            else:
                properties = [p for p in sets[0].HasProperties if p.Name == row["property"]]
                if len(properties) != 1 or not properties[0].is_a("IfcPropertySingleValue"):
                    problems.append("PROPERTY_NOT_UNIQUE_OR_UNSUPPORTED")
                else:
                    nominal = properties[0].NominalValue
                    actual_type = nominal.is_a() if nominal else None
                    actual_value = nominal.wrappedValue if nominal else None
                    entry.update(actual_type=actual_type, actual_value=actual_value, expected_raw_type=row["raw_type"], expected_value=row.get("value"))
                    corrected_types = {
                        ("Pset_建筑技术信息属性集", "地下建筑层数", "Integerl"): "Integer",
                        ("Pset_建筑技术信息属性集", "建筑面积", "Reall"): "Real",
                    }
                    effective_type = corrected_types.get((row["property_set"], row["property"], row["raw_type"]), row["raw_type"])
                    expected_type = IFC_TYPES.get(effective_type)
                    entry["expected_export_type"] = effective_type
                    if expected_type is None:
                        problems.append("RAW_REFERENCE_TYPE_NOT_YET_VERIFIED")
                    elif actual_type != expected_type:
                        problems.append("IFC_TYPE_MISMATCH")
                    if not same_value(row.get("value"), actual_value, effective_type):
                        problems.append("VALUE_MISMATCH")
        entry["problems"] = problems
        entry["passed"] = not problems
        checks.append(entry)
        if problems:
            failures.append(entry)
        else:
            verified += 1
    after = hash_file(ifc_path)
    if before != after:
        failures.append({"code": "IFC_CHANGED_DURING_READ_ONLY_AUDIT"})
    return {
        "schema_version": "hifc.read-only-mapping-verification/1.0", "all_passed": not failures,
        "ifc_path": str(ifc_path), "ifc_sha256_before": before, "ifc_sha256_after": after,
        "ifc_schema": model.schema, "verified_properties": verified, "observed_properties": len(rows),
        "property_set_templates": len(model.by_type("IfcPropertySetTemplate")),
        "schema_error_count": len(schema_errors), "schema_errors": schema_errors,
        "temporary_bridge_property_count": len(leaked_bridges),
        "spatial_owner_checks": spatial_owner_checks,
        "quantity_checks": quantity_checks,
        "checks": checks, "failures": failures,
        "reference_software_ab_verified": False, "coverage_basis": "receipt materialization rows; not all 280 reference definitions",
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("ifc", type=Path)
    parser.add_argument("--receipt", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    receipt = args.receipt or args.ifc.with_suffix(".receipt.json")
    if args.output and args.output.resolve() in {args.ifc.resolve(), receipt.resolve()}:
        parser.error("报告路径不能与 IFC 或导出回执相同；检查保持只读。")
    report = verify_export(args.ifc, receipt)
    text = json.dumps(report, ensure_ascii=False, indent=2, default=str)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(text + "\n", encoding="utf-8")
    print(json.dumps({"all_passed": report["all_passed"], "verified_properties": report["verified_properties"], "observed_properties": report["observed_properties"], "failures": len(report["failures"]), "ifc_sha256_unchanged": report["ifc_sha256_before"] == report["ifc_sha256_after"]}, ensure_ascii=False))
    return 0 if report["all_passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
