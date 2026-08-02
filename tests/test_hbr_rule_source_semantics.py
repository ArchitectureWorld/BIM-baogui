import json
import re
import uuid
import hashlib
from pathlib import Path

import pytest


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
OLD_BINDINGS_PATH = (
    ROOT / "specs/hifc-mapping/v1/generated/GH_HIFC_ParameterBindings.json"
)
OLD_COMPATIBILITY_PATH = (
    ROOT
    / "specs/hifc-mapping/v1/data/official_plugin_compatibility_status.v1.json"
)
RULE_ACTIVATION_CATALOG_PATH = (
    ROOT / "src/BIMBaoGui.Stage01/Context/RuleActivationCatalog.cs"
)
PLANNING_TARGET_CATALOG_PATH = (
    ROOT / "src/BIMBaoGui.Stage01/Core/PlanningTargetCatalog.cs"
)
STAGE01_REGISTRY_PROVIDER_PATH = (
    ROOT / "src/BIMBaoGui.Stage01/Infrastructure/Stage01RegistryProvider.cs"
)
OFFICIAL_MAPPING_CATALOG_PATH = (
    ROOT / "src/BIMBaoGui.Stage01/Hifc/OfficialHifcMappingCatalog.cs"
)
OFFICIAL_COMPATIBILITY_CATALOG_PATH = (
    ROOT / "src/BIMBaoGui.Stage01/Hifc/OfficialPluginCompatibilityCatalog.cs"
)
PLANNING_TARGET_POLICY_PATH = (
    ROOT / "src/BIMBaoGui.Stage01/Core/PlanningTargetRequirementPolicy.cs"
)

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


def _compact_csharp_text(source):
    return " ".join(source.split())


def _compact_csharp(path):
    return _compact_csharp_text(path.read_text(encoding="utf-8"))


def _assert_legacy_runtime_projection_contracts(
    activation_catalog=None,
    policy_catalog=None,
):
    registry = _compact_csharp(STAGE01_REGISTRY_PROVIDER_PATH)
    assert (
        "AllowedValues = source.allowed_values ?? Array.Empty<string>()"
        in registry
    )
    assert (
        "if (!string.IsNullOrWhiteSpace(source.@default)) "
        "defaults[source.field_key] = source.@default;"
        in registry
    )

    mapping = _compact_csharp(OFFICIAL_MAPPING_CATALOG_PATH)
    for contract in (
        "Category = (item.category ?? string.Empty).Trim(),",
        "Carrier = item.carrier ?? string.Empty,",
        "PersistenceMode = item.persistenceMode ?? string.Empty,",
        "SharedParameterType = rule.canonical.sharedParameterType ?? string.Empty,",
        "string sourceOverride = rule.official.sourceParameterOverride ?? string.Empty;",
        "SourceParameterOverride = sourceOverride,",
        "OfficialSourceParameterGroup = "
        "(item.officialSourceParameterGroup ?? string.Empty).Trim(),",
    ):
        assert contract in mapping

    compatibility = _compact_csharp(OFFICIAL_COMPATIBILITY_CATALOG_PATH)
    for contract in (
        "EntityPolicyRecord source = item.Value ?? new EntityPolicyRecord();",
        "OfficialObjectMappingEvidence = "
        'source.officialObjectMappingEvidence ?? "UNVERIFIED",',
        "RevitCarrier = source.revitCarrier ?? string.Empty,",
        "WritePolicy = source.writePolicy ?? "
        '"BLOCK_PENDING_OFFICIAL_PLUGIN_CONTRACT",',
        "OfficialExportVerified = source.officialExportVerified",
    ):
        assert contract in compatibility

    policy = (
        _compact_csharp(PLANNING_TARGET_POLICY_PATH)
        if policy_catalog is None
        else _compact_csharp_text(policy_catalog)
    )
    for contract in (
        "if (PlanningTargetCatalog.Get(metricCode) == null) "
        "return PlanningTargetRequirement.NotApplicable;",
        "if (string.Equals(modelFileType, SiteModel, StringComparison.Ordinal)) "
        "return PlanningTargetRequirement.Required;",
        "if (string.Equals(modelFileType, AboveGroundModel, StringComparison.Ordinal) "
        "|| string.Equals(modelFileType, UndergroundModel, StringComparison.Ordinal)) "
        "return PlanningTargetRequirement.Inherited;",
    ):
        assert contract in policy

    activation = (
        _compact_csharp(RULE_ACTIVATION_CATALOG_PATH)
        if activation_catalog is None
        else _compact_csharp_text(activation_catalog)
    )
    for contract in (
        "Activated = (activated ?? Array.Empty<string>())"
        ".Distinct(StringComparer.Ordinal)"
        ".OrderBy(x => x, StringComparer.Ordinal).ToArray();",
        "NotApplicable = (notApplicable ?? Array.Empty<string>())"
        ".Distinct(StringComparer.Ordinal)"
        ".OrderBy(x => x, StringComparer.Ordinal).ToArray();",
        "if (applies) activated.Add(rule.Value); "
        "else notApplicable.Add(rule.Value);",
        "PlanningTargetRequirement requirement = "
        "PlanningTargetRequirementPolicy.GetRequirement("
        "modelFileType, definition.MetricCode);",
        'string ruleId = "HBR.TARGET." + '
        'definition.MetricCode.Substring("planning.".Length).ToUpperInvariant();',
        "if (requirement == PlanningTargetRequirement.NotApplicable) "
        "notApplicable.Add(ruleId); else activated.Add(ruleId);",
    ):
        assert contract in activation


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
    return {
        "fieldKey": field_key,
        "label": legacy["property"],
        "type": legacy["type"],
        "uiGroup": legacy["ui_group"],
        "sourceKind": legacy["source_kind"],
        "allowedValues": [] if allowed_values is None else allowed_values,
        "defaultValue": default_value,
    }


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
        "defaultValue": None,
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
            "official": {"sourceParameterOverride": None},
        },
    ) == {
        "category": "",
        "carrier": "",
        "persistenceMode": "",
        "sharedParameterType": "",
        "officialSourceParameterGroup": "",
        "sourceParameterOverride": "",
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


def test_legacy_activation_oracle_is_extracted_from_current_csharp_structure():
    expected = {
        profile["profileId"]: profile["activationRuleIds"]
        for profile in _load(SOURCE_PATH)["modelProfiles"]
    }
    assert _legacy_fixed_activation_rule_ids(
        activation_catalog=RULE_ACTIVATION_CATALOG_PATH.read_text(
            encoding="utf-8"
        ),
        policy_catalog=PLANNING_TARGET_POLICY_PATH.read_text(
            encoding="utf-8"
        ),
        planning_catalog=PLANNING_TARGET_CATALOG_PATH.read_text(
            encoding="utf-8"
        ),
        expected=expected,
    ) == expected


def test_legacy_activation_oracle_rejects_csharp_structure_drift():
    expected = {
        profile["profileId"]: profile["activationRuleIds"]
        for profile in _load(SOURCE_PATH)["modelProfiles"]
    }
    activation_catalog = RULE_ACTIVATION_CATALOG_PATH.read_text(
        encoding="utf-8"
    )
    policy_catalog = PLANNING_TARGET_POLICY_PATH.read_text(encoding="utf-8")
    planning_catalog = PLANNING_TARGET_CATALOG_PATH.read_text(
        encoding="utf-8"
    )
    mutations = (
        {
            "activation_catalog": activation_catalog.replace(
                '        activated.Add("HBR.SITE.BASE");\n',
                "",
                1,
            ),
            "policy_catalog": policy_catalog,
            "planning_catalog": planning_catalog,
        },
        {
            "activation_catalog": activation_catalog,
            "policy_catalog": policy_catalog.replace(
                '    public const string SiteModel = "总平模型";\n',
                "",
                1,
            ),
            "planning_catalog": planning_catalog,
        },
        {
            "activation_catalog": activation_catalog,
            "policy_catalog": policy_catalog,
            "planning_catalog": planning_catalog.replace(
                '    public const string GreenRateCode = '
                '"planning.green_rate";\n',
                "",
                1,
            ),
        },
    )
    assert all(
        mutation["activation_catalog"] != activation_catalog
        or mutation["policy_catalog"] != policy_catalog
        or mutation["planning_catalog"] != planning_catalog
        for mutation in mutations
    )
    for mutation in mutations:
        with pytest.raises(AssertionError):
            _legacy_fixed_activation_rule_ids(
                **mutation,
                expected=expected,
            )


@pytest.mark.parametrize(
    ("legacy_contract", "drifted_contract"),
    (
        (
            ".OrderBy(x => x, StringComparer.Ordinal)",
            ".OrderByDescending(x => x, StringComparer.Ordinal)",
        ),
        (".ToUpperInvariant()", ".ToLowerInvariant()"),
        (
            "else notApplicable.Add(rule.Value);",
            "else activated.Add(rule.Value);",
        ),
    ),
)
def test_legacy_activation_oracle_rejects_runtime_projection_drift(
    legacy_contract,
    drifted_contract,
):
    expected = {
        profile["profileId"]: profile["activationRuleIds"]
        for profile in _load(SOURCE_PATH)["modelProfiles"]
    }
    activation_catalog = RULE_ACTIVATION_CATALOG_PATH.read_text(
        encoding="utf-8"
    )
    drifted_activation_catalog = activation_catalog.replace(
        legacy_contract,
        drifted_contract,
    )
    assert drifted_activation_catalog != activation_catalog

    with pytest.raises(AssertionError):
        _legacy_fixed_activation_rule_ids(
            activation_catalog=drifted_activation_catalog,
            policy_catalog=PLANNING_TARGET_POLICY_PATH.read_text(
                encoding="utf-8"
            ),
            planning_catalog=PLANNING_TARGET_CATALOG_PATH.read_text(
                encoding="utf-8"
            ),
            expected=expected,
        )


def _legacy_fixed_activation_rule_ids(
    expected,
    activation_catalog=None,
    policy_catalog=None,
    planning_catalog=None,
):
    if activation_catalog is None:
        activation_catalog = RULE_ACTIVATION_CATALOG_PATH.read_text(
            encoding="utf-8"
        )
    if policy_catalog is None:
        policy_catalog = PLANNING_TARGET_POLICY_PATH.read_text(
            encoding="utf-8"
        )
    if planning_catalog is None:
        planning_catalog = PLANNING_TARGET_CATALOG_PATH.read_text(
            encoding="utf-8"
        )
    _assert_legacy_runtime_projection_contracts(
        activation_catalog=activation_catalog,
        policy_catalog=policy_catalog,
    )

    model_constant_matches = re.findall(
        r'^\s*public const string (\w+Model) = "([^"]+)";\s*$',
        policy_catalog,
        flags=re.MULTILINE,
    )
    model_constants = dict(model_constant_matches)
    assert len(model_constant_matches) == len(model_constants) == 3
    assert set(model_constants) == {
        "SiteModel",
        "AboveGroundModel",
        "UndergroundModel",
    }
    assert len(set(model_constants.values())) == 3

    branch_pattern = re.compile(
        r'(?:if|else\s+if)\s*\(\s*string\.Equals\('
        r'\s*modelFileType,\s*PlanningTargetRequirementPolicy\.(\w+),'
        r'\s*StringComparison\.Ordinal\s*\)\s*\)\s*\{(.*?)\}',
        flags=re.DOTALL,
    )
    branch_matches = branch_pattern.findall(activation_catalog)
    assert len(branch_matches) == 3
    assert {constant for constant, _ in branch_matches} == set(
        model_constants
    )
    add_pattern = re.compile(r'activated\.Add\("([^"]+)"\);')
    fixed_ids_by_constant = {}
    for constant, body in branch_matches:
        fixed_ids = add_pattern.findall(body)
        assert fixed_ids
        assert len(fixed_ids) == len(set(fixed_ids))
        assert not add_pattern.sub("", body).strip()
        fixed_ids_by_constant[constant] = set(fixed_ids)

    metric_constant_matches = re.findall(
        r'^\s*public const string (\w+Code) = "(planning\.[a-z0-9_]+)";\s*$',
        planning_catalog,
        flags=re.MULTILINE,
    )
    metric_constants = dict(metric_constant_matches)
    assert len(metric_constant_matches) == len(metric_constants) == 3
    assert set(metric_constants) == {
        "BuildingDensityCode",
        "FloorAreaRatioCode",
        "GreenRateCode",
    }
    definition_constants = re.findall(
        r'new PlanningTargetDefinition\(\s*(\w+Code),',
        planning_catalog,
    )
    assert len(definition_constants) == 3
    assert set(definition_constants) == set(metric_constants)
    target_ids = {
        "HBR.TARGET." + metric[len("planning.") :].upper()
        for metric in metric_constants.values()
    }

    site_requirement = re.search(
        r'if\s*\(\s*string\.Equals\(modelFileType,\s*SiteModel,'
        r'\s*StringComparison\.Ordinal\)\s*\)\s*'
        r'return PlanningTargetRequirement\.(\w+);',
        policy_catalog,
    )
    inherited_requirement = re.search(
        r'if\s*\(\s*string\.Equals\(modelFileType,\s*AboveGroundModel,'
        r'\s*StringComparison\.Ordinal\)\s*\|\|\s*'
        r'string\.Equals\(modelFileType,\s*UndergroundModel,'
        r'\s*StringComparison\.Ordinal\)\s*\)\s*'
        r'return PlanningTargetRequirement\.(\w+);',
        policy_catalog,
    )
    assert site_requirement is not None
    assert inherited_requirement is not None
    target_requirement_by_constant = {
        "SiteModel": site_requirement.group(1),
        "AboveGroundModel": inherited_requirement.group(1),
        "UndergroundModel": inherited_requirement.group(1),
    }
    assert target_requirement_by_constant == {
        "SiteModel": "Required",
        "AboveGroundModel": "Inherited",
        "UndergroundModel": "Inherited",
    }

    result = {}
    for constant, profile_id in model_constants.items():
        applicable_target_ids = (
            set()
            if target_requirement_by_constant[constant]
            == "NotApplicable"
            else target_ids
        )
        result[profile_id] = sorted(
            fixed_ids_by_constant[constant] | applicable_target_ids
        )
    assert result == expected
    return result


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

    expected_refs = {
        (legacy["field_key"], legacy["source_row"]): {
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

    actual_activation = {
        profile["profileId"]: profile["activationRuleIds"]
        for profile in source["modelProfiles"]
    }
    expected_activation = _legacy_fixed_activation_rule_ids(
        expected=actual_activation
    )
    assert actual_activation == expected_activation


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
    activation_expected = dict(re.findall(r'\["([^"]+)"\]\s*=\s*"([^"]+)"', activation_catalog))
    activation_actual = {c["conditionId"]: c["activationRuleId"] for c in source["conditions"] if c["activationRuleId"] is not None}
    assert activation_actual == activation_expected


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
        "conditions": "5941f38c19608314006dafdfa82744afad65bce50391edaee0b91f9b158b26bd",
        "modelProfiles": "9a00bb19f642bf5ad98e39e589873b2d422378cf5e838b02e63cbd35cbef5b05",
        "legacyAliases": "1a18f522e13b6072d12b52e644e165e9bdf10283daf7b525a2ca02578b3b5a80",
    }
    for key, digest in frozen.items():
        payload = json.dumps(source[key], ensure_ascii=False, sort_keys=True, separators=(",", ":"))
        assert hashlib.sha256(payload.encode()).hexdigest() == digest
    stage_payload = json.dumps(sorted(refs), ensure_ascii=False, separators=(",", ":"))
    assert hashlib.sha256(stage_payload.encode()).hexdigest() == "9c1ea357d65558736ecc6d2f43bf15cf6d8e64f2491e63bf8f0d18b0e75264fb"


def test_real_runtime_and_revit_types_follow_all_supported_dimensions():
    source = _load(SOURCE_PATH)
    expected = {"m":"IfcLengthMeasure","mm":"IfcLengthMeasure","m2":"IfcAreaMeasure","m3":"IfcVolumeMeasure","deg":"IfcPlaneAngleMeasure"}
    mm_rules = [rule for rule in source["properties"] if rule["ifc"]["declaredType"] == "IfcReal" and rule["ifc"]["canonicalUnit"] == "mm"]
    assert len(mm_rules) == 15
    for rule in source["properties"]:
        if rule["ifc"]["declaredType"] != "IfcReal" or rule["ifc"]["canonicalUnit"] not in expected: continue
        assert {"IfcReal", expected[rule["ifc"]["canonicalUnit"]]} <= set(rule["ifc"]["allowedRuntimeTypes"])
        assert rule["revit"]["parameterType"] == {"m":"Length","mm":"Length","m2":"Area","m3":"Volume","deg":"Angle"}[rule["ifc"]["canonicalUnit"]]
