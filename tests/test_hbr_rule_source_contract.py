import json
import copy
from pathlib import Path

import jsonschema


ROOT = Path(__file__).resolve().parents[1]
SCHEMA_PATH = ROOT / "specs/hbr-rules/v1/schemas/hbr_rule_source.schema.json"
SOURCE_PATH = ROOT / "specs/hbr-rules/v1/source/hbr_rule_source.v1.json"


def _load(path: Path):
    with path.open(encoding="utf-8") as stream:
        return json.load(stream)


def _identity(rule):
    pset = rule["ifc"]["propertySet"]
    if not pset.startswith("Pset_"):
        pset = f"Pset_{pset}"
    return rule["ifc"]["entity"], pset, rule["ifc"]["property"]


def test_rule_source_matches_declared_json_schema():
    schema = _load(SCHEMA_PATH)
    source = _load(SOURCE_PATH)

    jsonschema.Draft202012Validator.check_schema(schema)
    errors = sorted(
        jsonschema.Draft202012Validator(schema).iter_errors(source),
        key=lambda error: list(error.absolute_path),
    )
    assert not errors, "\n".join(
        f"{'.'.join(map(str, error.absolute_path)) or '<root>'}: {error.message}"
        for error in errors
    )


def test_schema_closes_top_level_and_property_contracts():
    schema = _load(SCHEMA_PATH)

    assert schema["additionalProperties"] is False
    assert schema["$defs"]["propertyRule"]["additionalProperties"] is False
    assert schema["$defs"]["ifcContract"]["additionalProperties"] is False
    assert schema["$defs"]["revitContract"]["additionalProperties"] is False
    required = set(schema["$defs"]["propertyRule"]["required"])
    assert {"stageOwnership", "suggestion", "ifcWrite"} <= required
    assert {"artifact", "sheet", "row"} <= set(schema["$defs"]["sourceContract"]["required"])
    assert {"bindingScope", "storageType", "parameterType"} <= set(schema["$defs"]["revitContract"]["required"])


def test_rule_source_preserves_verified_set_relationships():
    source = _load(SOURCE_PATH)
    properties = source["properties"]
    mvd = [p for p in properties if p["contractKind"] == "MVD"]
    extension = [p for p in properties if p["contractKind"] == "HIFC_EXTENSION"]
    official = [p for p in properties if p["officialPlugin"]["inExtracted166"]]
    stage01 = source["stage01"]["fieldRefs"]
    official_ids = {p["propertyId"] for p in official}

    assert len(mvd) == 356
    assert len(extension) == 3
    assert len(official) == 166
    assert sum(p["contractKind"] == "MVD" for p in official) == 163
    assert len(stage01) == 102
    assert sum(ref["propertyId"] in official_ids for ref in stage01) == 89
    assert sum(not p["officialPlugin"]["inExtracted166"] for p in mvd) == 193


def test_hifc_extensions_are_exactly_the_three_verified_identities():
    source = _load(SOURCE_PATH)
    actual = {
        _identity(rule)
        for rule in source["properties"]
        if rule["contractKind"] == "HIFC_EXTENSION"
    }
    expected = {
        ("IfcDoor", "Pset_门信息属性集", "开启方向"),
        ("IfcDuctSegment", "Pset_风管段信息属性集", "隔热层厚度"),
        ("IfcSpace", "Pset_建筑空间信息属性集", "空间形成方式"),
    }

    assert actual == expected


def test_required_top_level_sections_are_present():
    source = _load(SOURCE_PATH)
    required = {
        "schemaVersion",
        "packageId",
        "packageVersion",
        "guidNamespace",
        "evidenceSources",
        "properties",
        "carrierRoles",
        "modelProfiles",
        "conditions",
        "tasks",
        "legacyAliases",
        "stage01",
    }

    assert required <= set(source)
    assert source["schemaVersion"] == "1.0.0"
    assert source["packageId"] == "HBR-WUHAN-PLANNING"
    assert len(source["modelProfiles"]) == 3


def test_property_contracts_have_the_planned_implementation_sections():
    source = _load(SOURCE_PATH)
    for rule in source["properties"]:
        assert rule["source"]["artifact"]
        assert rule["source"]["sheet"]
        assert rule["source"]["row"] is None or rule["source"]["row"] >= 2
        assert rule["revit"]["bindingScope"] == "INSTANCE"
        assert rule["revit"]["storageType"]
        assert rule["revit"]["parameterType"]
        assert rule["officialPlugin"]["evidenceStatus"]
        assert rule["carrierRoleIds"]
        assert rule["stageOwnership"] == ["STAGE02", "STAGE03"]
        assert rule["suggestion"]["kind"] == "EXISTING_OR_ALIAS"
        assert rule["ifcWrite"]["writeStrategy"] == "CREATE_OR_UPDATE_PSET"
    assert all(
        rule["extensionReason"]
        for rule in source["properties"]
        if rule["contractKind"] == "HIFC_EXTENSION"
    )


def test_schema_closes_source_official_task_and_condition_contracts():
    schema = _load(SCHEMA_PATH)
    assert schema["$defs"]["sourceContract"]["additionalProperties"] is False
    assert schema["$defs"]["officialPluginContract"]["additionalProperties"] is False
    assert schema["$defs"]["taskContract"]["additionalProperties"] is False
    assert schema["$defs"]["conditionContract"]["additionalProperties"] is False


def test_schema_closes_all_top_level_collection_item_contracts():
    schema = _load(SCHEMA_PATH)
    for definition in (
        "evidenceSourceContract", "carrierRoleContract", "cardinalityContract",
        "modelProfileContract", "legacyAliasContract", "stage01Contract", "stage01FieldRefContract",
    ):
        assert schema["$defs"][definition]["additionalProperties"] is False
    assert schema["properties"]["evidenceSources"]["items"] == {"$ref": "#/$defs/evidenceSourceContract"}
    assert schema["properties"]["carrierRoles"]["items"] == {"$ref": "#/$defs/carrierRoleContract"}
    assert schema["properties"]["modelProfiles"]["items"] == {"$ref": "#/$defs/modelProfileContract"}
    assert schema["properties"]["legacyAliases"]["items"] == {"$ref": "#/$defs/legacyAliasContract"}
    assert schema["properties"]["stage01"] == {"$ref": "#/$defs/stage01Contract"}


def test_schema_rejects_invalid_core_business_shapes():
    schema = _load(SCHEMA_PATH)
    source = _load(SOURCE_PATH)
    validator = jsonschema.Draft202012Validator(schema, format_checker=jsonschema.FormatChecker())
    mutations = []
    def mutate(path, value):
        candidate = copy.deepcopy(source)
        cursor = candidate
        for key in path[:-1]: cursor = cursor[key]
        cursor[path[-1]] = value
        mutations.append(candidate)
    mutate(["properties", 0, "carrierRoleIds"], [123])
    mutate(["properties", 0, "requirement"], {})
    mutate(["properties", 0, "stageOwnership"], ["BOGUS"])
    mutate(["properties", 0, "suggestion"], {"kind": "TYPO", "aliases": [7]})
    mutate(["properties", 0, "ifcWrite"], {})
    mutate(["properties", 0, "ifc", "allowedRuntimeTypes"], [7])
    mutate(["tasks", 0, "requirement"], "BOGUS")
    mutate(["properties", 0, "requirement"], {"level": "CONDITIONAL", "conditionId": None})
    mutate(["properties", 0, "requirement"], {"level": "REQUIRED", "conditionId": "site.other_land"})
    non_official_index = next(i for i, rule in enumerate(source["properties"]) if not rule["officialPlugin"]["inExtracted166"])
    mutate(["properties", non_official_index, "officialPlugin"], {"inExtracted166": False, "evidenceStatus": "OFFICIAL_EXTRACTED", "originalIdentity": "fake"})
    mutate(["carrierRoles", 0, "cardinality"], {"min": 2, "max": 1})
    assert all(list(validator.iter_errors(candidate)) for candidate in mutations)
