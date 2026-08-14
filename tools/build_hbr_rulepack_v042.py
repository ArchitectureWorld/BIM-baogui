import argparse
import importlib.util
import json
import os
import sys
import tempfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
BASE_COMPILER_PATH = ROOT / "tools" / "build_hbr_rulepack.py"
DEFAULT_OVERLAY = (
    ROOT
    / "specs"
    / "hbr-rules"
    / "v1"
    / "source"
    / "hbr_rule_source.v0.4.2-overlay.json"
)


def _load_base_compiler():
    spec = importlib.util.spec_from_file_location(
        "hbr_rulepack_base_compiler", BASE_COMPILER_PATH
    )
    if spec is None or spec.loader is None:
        raise RuntimeError("cannot load base HBR rule-pack compiler")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _load_json(path: Path):
    with path.open(encoding="utf-8") as stream:
        return json.load(stream)


def _require(condition, message):
    if not condition:
        raise ValueError(message)


def _nonempty(value, label):
    _require(isinstance(value, str) and bool(value.strip()), f"{label} must be non-empty")
    return value.strip()


def _validate_overlay(overlay, base_source):
    _require(isinstance(overlay, dict), "v0.4.2 overlay must be an object")
    _require(overlay.get("overlaySchemaVersion") == "1.0.0", "unsupported overlay schema")
    _require(
        overlay.get("targetPackageId") == base_source["packageId"],
        "overlay packageId does not match base source",
    )
    _require(
        overlay.get("targetPackageVersion") == base_source["packageVersion"],
        "overlay packageVersion does not match base source",
    )

    roles = overlay.get("carrierRoles")
    properties = overlay.get("properties")
    _require(isinstance(roles, list) and roles, "overlay carrierRoles must be non-empty")
    _require(isinstance(properties, list) and properties, "overlay properties must be non-empty")

    base_role_ids = {item["roleId"] for item in base_source["carrierRoles"]}
    base_property_ids = {item["propertyId"] for item in base_source["properties"]}
    role_ids = []
    for index, role in enumerate(roles):
        label = f"overlay.carrierRoles[{index}]"
        role_id = _nonempty(role.get("roleId"), f"{label}.roleId")
        _require(role_id not in base_role_ids, f"{label}.roleId collides with base role")
        role_ids.append(role_id)
        _require(role.get("selectionPolicy") == "MANUAL_SEMANTIC_ASSIGNMENT", f"{label}.selectionPolicy must be manual")
        _require(role.get("ifcOwnerStrategy") == "BY_EXPORT_GUID", f"{label}.ifcOwnerStrategy must be BY_EXPORT_GUID")
        _require(role.get("revitCategories") == [], f"{label}.revitCategories must remain empty to prevent automatic matching")
        _require(role.get("allowedElementKinds") == [], f"{label}.allowedElementKinds must remain empty to prevent automatic matching")
        carriers = role.get("manualCarriers")
        _require(isinstance(carriers, list) and carriers, f"{label}.manualCarriers must be non-empty")
        carrier_keys = []
        for carrier_index, carrier in enumerate(carriers):
            carrier_label = f"{label}.manualCarriers[{carrier_index}]"
            category = _nonempty(carrier.get("category"), f"{carrier_label}.category")
            kinds = carrier.get("elementKinds")
            _require(isinstance(kinds, list) and kinds, f"{carrier_label}.elementKinds must be non-empty")
            normalized_kinds = sorted({_nonempty(kind, f"{carrier_label}.elementKinds") for kind in kinds})
            _require(kinds == normalized_kinds, f"{carrier_label}.elementKinds must be canonical sorted unique values")
            carrier_keys.extend((category, kind) for kind in normalized_kinds)
        _require(len(carrier_keys) == len(set(carrier_keys)), f"{label}.manualCarriers contains duplicate combinations")
        _require(carriers == sorted(carriers, key=lambda item: item["category"]), f"{label}.manualCarriers must be sorted by category")

    _require(len(role_ids) == len(set(role_ids)), "overlay roleIds must be unique")
    overlay_role_ids = set(role_ids)

    property_ids = []
    parameter_guids = []
    for index, prop in enumerate(properties):
        label = f"overlay.properties[{index}]"
        property_id = _nonempty(prop.get("propertyId"), f"{label}.propertyId")
        _require(property_id not in base_property_ids, f"{label}.propertyId collides with base property")
        property_ids.append(property_id)
        revit = prop.get("revit") or {}
        parameter_guid = _nonempty(revit.get("parameterGuid"), f"{label}.revit.parameterGuid")
        parameter_guids.append(parameter_guid)
        role_refs = prop.get("carrierRoleIds")
        _require(isinstance(role_refs, list) and role_refs, f"{label}.carrierRoleIds must be non-empty")
        _require(set(role_refs) <= overlay_role_ids, f"{label}.carrierRoleIds must reference overlay roles")
        requirement = prop.get("requirement") or {}
        _require(requirement.get("level") == "CONDITIONAL", f"{label}.requirement.level must be CONDITIONAL")
        condition_id = _nonempty(requirement.get("conditionId"), f"{label}.requirement.conditionId")
        _require(
            condition_id in {item["conditionId"] for item in base_source["conditions"]},
            f"{label}.conditionId is unknown",
        )
        ifc_write = prop.get("ifcWrite") or {}
        _require(ifc_write.get("ownerStrategy") == "BY_EXPORT_GUID", f"{label}.ifcWrite.ownerStrategy must be BY_EXPORT_GUID")
        _require("STAGE02" in (prop.get("stageOwnership") or []), f"{label} must belong to STAGE02")

    _require(len(property_ids) == len(set(property_ids)), "overlay propertyIds must be unique")
    _require(len(parameter_guids) == len(set(parameter_guids)), "overlay parameter GUIDs must be unique")
    base_parameter_guids = {item["revit"]["parameterGuid"] for item in base_source["properties"]}
    _require(not (set(parameter_guids) & base_parameter_guids), "overlay parameter GUID collides with base source")


def merge_overlay(base_source, overlay):
    merged = json.loads(json.dumps(base_source, ensure_ascii=False))
    merged["carrierRoles"].extend(overlay["carrierRoles"])
    merged["properties"].extend(overlay["properties"])
    return merged


def compile_rulepack(source_path, baseline_path, overlay_path, output_path):
    base = _load_base_compiler()
    source = base.load_validated_rule_source(source_path, baseline_path)
    overlay = _load_json(Path(overlay_path))
    _validate_overlay(overlay, source)
    merged = merge_overlay(source, overlay)
    rulepack_bytes = base.build_rulepack_bytes(merged)

    output_path = Path(output_path)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary_name = tempfile.mkstemp(
        dir=str(output_path.parent),
        prefix=f".{output_path.name}.",
        suffix=".tmp",
    )
    try:
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(rulepack_bytes)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary_name, output_path)
    except BaseException:
        try:
            os.unlink(temporary_name)
        except FileNotFoundError:
            pass
        raise


def _parser():
    parser = argparse.ArgumentParser(description="Compile base HBR source plus v0.4.2 Stage02 semantic overlay")
    parser.add_argument("--source", required=True)
    parser.add_argument("--baseline", required=True)
    parser.add_argument("--overlay", default=str(DEFAULT_OVERLAY))
    parser.add_argument("--output", required=True)
    return parser


def main(argv=None):
    args = _parser().parse_args(argv)
    try:
        compile_rulepack(args.source, args.baseline, args.overlay, args.output)
    except (OSError, UnicodeError, ValueError, RuntimeError) as error:
        print(f"HBR v0.4.2 rule-pack compilation failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
