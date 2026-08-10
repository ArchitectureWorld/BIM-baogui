import json
import re
import uuid
import hashlib
from pathlib import Path

import pytest


ROOT = Path(__file__).resolve().parents[1]
SOURCE_PATH = ROOT / "specs/hbr-rules/v1/source/hbr_rule_source.v1.json"
BASELINE_PATH = (
    ROOT
    / "specs/hbr-rules/v1/compatibility/hbr_rule_compatibility_baseline.v1.json"
)
OLD_OFFICIAL_PATH = ROOT / "specs/hifc-mapping/v1/data/wuhan_planning_rules.v1.json"
OLD_STAGE01_PATH = (
    ROOT
    / "src/BIMBaoGui.Stage01/Resources/stage01_file_initialization_registry_v0.1.json"
)
OLD_CARRIERS_PATH = (
    ROOT / "specs/hifc-mapping/v1/data/implementation_object_carriers.v1.json"
)
OLD_BINDINGS_PATH = (
    ROOT / "specs/hifc-mapping/v1/generated/GH_HIFC_ParameterBindings.json"
)
OLD_COMPATIBILITY_PATH = (
    ROOT
    / "specs/hifc-mapping/v1/data/official_plugin_compatibility_status.v1.json"
)
STAGE01_REGISTRY_PROVIDER_PATH = (
    ROOT / "src/BIMBaoGui.Stage01/Infrastructure/Stage01RegistryProvider.cs"
)
STAGE01_KEYS_PATH = ROOT / "src/BIMBaoGui.Stage01/Core/Stage01Keys.cs"
PLANNING_TARGET_REQUIREMENT_POLICY_PATH = (
    ROOT / "src/BIMBaoGui.Stage01/Core/PlanningTargetRequirementPolicy.cs"
)
OFFICIAL_MAPPING_CATALOG_PATH = (
    ROOT / "src/BIMBaoGui.Stage01/Hifc/OfficialHifcMappingCatalog.cs"
)
OFFICIAL_COMPATIBILITY_CATALOG_PATH = (
    ROOT / "src/BIMBaoGui.Stage01/Hifc/OfficialPluginCompatibilityCatalog.cs"
)
SNAPSHOT_DIR = ROOT / "tests/BIMBaoGui.Stage01.Core.Tests/Snapshots"
STAGE01_SNAPSHOT_PATH = SNAPSHOT_DIR / "stage01-registry.v1.json"
TASK_SNAPSHOT_PATH = SNAPSHOT_DIR / "task-rules.v1.json"
ACTIVATION_SNAPSHOT_PATH = SNAPSHOT_DIR / "rule-activation.v1.json"
VERIFIED_INTERNAL_OPTIONAL_FIELD_PRESENCE = {
    "allowed_values": frozenset(
        {
            "HBR|FileIdentity|ModelFileType",
            "HBR|ProjectUnits|Angle",
            "HBR|ProjectUnits|Area",
            "HBR|ProjectUnits|Length",
            "HBR|Workflow|InitializationStatus",
        }
    ),
    "default": frozenset(
        {
            "HBR|ProjectUnits|Angle",
            "HBR|ProjectUnits|Area",
            "HBR|ProjectUnits|Length",
            "HBR|Workflow|Version",
        }
    ),
}
VERIFIED_POLICY_ENTITIES = frozenset(
    {
        "IfcBuilding",
        "IfcBuildingStorey",
        "IfcDoor",
        "IfcDuctSegment",
        "IfcOrganization",
        "IfcProject",
        "IfcSite",
        "IfcSpace",
        "IfcSpatialZone",
    }
)
VERIFIED_POLICY_FIELD_PRESENCE = {
    field: VERIFIED_POLICY_ENTITIES
    for field in (
        "officialObjectMappingEvidence",
        "revitCarrier",
        "writePolicy",
        "officialExportVerified",
    )
}

ESSENTIAL_FIELD_KEYS = frozenset(
    {
        "HBR|FileIdentity|SubitemCode",
        "HBR|FileIdentity|SubitemName",
        "HBR|FileIdentity|ModelFileType",
        "HBR|FileIdentity|ModelScope",
        "HBR|FileIdentity|FileGuid",
        "HBR|Workflow|Version",
        "HBR|Workflow|InitializationStatus",
        "HBR|SpatialReference|TrueNorthAngle",
        "HBR|ProjectUnits|Length",
        "HBR|ProjectUnits|Area",
        "HBR|ProjectUnits|Angle",
        "IfcProject|Pset_申报信息属性集|项目编号",
        "IfcProject|Pset_申报信息属性集|项目名称",
        "IfcProject|Pset_申报信息属性集|项目地址",
        "IfcProject|Pset_申报信息属性集|建设单位",
        "IfcProject|Pset_申报信息属性集|设计单位",
        "IfcProject|Pset_Manifest|阶段",
        "IfcProject|Pset_申报信息属性集|基点坐标X",
        "IfcProject|Pset_申报信息属性集|基点坐标Y",
        "IfcProject|Pset_申报信息属性集|基点高程",
        "IfcProject|Pset_申报信息属性集|坐标系名称",
        "IfcProject|Pset_申报信息属性集|高程系名称",
        "IfcOrganization|Pset_组织通用属性集|企业名称",
        "IfcOrganization|Pset_组织通用属性集|社会统一信用代码",
        "IfcOrganization|Pset_组织通用属性集|项目参建类型",
        "IfcOrganization|Pset_组织通用属性集|联系人姓名",
        "IfcOrganization|Pset_组织通用属性集|联系人手机号码",
    }
)

STATIC_DEFAULTS = {
    "HBR|FileIdentity|ModelFileType": "总平模型",
    "HBR|FileIdentity|ModelScope": "项目总平面报规模型",
    "HBR|Workflow|Version": "0.1.0",
    "HBR|Workflow|InitializationStatus": "未初始化",
    "IfcProject|Pset_Manifest|阶段": "规划报建",
    "IfcProject|Pset_申报信息属性集|坐标系名称": "CGCS2000",
    "IfcProject|Pset_申报信息属性集|高程系名称": "1985国家高程基准",
    "HBR|SpatialReference|TrueNorthAngle": "0",
    "HBR|ProjectUnits|Length": "m",
    "HBR|ProjectUnits|Area": "m²",
    "HBR|ProjectUnits|Angle": "°",
}
FILE_GUID_KEY = "HBR|FileIdentity|FileGuid"


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


def _compact_csharp(path):
    compact = " ".join(path.read_text(encoding="utf-8").split())
    return re.sub(r"\(\s+", "(", re.sub(r"\s+\)", ")", compact))


def _essential_keys_from_legacy_provider():
    snapshot = _load(STAGE01_SNAPSHOT_PATH)
    resolved = {
        field["key"] for field in snapshot["fields"] if field["essential"]
    }
    assert len(resolved) == 27
    return frozenset(resolved)


def _assert_legacy_runtime_projection_contracts():
    registry = _compact_csharp(STAGE01_REGISTRY_PROVIDER_PATH)
    for contract in (
        "FromDatabase(HbrRuleDatabase database)",
        "database.Package.Stage01.InternalWorkflowFields",
        "database.Package.Stage01.FieldRefs",
        "AllowedValues = source.AllowedValues",
        "source.DefaultStrategy",
        'case "NONE":',
        'case "STATIC":',
        'case "NEW_GUID":',
    ):
        assert contract in registry
    assert "GetManifestResourceStream" not in registry
    assert "EssentialKeys" not in registry

    mapping = _compact_csharp(OFFICIAL_MAPPING_CATALOG_PATH)
    for contract in (
        "database.Package.LegacyAliases",
        "property.OfficialPlugin.LegacyProjection",
        "ParseOriginalIdentity(property.OfficialPlugin.OriginalIdentity",
        "ParameterName = alias.Alias,",
        "Category = legacy.Category.Trim(),",
        "Carrier = legacy.Carrier,",
        "PersistenceMode = legacy.PersistenceMode,",
        "SharedParameterType = legacy.SharedParameterType,",
        "string sourceOverride = legacy.SourceParameterOverride;",
        "SourceParameterOverride = sourceOverride,",
        "OfficialSourceParameterGroup = legacy.OfficialSourceParameterGroup.Trim(),",
        "Unit = legacy.OfficialUnit ?? string.Empty,",
    ):
        assert contract in mapping
    assert "GetManifestResourceStream" not in mapping

    compatibility = _compact_csharp(OFFICIAL_COMPATIBILITY_CATALOG_PATH)
    for contract in (
        "database.Package.Stage01.OfficialPluginCompatibility",
        "OfficialObjectMappingEvidence = policy.OfficialObjectMappingEvidence,",
        "RevitCarrier = policy.RevitCarrier,",
        "WritePolicy = policy.WritePolicy,",
        "OfficialExportVerified = policy.OfficialExportVerified",
        "exception.Reason",
    ):
        assert contract in compatibility
    assert "GetManifestResourceStream" not in compatibility

def _assert_optional_presence_for_stable_key(
    record, stable_key, verified_presence
):
    for field, stable_keys_with_field in verified_presence.items():
        assert (field in record) == (stable_key in stable_keys_with_field)


def _assert_verified_legacy_evidence_presence_sets(
    old_stage01, old_compatibility
):
    internal_fields = old_stage01["internal_workflow_fields"]
    actual_internal_presence = {
        field: frozenset(
            item["field_key"] for item in internal_fields if field in item
        )
        for field in VERIFIED_INTERNAL_OPTIONAL_FIELD_PRESENCE
    }
    assert (
        actual_internal_presence
        == VERIFIED_INTERNAL_OPTIONAL_FIELD_PRESENCE
    )

    entities = old_compatibility["entities"]
    assert frozenset(entities) == VERIFIED_POLICY_ENTITIES
    actual_policy_presence = {
        field: frozenset(
            ifc_entity
            for ifc_entity, record in entities.items()
            if record is not None and field in record
        )
        for field in VERIFIED_POLICY_FIELD_PRESENCE
    }
    assert actual_policy_presence == VERIFIED_POLICY_FIELD_PRESENCE


def _project_internal_field_like_legacy_runtime(legacy):
    field_key = legacy["field_key"]
    _assert_optional_presence_for_stable_key(
        legacy,
        field_key,
        VERIFIED_INTERNAL_OPTIONAL_FIELD_PRESENCE,
    )
    allowed_values = (
        legacy["allowed_values"] if "allowed_values" in legacy else None
    )
    default_value = legacy["default"] if "default" in legacy else None
    if default_value is None or not default_value.strip():
        default_value = None
    default_contract = _expected_default_contract(field_key)
    return {
        "fieldKey": field_key,
        "label": legacy["property"],
        "type": legacy["type"],
        "uiGroup": legacy["ui_group"],
        "sourceKind": legacy["source_kind"],
        "allowedValues": [] if allowed_values is None else allowed_values,
        "essential": field_key in ESSENTIAL_FIELD_KEYS,
        "defaultStrategy": default_contract["defaultStrategy"],
        "defaultValue": default_contract["defaultValue"],
    }


def _expected_default_contract(field_key):
    if field_key == FILE_GUID_KEY:
        return {"defaultStrategy": "NEW_GUID", "defaultValue": None}
    if field_key in STATIC_DEFAULTS:
        return {
            "defaultStrategy": "STATIC",
            "defaultValue": STATIC_DEFAULTS[field_key],
        }
    return {"defaultStrategy": "NONE", "defaultValue": None}


def _project_official_mapping_like_legacy_runtime(binding, legacy_rule):
    category = binding["category"]
    carrier = binding["carrier"]
    persistence_mode = binding["persistenceMode"]
    parameter_group = binding["officialSourceParameterGroup"]
    shared_parameter_type = legacy_rule["canonical"][
        "sharedParameterType"
    ]
    source_parameter_override = legacy_rule["official"][
        "sourceParameterOverride"
    ]
    official_unit = legacy_rule["official"]["unit"]
    return {
        "category": "" if category is None else category.strip(),
        "carrier": "" if carrier is None else carrier,
        "persistenceMode": (
            "" if persistence_mode is None else persistence_mode
        ),
        "sharedParameterType": (
            "" if shared_parameter_type is None else shared_parameter_type
        ),
        "officialSourceParameterGroup": (
            "" if parameter_group is None else parameter_group.strip()
        ),
        "sourceParameterOverride": (
            ""
            if source_parameter_override is None
            else source_parameter_override
        ),
        "officialUnit": official_unit,
    }


def _project_entity_policy_like_legacy_runtime(ifc_entity, legacy):
    legacy = {} if legacy is None else legacy
    _assert_optional_presence_for_stable_key(
        legacy,
        ifc_entity,
        VERIFIED_POLICY_FIELD_PRESENCE,
    )
    evidence = (
        legacy["officialObjectMappingEvidence"]
        if "officialObjectMappingEvidence" in legacy
        else None
    )
    carrier = legacy["revitCarrier"] if "revitCarrier" in legacy else None
    write_policy = (
        legacy["writePolicy"] if "writePolicy" in legacy else None
    )
    export_verified = (
        legacy["officialExportVerified"]
        if "officialExportVerified" in legacy
        else False
    )
    return {
        "ifcEntity": ifc_entity,
        "officialObjectMappingEvidence": (
            "UNVERIFIED" if evidence is None else evidence
        ),
        "revitCarrier": "" if carrier is None else carrier,
        "writePolicy": (
            "BLOCK_PENDING_OFFICIAL_PLUGIN_CONTRACT"
            if write_policy is None
            else write_policy
        ),
        "officialExportVerified": export_verified,
    }


def test_legacy_projection_oracle_models_runtime_null_and_whitespace_semantics():
    _assert_legacy_runtime_projection_contracts()
    internal = {
        "field_key": "HBR|ProjectUnits|Length",
        "property": "Synthetic",
        "type": "string",
        "ui_group": "Synthetic",
        "source_kind": "system_generated",
        "allowed_values": None,
        "default": "   ",
    }
    assert _project_internal_field_like_legacy_runtime(internal) == {
        "fieldKey": "HBR|ProjectUnits|Length",
        "label": "Synthetic",
        "type": "string",
        "uiGroup": "Synthetic",
        "sourceKind": "system_generated",
        "allowedValues": [],
        "essential": True,
        "defaultStrategy": "STATIC",
        "defaultValue": "m",
    }

    assert _project_entity_policy_like_legacy_runtime("IfcSynthetic", None) == {
        "ifcEntity": "IfcSynthetic",
        "officialObjectMappingEvidence": "UNVERIFIED",
        "revitCarrier": "",
        "writePolicy": "BLOCK_PENDING_OFFICIAL_PLUGIN_CONTRACT",
        "officialExportVerified": False,
    }

    assert _project_official_mapping_like_legacy_runtime(
        {
            "category": None,
            "carrier": None,
            "persistenceMode": None,
            "officialSourceParameterGroup": None,
        },
        {
            "canonical": {"sharedParameterType": None},
            "official": {"sourceParameterOverride": None, "unit": None},
        },
    ) == {
        "category": "",
        "carrier": "",
        "persistenceMode": "",
        "sharedParameterType": "",
        "officialSourceParameterGroup": "",
        "sourceParameterOverride": "",
        "officialUnit": None,
    }


def test_legacy_equivalence_oracle_rejects_missing_binding_category():
    bindings = _load(OLD_BINDINGS_PATH)["bindings"]
    rules_by_id = {
        item["propertyId"]: item
        for item in _load(OLD_OFFICIAL_PATH)["properties"]
    }
    binding = dict(bindings[0])
    binding.pop("category")
    with pytest.raises((KeyError, AssertionError)):
        _project_official_mapping_like_legacy_runtime(
            binding,
            rules_by_id[binding["propertyId"]],
        )


def test_legacy_equivalence_oracle_rejects_missing_registry_allowed_values():
    internal = next(
        dict(item)
        for item in _load(OLD_STAGE01_PATH)["internal_workflow_fields"]
        if item["field_key"] == "HBR|FileIdentity|ModelFileType"
    )
    internal.pop("allowed_values")
    with pytest.raises((KeyError, AssertionError)):
        _project_internal_field_like_legacy_runtime(internal)


def test_legacy_equivalence_oracle_rejects_missing_policy_export_flag():
    policy = dict(_load(OLD_COMPATIBILITY_PATH)["entities"]["IfcProject"])
    policy.pop("officialExportVerified")
    with pytest.raises((KeyError, AssertionError)):
        _project_entity_policy_like_legacy_runtime("IfcProject", policy)


def test_legacy_evidence_presence_sets_match_verified_shapes():
    _assert_verified_legacy_evidence_presence_sets(
        _load(OLD_STAGE01_PATH),
        _load(OLD_COMPATIBILITY_PATH),
    )


def test_migrated_metadata_is_exactly_equivalent_to_legacy_resources():
    _assert_legacy_runtime_projection_contracts()
    source = _load(SOURCE_PATH)
    old_stage01 = _load(OLD_STAGE01_PATH)
    old_rules = _load(OLD_OFFICIAL_PATH)
    old_bindings = _load(OLD_BINDINGS_PATH)
    old_compatibility = _load(OLD_COMPATIBILITY_PATH)
    _assert_verified_legacy_evidence_presence_sets(
        old_stage01,
        old_compatibility,
    )

    expected_internal = {}
    for legacy in old_stage01["internal_workflow_fields"]:
        expected_internal[legacy["field_key"]] = (
            _project_internal_field_like_legacy_runtime(legacy)
        )
    actual_internal = {
        item["fieldKey"]: item
        for item in source["stage01"]["internalWorkflowFields"]
    }
    assert actual_internal == expected_internal

    identity_overrides = {
        item["sourceIdentity"]: item["effectiveIdentity"]
        for item in _load(BASELINE_PATH)["approvedIdentityOverrides"]
    }
    expected_refs = {
        (
            identity_overrides.get(legacy["field_key"], legacy["field_key"]),
            legacy["source_row"],
        ): {
            "uiGroup": legacy["ui_group"],
            "sourceKind": legacy["source_kind"],
            "writeInStage01": legacy["write_in_stage01"],
        }
        for legacy in old_stage01["mvd_fields"]
    }
    actual_refs = {
        (reference["fieldKey"], reference["sourceRow"]): {
            "uiGroup": reference["uiGroup"],
            "sourceKind": reference["sourceKind"],
            "writeInStage01": reference["writeInStage01"],
        }
        for reference in source["stage01"]["fieldRefs"]
    }
    assert actual_refs == expected_refs

    bindings_by_id = {
        item["propertyId"]: item for item in old_bindings["bindings"]
    }
    rules_by_id = {item["propertyId"]: item for item in old_rules["properties"]}
    assert set(bindings_by_id) == set(rules_by_id)
    expected_projections = {}
    for property_id, binding in bindings_by_id.items():
        legacy_rule = rules_by_id[property_id]
        expected_projections[property_id] = (
            _project_official_mapping_like_legacy_runtime(
                binding, legacy_rule
            )
        )
    actual_projections = {
        rule["propertyId"]: rule["officialPlugin"]["legacyProjection"]
        for rule in source["properties"]
        if rule["officialPlugin"]["inExtracted166"]
    }
    assert actual_projections == expected_projections
    assert sum(value["category"] == "" for value in actual_projections.values()) == 25
    assert all(
        value["sourceParameterOverride"] == ""
        for value in actual_projections.values()
    )

    expected_policies = {}
    for ifc_entity, legacy in old_compatibility["entities"].items():
        expected_policies[ifc_entity] = (
            _project_entity_policy_like_legacy_runtime(ifc_entity, legacy)
        )
    actual_policies = {
        item["ifcEntity"]: item
        for item in source["stage01"]["officialPluginCompatibility"][
            "entityPolicies"
        ]
    }
    assert actual_policies == expected_policies

    reasons = old_compatibility["stage01ProjectFieldExceptionReasons"]
    expected_exceptions = {
        field_key: {"fieldKey": field_key, "reason": reasons[field_key]}
        for field_key in old_compatibility["stage01ProjectFieldExceptions"]
    }
    actual_exceptions = {
        item["fieldKey"]: item
        for item in source["stage01"]["officialPluginCompatibility"]["exceptions"]
    }
    assert actual_exceptions == expected_exceptions


def test_stage01_defaults_essential_spatial_and_condition_contracts_equal_runtime():
    source = _load(SOURCE_PATH)
    old_stage01 = _load(OLD_STAGE01_PATH)
    provider = _compact_csharp(STAGE01_REGISTRY_PROVIDER_PATH)
    snapshot = _load(STAGE01_SNAPSHOT_PATH)
    stage01 = source["stage01"]

    assert {"spatialMappings", "defaultActiveGroup"} <= set(stage01)
    assert stage01["spatialMappings"] == [
        {
            "sourceName": "X",
            "fieldKey": "IfcProject|Pset_申报信息属性集|基点坐标X",
            "targetName": "NorthSouth",
            "unit": "m",
        },
        {
            "sourceName": "Y",
            "fieldKey": "IfcProject|Pset_申报信息属性集|基点坐标Y",
            "targetName": "EastWest",
            "unit": "m",
        },
        {
            "sourceName": "Elevation",
            "fieldKey": "IfcProject|Pset_申报信息属性集|基点高程",
            "targetName": "Elevation",
            "unit": "m",
        },
    ]

    fields = stage01["internalWorkflowFields"] + stage01["fieldRefs"]
    assert len(fields) == 114
    assert _essential_keys_from_legacy_provider() == ESSENTIAL_FIELD_KEYS
    assert {field["fieldKey"] for field in fields if field["essential"]} == (
        ESSENTIAL_FIELD_KEYS
    )
    for field in fields:
        expected = _expected_default_contract(field["fieldKey"])
        assert {
            "defaultStrategy": field["defaultStrategy"],
            "defaultValue": field["defaultValue"],
        } == expected

    legacy_effective = {}
    for field in old_stage01["internal_workflow_fields"]:
        if "default" in field and field["default"] is not None:
            if field["default"].strip():
                legacy_effective[field["field_key"]] = field["default"]
    legacy_set_if_empty = [
        (FILE_GUID_KEY, "<GUID>"),
        ("HBR|Workflow|Version", "0.5.0"),
        ("HBR|Workflow|InitializationStatus", "未初始化"),
        ("HBR|FileIdentity|ModelFileType", "总平模型"),
        ("HBR|FileIdentity|ModelScope", "项目总平面报规模型"),
        ("IfcProject|Pset_Manifest|阶段", "规划报建"),
        ("IfcProject|Pset_申报信息属性集|坐标系名称", "CGCS2000"),
        ("IfcProject|Pset_申报信息属性集|高程系名称", "1985国家高程基准"),
        ("HBR|SpatialReference|TrueNorthAngle", "0"),
        ("HBR|ProjectUnits|Length", "m"),
        ("HBR|ProjectUnits|Area", "m²"),
        ("HBR|ProjectUnits|Angle", "°"),
    ]
    for field_key, value in legacy_set_if_empty:
        if field_key not in legacy_effective or not legacy_effective[field_key].strip():
            legacy_effective[field_key] = value

    projected_effective = {}
    for field in fields:
        if field["defaultStrategy"] == "STATIC":
            projected_effective[field["fieldKey"]] = field["defaultValue"]
        elif field["defaultStrategy"] == "NEW_GUID":
            projected_effective[field["fieldKey"]] = "<GUID>"
    assert projected_effective == legacy_effective
    assert projected_effective["HBR|Workflow|Version"] == "0.1.0"
    for runtime_contract in (
        'case "NONE":',
        'case "STATIC":',
        'case "NEW_GUID":',
        'Guid.NewGuid().ToString("D")',
        "condition.DefaultActive",
        "database.Package.Stage01.DefaultActiveGroup",
    ):
        assert runtime_contract in provider

    condition_ids = [condition["key"] for condition in snapshot["conditions"]]
    assert len(condition_ids) == 14
    assert [condition["conditionId"] for condition in source["conditions"]] == (
        condition_ids
    )
    assert all(condition["defaultActive"] is False for condition in source["conditions"])
    assert "model.SetCondition(condition.ConditionId, condition.Value);" in provider
    assert stage01["defaultActiveGroup"] == "01_文件与项目身份"
    assert snapshot["defaults"]["activeGroup"] == stage01["defaultActiveGroup"]
    assert 'model.ActiveGroup = "01_文件与项目身份";' not in provider


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
        assert str(uuid.uuid5(uuid.UUID(old["idNamespace"]), legacy["canonicalKey"])) == property_id


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
        assert role["cardinality"]["max"] is None or role["cardinality"]["min"] <= role["cardinality"]["max"]


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
            f"HBR｜{pset_name}｜{rule['ifc']['property']}"
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
    identity_overrides = {
        item["sourceIdentity"]: item["effectiveIdentity"]
        for item in _load(BASELINE_PATH)["approvedIdentityOverrides"]
    }

    assert {ref["fieldKey"] for ref in refs} == {
        identity_overrides.get(field["field_key"], field["field_key"])
        for field in old_stage01["mvd_fields"]
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
    task_snapshot = _load(TASK_SNAPSHOT_PATH)
    stage01_snapshot = _load(STAGE01_SNAPSHOT_PATH)
    activation_snapshot = _load(ACTIVATION_SNAPSHOT_PATH)
    task_catalog = (
        ROOT / "src/BIMBaoGui.Stage01/TaskPlanning/TaskRuleCatalog.cs"
    ).read_text(encoding="utf-8")
    activation_catalog = (
        ROOT / "src/BIMBaoGui.Stage01/Context/RuleActivationCatalog.cs"
    ).read_text(encoding="utf-8")
    registry_provider = STAGE01_REGISTRY_PROVIDER_PATH.read_text(encoding="utf-8")

    frozen_tasks = {
        task["taskId"]: task
        for partition in task_snapshot["partitions"]
        for task in partition["rules"]
    }
    source_tasks = {task["taskId"]: task for task in source["tasks"]}
    assert len(frozen_tasks) == len(source_tasks) == 28
    assert [len(partition["rules"]) for partition in task_snapshot["partitions"]] == [
        15,
        7,
        6,
    ]
    assert set(source_tasks) == set(frozen_tasks)
    for task_id, task in source_tasks.items():
        frozen = frozen_tasks[task_id]
        assert {
            "modelFileType": task["modelFileType"],
            "taskId": task["taskId"],
            "name": task["name"],
            "objectCode": task["objectCode"],
            "requirement": {
                "REQUIRED": "Required",
                "CONDITIONAL": "Conditional",
            }[task["requirement"]],
            "conditionKey": task["conditionId"] or "",
            "sequence": task["sequence"],
            "skeletonTask": task["skeletonTask"],
        } == {
            key: frozen[key]
            for key in (
                "modelFileType",
                "taskId",
                "name",
                "objectCode",
                "requirement",
                "conditionKey",
                "sequence",
                "skeletonTask",
            )
        }
        for collection in (
            "attributeRequirements",
            "dependencies",
            "geometryChecks",
            "propertyChecks",
            "targetComparisons",
        ):
            assert sorted(task[collection]) == sorted(frozen[collection])

    frozen_conditions = {
        condition["key"]: condition for condition in stage01_snapshot["conditions"]
    }
    source_conditions = {
        condition["conditionId"]: condition for condition in source["conditions"]
    }
    assert len(frozen_conditions) == len(source_conditions) == 14
    assert {
        condition_id: {
            "key": condition_id,
            "label": condition["displayName"],
            "group": condition["group"],
        }
        for condition_id, condition in source_conditions.items()
    } == frozen_conditions
    assert {
        condition["key"]: condition["value"]
        for condition in stage01_snapshot["defaults"]["conditions"]
    } == {
        condition_id: condition["defaultActive"]
        for condition_id, condition in source_conditions.items()
    }
    assert all(task["source"] == "TaskRuleCatalog.cs" for task in source["tasks"])
    assert {condition["source"] for condition in source["conditions"]} <= {
        "RuleActivationCatalog.cs", "Stage01RegistryProvider.cs"
    }
    activation_expected = {
        rule["conditionKey"]: rule["activationRuleId"]
        for rule in activation_snapshot["conditionRules"]
    }
    activation_actual = {
        condition["conditionId"]: condition["activationRuleId"]
        for condition in source["conditions"]
        if condition["activationRuleId"] is not None
    }
    assert activation_actual == activation_expected

    none_cases = {
        case["modelFileType"]: case
        for case in activation_snapshot["cases"]
        if case["state"] == "none"
    }
    for profile in source["modelProfiles"]:
        assert none_cases[profile["profileId"]]["activated"] == sorted(
            profile["activationRuleIds"]
        )

    for contract in (
        "HbrRuleDatabase.Current",
        "database.Package.Tasks",
        ".Select(MapRule)",
        'case "REQUIRED":',
        'case "CONDITIONAL":',
    ):
        assert contract in task_catalog
    for contract in (
        "HbrRuleDatabase.Current",
        "database.Package.Conditions",
        "database.Package.ModelProfiles",
    ):
        assert contract in activation_catalog
    assert "database.Package.Conditions" in registry_provider

    combined_catalogs = task_catalog + activation_catalog + registry_provider
    for legacy_resource in (
        "stage01_file_initialization_registry_v0.1.json",
        "GetManifestResourceStream",
        "ReadEmbeddedText",
    ):
        assert legacy_resource not in combined_catalogs
    assert all(f'"{task_id}"' not in task_catalog for task_id in source_tasks)
    assert all(
        f'"{condition_id}"' not in activation_catalog + registry_provider
        for condition_id in source_conditions
    )


def test_mvd_source_evidence_and_canonical_fields_remain_workbook_faithful():
    source = _load(SOURCE_PATH)
    mvd = [rule for rule in source["properties"] if rule["contractKind"] == "MVD"]
    for rule in mvd:
        raw = rule["source"]
        assert {"rawProperty", "rawPropertySetId", "rawPropertySetName"} <= set(raw)
        assert rule["ifc"]["entity"] == raw["rawEntityId"]
        assert rule["ifc"]["propertySet"] == raw["rawPropertySetId"]
        if rule["ifc"]["property"] != raw["rawProperty"]:
            assert raw["rawProperty"] in rule["suggestion"]["aliases"]
        expected_source_unit = None if raw["rawUnit"] in {"", "14"} else raw["rawUnit"]
        accepted_source_units = {expected_source_unit}
        legacy_projection = rule["officialPlugin"].get("legacyProjection")
        if expected_source_unit is None and legacy_projection is not None:
            official_unit = legacy_projection.get("officialUnit")
            if official_unit:
                accepted_source_units.add(official_unit)
        assert rule["ifc"]["sourceUnit"] in accepted_source_units
    by_row = {rule["source"]["row"]: rule for rule in mvd}
    assert by_row[47]["ifc"]["property"] == "基点坐标X"
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


def test_task_targets_and_complete_payload_are_frozen():
    source = _load(SOURCE_PATH)
    tasks = {task["taskId"]: task for task in source["tasks"]}
    density = "planning.building_density"
    far = "planning.floor_area_ratio"
    green = "planning.green_rate"
    expected = {
        "SITE.BUILDING_FOOTPRINT": [density], "SITE.GREEN": [green],
        "SITE.TARGET_CHECK": [density, far, green], "ABOVE.BODY": [far],
        "ABOVE.INHERIT_TARGETS": [density, far, green],
        "UNDERGROUND.INHERIT_TARGETS": [density, far, green],
    }
    assert {key: tasks[key]["targetComparisons"] for key in expected} == expected
    normalized = json.dumps(source["tasks"], ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    assert hashlib.sha256(normalized.encode()).hexdigest() == "850a1a6007e34bf1ef0827221cd08d5fd6c25843a3c226381f24e85677357923"


def test_mvd_raw_blanks_are_real_empty_cells_not_style_sentinel_values():
    source = _load(SOURCE_PATH)
    mvd = [rule for rule in source["properties"] if rule["contractKind"] == "MVD"]
    for key in ("rawValueKind", "rawUnit", "rawIfcElementOrType"):
        assert all(rule["source"][key] != "14" for rule in mvd)
    assert sum(rule["source"]["rawValueKind"] == "" for rule in mvd) == 113
    assert sum(rule["source"]["rawUnit"] == "" for rule in mvd) == 272
    assert sum(rule["source"]["rawIfcElementOrType"] == "" for rule in mvd) == 93
    workbook = source["evidenceSources"][0]
    assert workbook["source"] == "《MVD》规划报建.xlsx"
    assert re.fullmatch(r"[0-9a-f]{64}", workbook["sha256"])


def test_stage01_xy_keep_blank_workbook_evidence_but_use_meter_length_contract():
    source = _load(SOURCE_PATH)
    xy = [
        rule
        for rule in source["properties"]
        if rule["propertyId"]
        in {
            "6b407894-09d4-529a-9f9f-a031219cdeaa",
            "1a64ef8d-e97c-5fa1-b53f-52b969b6198a",
        }
    ]
    assert len(xy) == 2
    for rule in xy:
        assert rule["source"]["rawUnit"] == ""
        assert rule["ifc"]["sourceUnit"] == "m"
        assert rule["ifc"]["canonicalUnit"] == "m"
        assert rule["revit"]["parameterType"] == "Length"
def test_reference_collections_are_unique_and_task_dependencies_form_profile_dags():
    source = _load(SOURCE_PATH)
    for collection, key in (("carrierRoles","roleId"),("conditions","conditionId"),("tasks","taskId"),("modelProfiles","profileId")):
        values = [item[key] for item in source[collection]]
        assert len(values) == len(set(values))
    aliases = [(item["propertyId"], item["alias"]) for item in source["legacyAliases"]]
    assert len(aliases) == len(set(aliases)) == 166
    tasks = {item["taskId"]: item for item in source["tasks"]}
    for profile in source["modelProfiles"]:
        assert len(profile["taskIds"]) == len(set(profile["taskIds"]))
        ids = set(profile["taskIds"])
        assert all(dep in ids for task_id in ids for dep in tasks[task_id]["dependencies"])
        visiting, visited = set(), set()
        def visit(task_id):
            assert task_id not in visiting
            if task_id in visited: return
            visiting.add(task_id)
            for dep in tasks[task_id]["dependencies"]: visit(dep)
            visiting.remove(task_id); visited.add(task_id)
        for task_id in ids: visit(task_id)
    refs = [(r["fieldKey"], r["sourceRow"], r["propertyId"]) for r in source["stage01"]["fieldRefs"]]
    assert len(refs) == len(set(refs)) == 102
    frozen = {
        "carrierRoles": "6f2c90a21b46b26ae82289766c1712f386d7a3432cc2fa6beba8f11f6d829d91",
        "conditions": "26a810386985cd144f15dc9dfae610c1af7f63c1fb927acf30399d2a103f81b5",
        "modelProfiles": "9a00bb19f642bf5ad98e39e589873b2d422378cf5e838b02e63cbd35cbef5b05",
        "legacyAliases": "1a18f522e13b6072d12b52e644e165e9bdf10283daf7b525a2ca02578b3b5a80",
    }
    for key, digest in frozen.items():
        payload = json.dumps(source[key], ensure_ascii=False, sort_keys=True, separators=(",", ":"))
        assert hashlib.sha256(payload.encode()).hexdigest() == digest
    stage_payload = json.dumps(sorted(refs), ensure_ascii=False, separators=(",", ":"))
    assert hashlib.sha256(stage_payload.encode()).hexdigest() == "24bdb2fc2683634e47a65b9e2256cdec7ce2be0e96ea73529a52873d4f4bdcaf"


def test_real_runtime_and_revit_types_follow_all_supported_dimensions():
    source = _load(SOURCE_PATH)
    expected = {"m":"IfcLengthMeasure","mm":"IfcLengthMeasure","m2":"IfcAreaMeasure","m3":"IfcVolumeMeasure","deg":"IfcPlaneAngleMeasure"}
    mm_rules = [rule for rule in source["properties"] if rule["ifc"]["declaredType"] == "IfcReal" and rule["ifc"]["canonicalUnit"] == "mm"]
    assert len(mm_rules) == 15
    for rule in source["properties"]:
        if rule["ifc"]["declaredType"] != "IfcReal" or rule["ifc"]["canonicalUnit"] not in expected: continue
        assert {"IfcReal", expected[rule["ifc"]["canonicalUnit"]]} <= set(rule["ifc"]["allowedRuntimeTypes"])
        assert rule["revit"]["parameterType"] == {"m":"Length","mm":"Length","m2":"Area","m3":"Volume","deg":"Angle"}[rule["ifc"]["canonicalUnit"]]


EXPECTED_OWNER_STATUS = {
    "SINGLE_ENTITY_BY_TYPE": "SUPPORTED",
    "BY_EXPORT_GUID": "SUPPORTED",
    "CANONICAL_SPATIAL_ZONE_RECORD": "NOT_IMPLEMENTED",
    "USER_SELECTED_EXPORTABLE_GENERIC_MODEL": "NOT_IMPLEMENTED",
}

EXPECTED_REQUIREMENT_STATUS = {
    "REQUIRED": "SUPPORTED",
    "CONDITIONAL": "SUPPORTED",
    "OPTIONAL": "SUPPORTED",
    "NOT_APPLICABLE": "SUPPORTED",
    "UNCLASSIFIED": "UNCLASSIFIED_REQUIREMENT",
}


def test_runtime_support_policy_resolves_all_359_rules_without_fallback():
    from tools.build_hbr_rulepack import effective_runtime_status

    source = _load(SOURCE_PATH)
    assert {
        item["ownerStrategy"]: item["status"]
        for item in source["runtimeSupport"]["ownerStrategies"]
    } == EXPECTED_OWNER_STATUS
    assert {
        item["level"]: item["status"]
        for item in source["runtimeSupport"]["requirementLevels"]
    } == EXPECTED_REQUIREMENT_STATUS
    statuses = [effective_runtime_status(source, rule) for rule in source["properties"]]
    assert len(statuses) == 359
    assert statuses.count("NOT_IMPLEMENTED") == 57
    assert statuses.count("UNCLASSIFIED_REQUIREMENT") == 302
