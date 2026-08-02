import json
import re
import uuid
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SOURCE_PATH = ROOT / "specs/hbr-rules/v1/source/hbr_rule_source.v1.json"
OLD_OFFICIAL_PATH = ROOT / "specs/hifc-mapping/v1/data/wuhan_planning_rules.v1.json"
OLD_STAGE01_PATH = (
    ROOT
    / "src/BIMBaoGui.Stage01/Resources/stage01_file_initialization_registry_v0.1.json"
)
OLD_CARRIERS_PATH = (
    ROOT / "specs/hifc-mapping/v1/data/implementation_object_carriers.v1.json"
)


def _load(path: Path):
    with path.open(encoding="utf-8") as stream:
        return json.load(stream)


def _normalize_pset(value: str) -> str:
    return value if value.startswith("Pset_") else f"Pset_{value}"


def _rule_identity(rule):
    return (
        rule["ifc"]["entity"],
        _normalize_pset(rule["ifc"]["propertySet"]),
        rule["ifc"]["property"],
    )


def _old_identity(rule):
    official = rule["official"]
    return (
        official["ifcEntity"],
        _normalize_pset(official["propertySet"]),
        official["ifcProperty"],
    )


def test_all_identities_ids_and_parameter_guids_are_unique():
    source = _load(SOURCE_PATH)
    properties = source["properties"]
    mvd = [rule for rule in properties if rule["contractKind"] == "MVD"]
    identities = [_rule_identity(rule) for rule in mvd]
    ids = [rule["propertyId"] for rule in properties]
    guids = [rule["revit"]["parameterGuid"] for rule in properties]

    assert len(identities) == len(set(identities)) == 356
    assert len(ids) == len(set(ids)) == 359
    assert len(guids) == len(set(guids)) == 359
    assert all(uuid.UUID(value).version == 5 for value in ids)
    assert all(uuid.UUID(value).version == 5 for value in guids)


def test_old_166_ids_guid_seeds_canonical_keys_and_aliases_are_frozen():
    source = _load(SOURCE_PATH)
    old = _load(OLD_OFFICIAL_PATH)
    actual = {rule["propertyId"]: rule for rule in source["properties"] if rule["officialPlugin"]["inExtracted166"]}
    expected = {rule["propertyId"]: rule for rule in old["properties"]}
    assert set(actual) == set(expected)
    for property_id, legacy in expected.items():
        rule = actual[property_id]
        assert rule["propertyId"] == legacy["propertyId"]
        assert rule["revit"]["parameterGuid"] == legacy["canonical"]["revitParameterGuid"]
        assert rule["canonicalKey"] == legacy["canonicalKey"]
        assert legacy["canonical"]["revitParameterName"] in rule["revit"]["legacyNames"]
        assert rule["officialPlugin"]["originalIdentity"] == "|".join(_old_identity(legacy))


def test_new_mvd_only_ids_are_written_fixed_uuid5_values():
    source = _load(SOURCE_PATH)
    namespace = uuid.UUID(source["guidNamespace"])
    new_rules = [
        rule
        for rule in source["properties"]
        if rule["contractKind"] == "MVD"
        and not rule["officialPlugin"]["inExtracted166"]
    ]

    assert len(new_rules) == 193
    for rule in new_rules:
        expected = str(uuid.uuid5(namespace, rule["canonicalKey"]))
        assert rule["propertyId"] == expected
        assert rule["revit"]["parameterGuid"] == expected


def test_no_style_ids_are_promoted_to_normalized_fields_and_rows_are_preserved():
    source = _load(SOURCE_PATH)
    mvd = [rule for rule in source["properties"] if rule["contractKind"] == "MVD"]

    assert {rule["source"]["row"] for rule in mvd} == set(range(2, 358))
    for rule in mvd:
        assert rule["ifc"]["sourceUnit"] != "14"
        assert rule["ifc"]["canonicalUnit"] != "14"
        assert rule["ifc"]["declaredType"] != "14"
        if rule["source"]["rawValueKind"] == "14":
            assert rule["ifc"]["declaredType"] != "14"
        assert "rawDeclaredType" in rule["source"]
        assert "rawUnit" in rule["source"]
        if rule["source"]["rawDeclaredType"].casefold() == "ifctext":
            assert rule["ifc"]["declaredType"] == "IfcText"
        if rule["source"]["rawUnit"] == "14":
            assert rule["ifc"]["sourceUnit"] is None


def test_entity_comes_from_mvd_entity_id_and_never_from_style_or_composite_column():
    source = _load(SOURCE_PATH)
    mvd = [rule for rule in source["properties"] if rule["contractKind"] == "MVD"]
    allowed = {
        "IfcProject", "IfcSite", "IfcBuilding", "IfcBuildingStorey", "IfcSpace",
        "IfcSpatialZone", "IfcWall", "IfcSlab", "IfcRoof", "IfcWindow", "IfcStairFlight", "IfcOrganization",
    }
    assert {rule["ifc"]["entity"] for rule in mvd} == allowed
    for rule in mvd:
        assert rule["ifc"]["entity"] == rule["source"]["rawEntityId"]
        assert "14" not in rule["ifc"]["entity"]
        assert "/" not in rule["ifc"]["entity"]
        assert "14" not in rule["canonicalKey"]
        assert "/" not in rule["canonicalKey"]
        assert rule["ifc"]["propertySet"].startswith("Pset_")


def test_every_property_has_legacy_alias_and_resolvable_role_details():
    source = _load(SOURCE_PATH)
    roles = {role["roleId"]: role for role in source["carrierRoles"]}
    for rule in source["properties"]:
        expected = f"HIFC.{rule['source']['rawPropertySetName'].replace('Pset_', '')}.{rule['source']['rawProperty']}"
        assert expected in rule["revit"]["legacyNames"]
        assert all(role_id in roles for role_id in rule["carrierRoleIds"])
    assert {role["ifcEntity"] for role in roles.values()} >= {
        rule["ifc"]["entity"] for rule in source["properties"]
    }
    for role in roles.values():
        assert {"displayName", "modelFileTypes", "revitCategories", "allowedElementKinds", "nameAliases", "familyAliases", "typeAliases", "cardinality", "selectionPolicy", "ifcOwnerStrategy"} <= set(role)
        assert {"min", "max"} <= set(role["cardinality"])
        if role["ifcEntity"] in {"IfcProject", "IfcSite", "IfcBuilding"}:
            assert role["cardinality"]["max"] == 1
        else:
            assert role["cardinality"]["max"] is None


def test_profiles_and_tasks_preserve_model_group_and_condition_mapping():
    source = _load(SOURCE_PATH)
    profiles = {profile["profileId"]: profile for profile in source["modelProfiles"]}
    assert {key: len(value["taskIds"]) for key, value in profiles.items()} == {"总平模型": 15, "单体建筑—地上": 7, "单体建筑—地下": 6}
    tasks = {task["taskId"]: task for task in source["tasks"]}
    assert all(task_id.startswith("SITE.") for task_id in profiles["总平模型"]["taskIds"])
    assert all(task_id.startswith("ABOVE.") for task_id in profiles["单体建筑—地上"]["taskIds"])
    assert all(task_id.startswith("UNDERGROUND.") for task_id in profiles["单体建筑—地下"]["taskIds"])
    assert tasks["SITE.OTHER_LAND"]["conditionId"] == "site.other_land"
    assert tasks["ABOVE.ROOF"]["conditionId"] == "building.roof"
    assert tasks["UNDERGROUND.PARKING"]["conditionId"] == "underground.parking"


def test_ifctext_spelling_is_normalized_and_runtime_types_follow_real_units():
    source = _load(SOURCE_PATH)
    for rule in source["properties"]:
        raw_type = rule["source"].get("rawDeclaredType")
        if isinstance(raw_type, str) and raw_type.casefold() == "ifctext":
            assert rule["ifc"]["declaredType"] == "IfcText"

        if rule["ifc"]["declaredType"] != "IfcReal":
            continue
        allowed = set(rule["ifc"]["allowedRuntimeTypes"])
        assert "IfcReal" in allowed
        unit = rule["ifc"]["canonicalUnit"]
        if unit == "m":
            assert "IfcLengthMeasure" in allowed
        elif unit == "m2":
            assert "IfcAreaMeasure" in allowed
        elif unit == "m3":
            assert "IfcVolumeMeasure" in allowed
        elif unit == "deg":
            assert "IfcPlaneAngleMeasure" in allowed


def test_parameter_names_visibility_and_unclassified_requiredness_are_explicit():
    source = _load(SOURCE_PATH)
    for rule in source["properties"]:
        pset_name = rule["source"]["rawPropertySetName"].replace("Pset_", "")
        assert rule["revit"]["parameterName"] == (
            f"HBR｜{pset_name}｜{rule['source']['rawProperty']}"
        )
        assert rule["revit"]["visible"] is True
        assert rule["revit"]["userModifiable"] is True
        assert rule["requirement"]["level"] == "UNCLASSIFIED"
        assert rule["requirement"]["conditionId"] is None


def test_stage01_refs_match_the_registry_and_have_no_dangling_ids():
    source = _load(SOURCE_PATH)
    old_stage01 = _load(OLD_STAGE01_PATH)
    property_ids = {rule["propertyId"] for rule in source["properties"]}
    refs = source["stage01"]["fieldRefs"]

    assert {ref["fieldKey"] for ref in refs} == {
        field["field_key"] for field in old_stage01["mvd_fields"]
    }
    assert {ref["propertyId"] for ref in refs} <= property_ids
    assert all(ref["sourceRow"] in range(2, 358) for ref in refs)


def test_all_cross_references_resolve_and_carriers_are_migrated_once():
    source = _load(SOURCE_PATH)
    old_carriers = _load(OLD_CARRIERS_PATH)["carriers"]
    carrier_ids = {role["roleId"] for role in source["carrierRoles"]}
    condition_ids = {condition["conditionId"] for condition in source["conditions"]}
    task_ids = {task["taskId"] for task in source["tasks"]}
    property_ids = {rule["propertyId"] for rule in source["properties"]}

    assert carrier_ids >= {
        carrier["canonicalObjectKind"] for carrier in old_carriers.values()
    }
    assert all(
        set(rule["carrierRoleIds"]) <= carrier_ids for rule in source["properties"]
    )
    assert all(
        rule["requirement"]["conditionId"] is None
        or rule["requirement"]["conditionId"] in condition_ids
        for rule in source["properties"]
    )
    assert all(
        task["conditionId"] is None or task["conditionId"] in condition_ids
        for task in source["tasks"]
    )
    assert all(
        set(profile["taskIds"]) <= task_ids for profile in source["modelProfiles"]
    )
    assert {alias["propertyId"] for alias in source["legacyAliases"]} <= property_ids


def test_tasks_and_conditions_are_traceable_to_the_existing_catalogs():
    source = _load(SOURCE_PATH)
    task_catalog = (
        ROOT / "src/BIMBaoGui.Stage01/TaskPlanning/TaskRuleCatalog.cs"
    ).read_text(encoding="utf-8")
    activation_catalog = (
        ROOT / "src/BIMBaoGui.Stage01/Context/RuleActivationCatalog.cs"
    ).read_text(encoding="utf-8")

    catalog_task_ids = set(
        re.findall(r'rules\.Add\((?:Rule|Conditional)\(model, "([^"]+)"', task_catalog)
    )
    registry_provider = (ROOT / "src/BIMBaoGui.Stage01/Infrastructure/Stage01RegistryProvider.cs").read_text(encoding="utf-8")
    catalog_conditions = set(re.findall(r'new ConditionDefinition\("([^"]+)"', registry_provider))

    assert {task["taskId"] for task in source["tasks"]} == catalog_task_ids
    assert {condition["conditionId"] for condition in source["conditions"]} == catalog_conditions
    assert len(catalog_conditions) == 14
    assert all(task["source"] == "TaskRuleCatalog.cs" for task in source["tasks"])
    assert {condition["source"] for condition in source["conditions"]} <= {
        "RuleActivationCatalog.cs", "Stage01RegistryProvider.cs"
    }


def test_mvd_source_evidence_and_canonical_fields_remain_workbook_faithful():
    source = _load(SOURCE_PATH)
    mvd = [rule for rule in source["properties"] if rule["contractKind"] == "MVD"]
    for rule in mvd:
        raw = rule["source"]
        assert {"rawProperty", "rawPropertySetId", "rawPropertySetName"} <= set(raw)
        assert rule["ifc"]["entity"] == raw["rawEntityId"]
        assert rule["ifc"]["propertySet"] == raw["rawPropertySetId"]
        assert rule["ifc"]["property"] == raw["rawProperty"]
        assert rule["ifc"]["sourceUnit"] == (None if raw["rawUnit"] in {"", "14"} else raw["rawUnit"])
    by_row = {rule["source"]["row"]: rule for rule in mvd}
    assert by_row[47]["ifc"]["property"] == "基点坐标 X"
    assert by_row[297]["ifc"]["propertySet"] == "Pset_Manifest"
    assert by_row[64]["ifc"]["sourceUnit"] == "度"


def test_tasks_and_conditions_are_complete_rebuildable_catalog_records():
    source = _load(SOURCE_PATH)
    tasks = {task["taskId"]: task for task in source["tasks"]}
    required = {"modelFileType", "name", "objectCode", "requirement", "conditionId", "sequence", "skeletonTask", "attributeRequirements", "dependencies", "geometryChecks", "propertyChecks", "targetComparisons", "source"}
    assert all(required <= set(task) for task in tasks.values())
    assert tasks["SITE.SKELETON"]["sequence"] == 10
    assert tasks["SITE.OTHER_LAND"]["conditionId"] == "site.other_land"
    assert tasks["SITE.OTHER_LAND"]["dependencies"] == ["SITE.TOTAL_LAND"]
    assert tasks["ABOVE.ROOF"]["conditionId"] == "building.roof"
    assert tasks["ABOVE.ROOF"]["geometryChecks"] == ["屋顶与主体关系有效"]
    assert tasks["UNDERGROUND.PARKING"]["conditionId"] == "underground.parking"
    assert tasks["UNDERGROUND.PARKING"]["attributeRequirements"] == ["停车类型", "机动车位", "非机动车位"]
    conditions = {condition["conditionId"]: condition for condition in source["conditions"]}
    assert all({"displayName", "group", "activationRuleId", "evidenceStatus", "source"} <= set(condition) for condition in conditions.values())
    assert conditions["site.other_land"]["activationRuleId"] == "HBR.SITE.OTHER_LAND"
    assert conditions["building.roof"]["activationRuleId"] is None
    assert conditions["building.roof"]["evidenceStatus"] == "NOT_IN_LEGACY_ACTIVATION_CATALOG"
