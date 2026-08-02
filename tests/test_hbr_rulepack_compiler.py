import copy
import hashlib
import json
import os
import re
import struct
import subprocess
import sys
import time
import uuid
import xml.etree.ElementTree as ET
from pathlib import Path

import pytest


ROOT = Path(__file__).resolve().parents[1]
SOURCE_PATH = ROOT / "specs/hbr-rules/v1/source/hbr_rule_source.v1.json"
LEGACY_CATALOG_PATH = (
    ROOT / "specs/hifc-mapping/v1/data/wuhan_planning_rules.v1.json"
)
BASELINE_PATH = (
    ROOT
    / "specs/hbr-rules/v1/compatibility/hbr_rule_compatibility_baseline.v1.json"
)
COMPILER_PATH = ROOT / "tools/build_hbr_rulepack.py"
STAGE01_PROJECT_PATH = ROOT / "src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj"


def _load_source():
    return json.loads(SOURCE_PATH.read_text(encoding="utf-8"))


def _load_baseline():
    return json.loads(BASELINE_PATH.read_text(encoding="utf-8"))


def _write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False), encoding="utf-8")


def _replace_exact_string_values(value, old, new):
    if isinstance(value, dict):
        for key, item in value.items():
            if item == old:
                value[key] = new
            else:
                _replace_exact_string_values(item, old, new)
    elif isinstance(value, list):
        for index, item in enumerate(value):
            if item == old:
                value[index] = new
            else:
                _replace_exact_string_values(item, old, new)


LEGACY_METADATA_DIGEST_NAMES = (
    "internalWorkflowFields",
    "stage01FieldMetadata",
    "officialLegacyProjection",
    "entityPolicies",
    "exceptions",
    "profileActivationRuleIds",
)


def _canonical_sha256(value):
    payload = json.dumps(
        value,
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
    ).encode("utf-8")
    return hashlib.sha256(payload).hexdigest()


def _legacy_metadata_projections(source):
    stage01 = source["stage01"]
    official_projection_fields = (
        "category",
        "carrier",
        "persistenceMode",
        "sharedParameterType",
        "officialSourceParameterGroup",
        "sourceParameterOverride",
    )
    return {
        "internalWorkflowFields": sorted(
            (dict(item) for item in stage01["internalWorkflowFields"]),
            key=lambda item: item["fieldKey"],
        ),
        "stage01FieldMetadata": sorted(
            (
                {
                    key: item[key]
                    for key in (
                        "fieldKey",
                        "sourceRow",
                        "uiGroup",
                        "sourceKind",
                        "writeInStage01",
                    )
                }
                for item in stage01["fieldRefs"]
            ),
            key=lambda item: (item["fieldKey"], item["sourceRow"]),
        ),
        "officialLegacyProjection": sorted(
            (
                {
                    "propertyId": rule["propertyId"],
                    **{
                        key: rule["officialPlugin"]["legacyProjection"][key]
                        for key in official_projection_fields
                    },
                }
                for rule in source["properties"]
                if rule["officialPlugin"]["inExtracted166"]
            ),
            key=lambda item: item["propertyId"],
        ),
        "entityPolicies": sorted(
            (
                dict(item)
                for item in stage01["officialPluginCompatibility"][
                    "entityPolicies"
                ]
            ),
            key=lambda item: item["ifcEntity"],
        ),
        "exceptions": sorted(
            (
                dict(item)
                for item in stage01["officialPluginCompatibility"]["exceptions"]
            ),
            key=lambda item: item["fieldKey"],
        ),
        "profileActivationRuleIds": sorted(
            (
                {
                    "profileId": profile["profileId"],
                    "activationRuleIds": profile["activationRuleIds"],
                }
                for profile in source["modelProfiles"]
            ),
            key=lambda item: item["profileId"],
        ),
    }


def test_compatibility_baseline_freezes_only_verified_published_identity_fields():
    baseline = json.loads(BASELINE_PATH.read_text(encoding="utf-8"))
    legacy = json.loads(LEGACY_CATALOG_PATH.read_text(encoding="utf-8"))
    source = _load_source()

    assert set(baseline) == {
        "schemaVersion",
        "baselineId",
        "baselineVersion",
        "workbookEvidence",
        "officialProperties",
        "legacyMetadataDigests",
    }
    assert baseline["schemaVersion"] == "1.1.0"
    assert baseline["baselineVersion"] == "1.1.0"
    assert baseline["workbookEvidence"] == {
        "logicalSource": "《MVD》规划报建.xlsx",
        "sha256": "63fac01de41f3bd149e4e857a81256e623382bbe9b3437ed69a2b5ace90628e4",
    }

    expected_from_legacy = [
        {
            "propertyId": item["propertyId"],
            "canonicalKey": item["canonicalKey"],
            "parameterGuid": item["canonical"]["revitParameterGuid"],
            "originalIdentity": "|".join(
                (
                    item["official"]["ifcEntity"],
                    (
                        item["official"]["propertySet"]
                        if item["official"]["propertySet"].startswith("Pset_")
                        else f"Pset_{item['official']['propertySet']}"
                    ),
                    item["official"]["ifcProperty"],
                )
            ),
        }
        for item in legacy["properties"]
    ]
    assert len(expected_from_legacy) == 166
    assert baseline["officialProperties"] == expected_from_legacy
    assert all(
        set(item)
        == {"propertyId", "canonicalKey", "parameterGuid", "originalIdentity"}
        for item in baseline["officialProperties"]
    )

    expected_from_source = [
        {
            "propertyId": item["propertyId"],
            "canonicalKey": item["canonicalKey"],
            "parameterGuid": item["revit"]["parameterGuid"],
            "originalIdentity": item["officialPlugin"]["originalIdentity"],
        }
        for item in source["properties"]
        if item["officialPlugin"]["inExtracted166"]
    ]
    assert len(expected_from_source) == 166
    assert {
        item["propertyId"]: item for item in baseline["officialProperties"]
    } == {item["propertyId"]: item for item in expected_from_source}

    projections = _legacy_metadata_projections(source)
    assert set(projections) == set(LEGACY_METADATA_DIGEST_NAMES)
    assert baseline["legacyMetadataDigests"] == {
        name: _canonical_sha256(projections[name])
        for name in LEGACY_METADATA_DIGEST_NAMES
    }
    assert all(
        re.fullmatch(r"[0-9a-f]{64}", digest)
        for digest in baseline["legacyMetadataDigests"].values()
    )


def test_compiler_rejects_published_original_identity_drift(tmp_path):
    from tools.build_hbr_rulepack import compile_rulepack, validate_semantics

    source = _load_source()
    official = next(
        item for item in source["properties"] if item["officialPlugin"]["inExtracted166"]
    )
    official["officialPlugin"]["originalIdentity"] += "|DRIFT"
    validate_semantics(source)

    mutated_source = tmp_path / "identity drift.json"
    _write_json(mutated_source, source)
    output = tmp_path / "identity drift.hbrpack"

    with pytest.raises(
        ValueError,
        match=r"originalIdentity.*compatibility baseline",
    ):
        compile_rulepack(mutated_source, output, BASELINE_PATH)

    assert not output.exists()


def test_compiler_rejects_internally_consistent_published_id_drift(tmp_path):
    from tools.build_hbr_rulepack import compile_rulepack, validate_semantics

    source = _load_source()
    official = next(
        item for item in source["properties"] if item["officialPlugin"]["inExtracted166"]
    )
    old_id = official["propertyId"]
    official["canonicalKey"] += "|COMPATIBILITY_DRIFT"
    new_id = str(
        uuid.uuid5(uuid.UUID(source["guidNamespace"]), official["canonicalKey"])
    )
    _replace_exact_string_values(source, old_id, new_id)
    validate_semantics(source)

    mutated_source = tmp_path / "id drift.json"
    _write_json(mutated_source, source)
    output = tmp_path / "id drift.hbrpack"

    with pytest.raises(
        ValueError,
        match=r"propertyId.*compatibility baseline",
    ):
        compile_rulepack(mutated_source, output, BASELINE_PATH)

    assert not output.exists()


def test_compiler_rejects_workbook_digest_drift(tmp_path):
    from tools.build_hbr_rulepack import compile_rulepack, validate_semantics

    source = _load_source()
    workbook = next(item for item in source["evidenceSources"] if "sha256" in item)
    workbook["sha256"] = "0" * 64
    validate_semantics(source)

    mutated_source = tmp_path / "workbook drift.json"
    _write_json(mutated_source, source)
    output = tmp_path / "workbook drift.hbrpack"

    with pytest.raises(
        ValueError,
        match=r"workbook.*sha256.*compatibility baseline",
    ):
        compile_rulepack(mutated_source, output, BASELINE_PATH)

    assert not output.exists()


def test_compiler_allows_valid_non_frozen_business_field_updates(tmp_path):
    from tools.build_hbr_rulepack import compile_rulepack, validate_semantics

    source = _load_source()
    official = next(
        item for item in source["properties"] if item["officialPlugin"]["inExtracted166"]
    )
    official["suggestion"]["aliases"].append("兼容基线允许的业务别名")
    validate_semantics(source)

    mutated_source = tmp_path / "business update.json"
    _write_json(mutated_source, source)
    output = tmp_path / "business update.hbrpack"

    compile_rulepack(mutated_source, output, BASELINE_PATH)

    assert json.loads(output.read_bytes()[48:].decode("utf-8")) == source


@pytest.mark.parametrize(
    ("mutation", "message"),
    [
        (
            lambda baseline: baseline.update({"unexpected": True}),
            "unexpected fields",
        ),
        (
            lambda baseline: baseline["officialProperties"].pop(),
            "exactly 166",
        ),
        (
            lambda baseline: baseline["officialProperties"][0].update(
                {"unexpected": True}
            ),
            "unexpected fields",
        ),
        (
            lambda baseline: baseline["officialProperties"][1].update(
                {
                    "propertyId": baseline["officialProperties"][0][
                        "propertyId"
                    ]
                }
            ),
            "propertyId.*unique",
        ),
        (
            lambda baseline: baseline["workbookEvidence"].update(
                {"logicalSource": "different.xlsx"}
            ),
            "workbookEvidence.logicalSource",
        ),
        (
            lambda baseline: baseline["legacyMetadataDigests"].pop(
                "exceptions"
            ),
            "legacyMetadataDigests.*missing required fields.*exceptions",
        ),
        (
            lambda baseline: baseline["legacyMetadataDigests"].update(
                {"unexpected": "0" * 64}
            ),
            "legacyMetadataDigests.*unexpected fields.*unexpected",
        ),
        (
            lambda baseline: baseline["legacyMetadataDigests"].update(
                {"entityPolicies": "not-a-sha256"}
            ),
            "legacyMetadataDigests.entityPolicies.*64 lowercase hex",
        ),
    ],
    ids=[
        "open-top-level",
        "wrong-count",
        "open-record",
        "duplicate-property-id",
        "wrong-workbook-source",
        "missing-legacy-digest",
        "open-legacy-digests",
        "invalid-legacy-digest",
    ],
)
def test_compiler_rejects_invalid_compatibility_baseline(
    tmp_path, mutation, message
):
    from tools.build_hbr_rulepack import compile_rulepack

    baseline = _load_baseline()
    mutation(baseline)
    invalid_baseline = tmp_path / "invalid baseline.json"
    _write_json(invalid_baseline, baseline)
    output = tmp_path / "invalid baseline.hbrpack"

    with pytest.raises(ValueError, match=message):
        compile_rulepack(SOURCE_PATH, output, invalid_baseline)

    assert not output.exists()


def test_compiler_rejects_duplicate_json_keys_in_baseline(tmp_path):
    from tools.build_hbr_rulepack import compile_rulepack

    baseline_text = BASELINE_PATH.read_text(encoding="utf-8")
    duplicate_key_text = baseline_text.replace(
        '"schemaVersion": "1.1.0",',
        '"schemaVersion": "1.1.0",\n  "schemaVersion": "1.1.0",',
        1,
    )
    invalid_baseline = tmp_path / "duplicate key baseline.json"
    invalid_baseline.write_text(duplicate_key_text, encoding="utf-8")
    output = tmp_path / "duplicate key.hbrpack"

    with pytest.raises(ValueError, match="duplicate JSON key.*schemaVersion"):
        compile_rulepack(SOURCE_PATH, output, invalid_baseline)

    assert not output.exists()


def _source_text_with_preceding_duplicate_schema_version():
    source_text = SOURCE_PATH.read_text(encoding="utf-8")
    valid_schema_version = '  "schemaVersion": "1.0.0",'
    assert source_text.count(valid_schema_version) == 1
    return source_text.replace(
        valid_schema_version,
        '  "schemaVersion": "9.9.9",\n' + valid_schema_version,
        1,
    )


def test_compiler_rejects_duplicate_top_level_json_keys_in_source(tmp_path):
    from tools.build_hbr_rulepack import compile_rulepack

    invalid_source = tmp_path / "duplicate top-level key source.json"
    invalid_source.write_text(
        _source_text_with_preceding_duplicate_schema_version(),
        encoding="utf-8",
    )
    output = tmp_path / "duplicate top-level key.hbrpack"

    with pytest.raises(
        ValueError,
        match=r"HBR rule source contains duplicate JSON key 'schemaVersion'",
    ):
        compile_rulepack(invalid_source, output, BASELINE_PATH)

    assert not output.exists()


def test_compiler_rejects_duplicate_nested_json_keys_in_source(tmp_path):
    from tools.build_hbr_rulepack import compile_rulepack

    source_text = SOURCE_PATH.read_text(encoding="utf-8")
    duplicate_key_text, replacement_count = re.subn(
        r'(?m)^(      "propertyId": "[^"]+",)$',
        '      "propertyId": "PRECEDING-DUPLICATE-VALUE",\n' + r'\1',
        source_text,
        count=1,
    )
    assert replacement_count == 1
    invalid_source = tmp_path / "duplicate nested key source.json"
    invalid_source.write_text(duplicate_key_text, encoding="utf-8")
    output = tmp_path / "duplicate nested key.hbrpack"

    with pytest.raises(
        ValueError,
        match=r"HBR rule source contains duplicate JSON key 'propertyId'",
    ):
        compile_rulepack(invalid_source, output, BASELINE_PATH)

    assert not output.exists()


def _ifc_identity(rule):
    return (
        rule["ifc"]["entity"],
        rule["ifc"]["propertySet"],
        rule["ifc"]["property"],
    )


def _duplicate_parameter_guid(source):
    source["properties"][1]["revit"]["parameterGuid"] = source["properties"][0][
        "revit"
    ]["parameterGuid"]


def _dangle_carrier_reference(source):
    source["properties"][0]["carrierRoleIds"][0] = "MISSING.CARRIER"


def _change_property_count(source):
    source["properties"].pop()


def _hide_parameter(source):
    source["properties"][0]["revit"]["visible"] = False


def _break_requirement_cross_field_contract(source):
    source["properties"][0]["requirement"] = {
        "level": "CONDITIONAL",
        "conditionId": None,
    }


def _create_dependency_cycle(source):
    first_id, second_id = source["modelProfiles"][0]["taskIds"][:2]
    tasks = {task["taskId"]: task for task in source["tasks"]}
    tasks[first_id]["dependencies"] = [second_id]
    tasks[second_id]["dependencies"] = [first_id]


def _change_contract_composition(source):
    rule = next(rule for rule in source["properties"] if rule["contractKind"] == "MVD")
    rule["contractKind"] = "HIFC_EXTENSION"
    rule["extensionReason"] = "test mutation"


def _change_official_count(source):
    rule = next(
        rule
        for rule in source["properties"]
        if rule["officialPlugin"]["inExtracted166"]
    )
    rule["officialPlugin"] = {
        "inExtracted166": False,
        "evidenceStatus": "MVD_WORKBOOK",
        "originalIdentity": None,
    }


def _change_official_mvd_count(source):
    official_extension = next(
        rule
        for rule in source["properties"]
        if rule["contractKind"] == "HIFC_EXTENSION"
        and rule["officialPlugin"]["inExtracted166"]
    )
    nonofficial_mvd = next(
        rule
        for rule in source["properties"]
        if rule["contractKind"] == "MVD"
        and not rule["officialPlugin"]["inExtracted166"]
    )
    official_extension["contractKind"] = "MVD"
    nonofficial_mvd["contractKind"] = "HIFC_EXTENSION"
    nonofficial_mvd["extensionReason"] = "test mutation"


def _change_stage01_official_hit_count(source):
    official_ids = {
        rule["propertyId"]
        for rule in source["properties"]
        if rule["officialPlugin"]["inExtracted166"]
    }
    refs = source["stage01"]["fieldRefs"]
    referenced_ids = {ref["propertyId"] for ref in refs}
    replacement = next(
        rule["propertyId"]
        for rule in source["properties"]
        if rule["propertyId"] not in official_ids
        and rule["propertyId"] not in referenced_ids
    )
    replacement_rule = next(
        rule for rule in source["properties"] if rule["propertyId"] == replacement
    )
    reference = next(ref for ref in refs if ref["propertyId"] in official_ids)
    reference.update(
        {
            "propertyId": replacement,
            "sourceRow": replacement_rule["source"]["row"],
            "fieldKey": "|".join(_ifc_identity(replacement_rule)),
        }
    )


def _duplicate_property_id(source):
    source["properties"][1]["propertyId"] = source["properties"][0]["propertyId"]


def _duplicate_mvd_identity(source):
    first, second = [
        rule for rule in source["properties"] if rule["contractKind"] == "MVD"
    ][:2]
    second["ifc"]["entity"] = first["ifc"]["entity"]
    second["ifc"]["propertySet"] = first["ifc"]["propertySet"]
    second["ifc"]["property"] = first["ifc"]["property"]


def _use_uuid4_property_id(source):
    source["properties"][0]["propertyId"] = "00000000-0000-4000-8000-000000000001"


def _break_new_uuid5_derivation(source):
    rule = next(
        rule
        for rule in source["properties"]
        if rule["contractKind"] == "MVD"
        and not rule["officialPlugin"]["inExtracted166"]
    )
    replacement = str(uuid.uuid5(uuid.NAMESPACE_DNS, "wrong-hbr-property-id"))
    rule["propertyId"] = replacement
    rule["revit"]["parameterGuid"] = replacement


def _dangle_requirement_condition(source):
    source["properties"][0]["requirement"] = {
        "level": "CONDITIONAL",
        "conditionId": "missing.condition",
    }


def _dangle_task_condition(source):
    task = next(task for task in source["tasks"] if task["requirement"] == "CONDITIONAL")
    task["conditionId"] = "missing.condition"


def _dangle_profile_task(source):
    source["modelProfiles"][0]["taskIds"][0] = "MISSING.TASK"


def _dangle_alias_property(source):
    source["legacyAliases"][0]["propertyId"] = "00000000-0000-5000-8000-000000000001"


def _dangle_stage_property(source):
    source["stage01"]["fieldRefs"][0]["propertyId"] = (
        "00000000-0000-5000-8000-000000000001"
    )


def _change_stage_field_key(source):
    source["stage01"]["fieldRefs"][0]["fieldKey"] = "IfcProject|Pset_Test|Wrong"


def _blank_source_artifact(source):
    source["properties"][0]["source"]["artifact"] = ""


def _use_invalid_extension_source_row(source):
    rule = next(
        rule for rule in source["properties"] if rule["contractKind"] == "HIFC_EXTENSION"
    )
    rule["source"]["row"] = 1


def _use_style_sentinel(source):
    source["properties"][0]["source"]["rawValueKind"] = "14"


def _use_unsupported_mvd_entity(source):
    role = source["carrierRoles"][0]
    role_id = role["roleId"]
    role["ifcEntity"] = "IfcBogus"
    role["cardinality"]["max"] = None
    affected_ids = set()
    for rule in source["properties"]:
        if role_id not in rule["carrierRoleIds"]:
            continue
        rule["ifc"]["entity"] = "IfcBogus"
        rule["source"]["rawEntityId"] = "IfcBogus"
        affected_ids.add(rule["propertyId"])
    for reference in source["stage01"]["fieldRefs"]:
        if reference["propertyId"] in affected_ids:
            _, property_set, property_name = reference["fieldKey"].split("|", 2)
            reference["fieldKey"] = f"IfcBogus|{property_set}|{property_name}"


def _duplicate_carrier_id(source):
    source["carrierRoles"][1]["roleId"] = source["carrierRoles"][0]["roleId"]


def _duplicate_task_id(source):
    source["tasks"][1]["taskId"] = source["tasks"][0]["taskId"]


def _duplicate_condition_id(source):
    source["conditions"][1]["conditionId"] = source["conditions"][0]["conditionId"]


def _duplicate_profile_id(source):
    source["modelProfiles"][1]["profileId"] = source["modelProfiles"][0]["profileId"]


def _duplicate_property_reference(source):
    role_id = source["properties"][0]["carrierRoleIds"][0]
    source["properties"][0]["carrierRoleIds"].append(role_id)


def _duplicate_profile_reference(source):
    task_id = source["modelProfiles"][0]["taskIds"][0]
    source["modelProfiles"][0]["taskIds"].append(task_id)


def _duplicate_task_array_value(source):
    task = next(task for task in source["tasks"] if task["attributeRequirements"])
    task["attributeRequirements"].append(task["attributeRequirements"][0])


def _duplicate_stage_reference(source):
    source["stage01"]["fieldRefs"][-1] = copy.deepcopy(
        source["stage01"]["fieldRefs"][0]
    )


def _duplicate_legacy_alias(source):
    source["legacyAliases"][-1] = copy.deepcopy(source["legacyAliases"][0])


def _disable_user_modifiable(source):
    source["properties"][0]["revit"]["userModifiable"] = False


def _break_official_plugin_cross_field_contract(source):
    rule = next(
        rule
        for rule in source["properties"]
        if rule["officialPlugin"]["inExtracted166"]
    )
    rule["officialPlugin"]["evidenceStatus"] = "MVD_WORKBOOK"


def _break_cardinality(source):
    source["carrierRoles"][0]["cardinality"] = {"min": 1, "max": 0}


def _use_invalid_enum(source):
    source["properties"][0]["stageOwnership"] = ["BOGUS"]


def _break_runtime_type_mapping(source):
    rule = next(
        rule
        for rule in source["properties"]
        if rule["ifc"]["declaredType"] == "IfcReal"
        and rule["ifc"]["canonicalUnit"] == "m"
    )
    rule["ifc"]["allowedRuntimeTypes"] = ["IfcReal"]


def _use_unknown_runtime_type(source):
    source["properties"][0]["ifc"]["allowedRuntimeTypes"] = ["BogusType"]


def _strip_pset_prefix(source):
    source["properties"][0]["ifc"]["propertySet"] = "WithoutPrefix"


def _break_write_strategy(source):
    source["properties"][0]["ifcWrite"]["writeStrategy"] = "REPLACE"


def _break_owner_strategy(source):
    source["properties"][0]["ifcWrite"]["ownerStrategy"] = "BY_EXPORT_GUID"


def _dangle_dependency(source):
    source["tasks"][0]["dependencies"] = ["MISSING.TASK"]


def _cross_profile_dependency(source):
    first_profile, second_profile = source["modelProfiles"][:2]
    tasks = {task["taskId"]: task for task in source["tasks"]}
    tasks[first_profile["taskIds"][0]]["dependencies"] = [second_profile["taskIds"][0]]


def _drop_evidence_source(source):
    source["evidenceSources"].pop()


def _drop_task(source):
    source["tasks"].pop()


def _drop_required_property_field(source):
    del source["properties"][0]["source"]["artifact"]


def _add_unknown_top_level_field(source):
    source["unexpected"] = True


def _drop_internal_workflow_fields(source):
    source["stage01"].pop("internalWorkflowFields")


def _truncate_internal_workflow_fields(source):
    source["stage01"]["internalWorkflowFields"].pop()


def _drop_field_ref_ui_group(source):
    source["stage01"]["fieldRefs"][0].pop("uiGroup")


def _drop_legacy_projection(source):
    official = next(
        rule
        for rule in source["properties"]
        if rule["officialPlugin"]["inExtracted166"]
    )
    official["officialPlugin"].pop("legacyProjection")


def _drop_legacy_projection_field(source):
    official = next(
        rule
        for rule in source["properties"]
        if rule["officialPlugin"]["inExtracted166"]
    )
    official["officialPlugin"]["legacyProjection"].pop("sharedParameterType")


def _truncate_official_plugin_exceptions(source):
    source["stage01"]["officialPluginCompatibility"]["exceptions"].pop()


def _drop_profile_activation_rule_ids(source):
    source["modelProfiles"][0].pop("activationRuleIds")


def _blank_official_plugin_exception_reason(source):
    source["stage01"]["officialPluginCompatibility"]["exceptions"][0][
        "reason"
    ] = "   "


def _set_migrated_string_to_whitespace(source, section, field, value="   "):
    if section == "internalWorkflowFields" and field == "allowedValues":
        item = next(
            item
            for item in source["stage01"][section]
            if item[field]
        )
        item[field][0] = value
        return
    if section == "internalWorkflowFields" and field == "defaultValue":
        item = next(
            item
            for item in source["stage01"][section]
            if item[field] is not None
        )
        item[field] = value
        return
    if section in {"internalWorkflowFields", "fieldRefs"}:
        source["stage01"][section][0][field] = value
        return
    if section == "modelProfiles" and field == "activationRuleIds":
        source[section][0][field][0] = value
        return
    if section == "modelProfiles":
        source[section][0][field] = value
        return
    if section == "entityPolicies":
        source["stage01"]["officialPluginCompatibility"][section][0][
            field
        ] = value
        return
    if section == "exceptions":
        source["stage01"]["officialPluginCompatibility"][section][0][
            field
        ] = value
        return
    if section == "legacyProjection":
        official = next(
            rule
            for rule in source["properties"]
            if rule["officialPlugin"]["inExtracted166"]
        )
        official["officialPlugin"][section][field] = value
        return
    raise AssertionError(f"unknown migrated section: {section}")


def _duplicate_internal_workflow_field(source):
    source["stage01"]["internalWorkflowFields"][-1] = copy.deepcopy(
        source["stage01"]["internalWorkflowFields"][0]
    )


def _duplicate_entity_policy(source):
    policies = source["stage01"]["officialPluginCompatibility"]["entityPolicies"]
    policies[-1] = copy.deepcopy(policies[0])


def _dangle_official_plugin_exception(source):
    source["stage01"]["officialPluginCompatibility"]["exceptions"][0][
        "fieldKey"
    ] = "IfcProject|Pset_Missing|Missing"


def _dangle_profile_activation_rule(source):
    source["modelProfiles"][0]["activationRuleIds"][0] = "MISSING.ACTIVATION.RULE"


def _duplicate_profile_activation_rule(source):
    activation_ids = source["modelProfiles"][0]["activationRuleIds"]
    activation_ids.append(activation_ids[0])


def _drift_internal_workflow_label(source):
    source["stage01"]["internalWorkflowFields"][0]["label"] += "_DRIFT"


def _drift_field_ref_ui_group(source):
    source["stage01"]["fieldRefs"][0]["uiGroup"] += "_DRIFT"


def _move_nonwritable_field_ref(source):
    references = source["stage01"]["fieldRefs"]
    nonwritable = next(item for item in references if not item["writeInStage01"])
    writable = next(item for item in references if item["writeInStage01"])
    nonwritable["writeInStage01"] = True
    writable["writeInStage01"] = False


def _drift_legacy_projection_carrier(source):
    official = next(
        rule
        for rule in source["properties"]
        if rule["officialPlugin"]["inExtracted166"]
    )
    official["officialPlugin"]["legacyProjection"]["carrier"] += "_DRIFT"


def _drift_entity_policy_evidence(source):
    source["stage01"]["officialPluginCompatibility"]["entityPolicies"][0][
        "officialObjectMappingEvidence"
    ] = "UNVERIFIED"


def _drift_official_plugin_exception_reason(source):
    source["stage01"]["officialPluginCompatibility"]["exceptions"][0][
        "reason"
    ] += "_DRIFT"


def _drift_profile_activation_rule_id(source):
    source["modelProfiles"][0]["activationRuleIds"][0] = (
        "HBR.BUILDING.ABOVE.BASE"
    )


@pytest.mark.parametrize(
    ("mutation", "message"),
    [
        (
            _drop_internal_workflow_fields,
            r"migrated metadata.*internalWorkflowFields",
        ),
        (
            _truncate_internal_workflow_fields,
            r"migrated metadata.*internalWorkflowFields.*exactly 12",
        ),
        (_drop_field_ref_ui_group, r"migrated metadata.*fieldRefs.*uiGroup"),
        (_drop_legacy_projection, r"migrated metadata.*legacyProjection"),
        (
            _drop_legacy_projection_field,
            r"migrated metadata.*legacyProjection.*sharedParameterType",
        ),
        (
            _truncate_official_plugin_exceptions,
            r"migrated metadata.*exceptions.*exactly 13",
        ),
        (
            _drop_profile_activation_rule_ids,
            r"migrated metadata.*activationRuleIds",
        ),
        (
            _blank_official_plugin_exception_reason,
            r"migrated metadata.*reason.*non-empty",
        ),
    ],
)
def test_validate_semantics_rejects_missing_or_truncated_migrated_metadata(
    mutation, message
):
    from tools.build_hbr_rulepack import validate_semantics

    source = _load_source()
    mutation(source)

    with pytest.raises(ValueError, match=message):
        validate_semantics(source)


@pytest.mark.parametrize(
    ("section", "field"),
    [
        ("internalWorkflowFields", "fieldKey"),
        ("internalWorkflowFields", "label"),
        ("internalWorkflowFields", "uiGroup"),
        ("internalWorkflowFields", "sourceKind"),
        ("internalWorkflowFields", "allowedValues"),
        ("internalWorkflowFields", "defaultValue"),
        ("fieldRefs", "fieldKey"),
        ("fieldRefs", "propertyId"),
        ("fieldRefs", "uiGroup"),
        ("fieldRefs", "sourceKind"),
        ("modelProfiles", "profileId"),
        ("modelProfiles", "activationRuleIds"),
        ("entityPolicies", "ifcEntity"),
        ("entityPolicies", "officialObjectMappingEvidence"),
        ("entityPolicies", "writePolicy"),
        ("entityPolicies", "revitCarrier"),
        ("legacyProjection", "carrier"),
        ("legacyProjection", "persistenceMode"),
        ("legacyProjection", "sharedParameterType"),
        ("legacyProjection", "officialSourceParameterGroup"),
        ("legacyProjection", "category"),
        ("legacyProjection", "sourceParameterOverride"),
        ("exceptions", "fieldKey"),
        ("exceptions", "reason"),
    ],
)
def test_validate_structure_rejects_whitespace_only_migrated_strings(
    section, field
):
    from tools.build_hbr_rulepack import _validate_structure

    source = _load_source()
    _set_migrated_string_to_whitespace(source, section, field)

    with pytest.raises(
        ValueError,
        match=rf"{field}.*(?:non-empty|whitespace|trimming)",
    ):
        _validate_structure(source)


@pytest.mark.parametrize(
    ("section", "field"),
    [
        ("legacyProjection", "category"),
        ("legacyProjection", "sourceParameterOverride"),
        ("entityPolicies", "revitCarrier"),
    ],
)
@pytest.mark.parametrize("whitespace", ["\n", "\r\n", "\t"])
def test_validate_structure_rejects_nonempty_whitespace_for_empty_capable_fields(
    section, field, whitespace
):
    from tools.build_hbr_rulepack import _validate_structure

    source = _load_source()
    _set_migrated_string_to_whitespace(
        source,
        section,
        field,
        whitespace,
    )

    with pytest.raises(ValueError, match=rf"{field}.*whitespace"):
        _validate_structure(source)


@pytest.mark.parametrize(
    ("section", "field"),
    [
        ("legacyProjection", "category"),
        ("legacyProjection", "sourceParameterOverride"),
        ("entityPolicies", "revitCarrier"),
    ],
)
def test_validate_structure_allows_true_empty_for_empty_capable_fields(
    section, field
):
    from tools.build_hbr_rulepack import _validate_structure

    source = _load_source()
    _set_migrated_string_to_whitespace(source, section, field, "")

    _validate_structure(source)


@pytest.mark.parametrize(
    ("mutation", "message"),
    [
        (
            _duplicate_internal_workflow_field,
            r"migrated metadata.*internalWorkflowFields.fieldKey.*unique",
        ),
        (
            _duplicate_entity_policy,
            r"migrated metadata.*entityPolicies.ifcEntity.*unique",
        ),
        (
            _dangle_stage_property,
            r"migrated metadata.*fieldRefs.*unknown property reference",
        ),
        (
            _dangle_official_plugin_exception,
            r"migrated metadata.*exceptions.*unknown field reference",
        ),
        (
            _dangle_profile_activation_rule,
            r"migrated metadata.*unknown activation rule reference",
        ),
        (
            _duplicate_profile_activation_rule,
            r"migrated metadata.*activationRuleIds.*unique",
        ),
    ],
)
def test_validate_semantics_rejects_duplicate_or_dangling_migrated_references(
    mutation, message
):
    from tools.build_hbr_rulepack import validate_semantics

    source = _load_source()
    mutation(source)

    with pytest.raises(ValueError, match=message):
        validate_semantics(source)


@pytest.mark.parametrize(
    ("mutation", "section"),
    [
        (_drift_internal_workflow_label, "internalWorkflowFields"),
        (_drift_field_ref_ui_group, "stage01FieldMetadata"),
        (_move_nonwritable_field_ref, "stage01FieldMetadata"),
        (_drift_legacy_projection_carrier, "officialLegacyProjection"),
        (_drift_entity_policy_evidence, "entityPolicies"),
        (_drift_official_plugin_exception_reason, "exceptions"),
        (_drift_profile_activation_rule_id, "profileActivationRuleIds"),
    ],
)
def test_validate_compatibility_rejects_legacy_equivalence_value_drift(
    mutation, section
):
    from tools.build_hbr_rulepack import validate_compatibility

    source = _load_source()
    baseline = _load_baseline()
    mutation(source)

    with pytest.raises(
        ValueError,
        match=rf"migrated metadata.*{section}.*legacy equivalence",
    ):
        validate_compatibility(source, baseline)


@pytest.mark.parametrize("section", LEGACY_METADATA_DIGEST_NAMES)
def test_validate_compatibility_rejects_legacy_metadata_digest_tampering(
    section,
):
    from tools.build_hbr_rulepack import validate_compatibility

    source = _load_source()
    baseline = _load_baseline()
    baseline["legacyMetadataDigests"][section] = "0" * 64

    with pytest.raises(
        ValueError,
        match=rf"migrated metadata.*{section}.*legacy equivalence",
    ):
        validate_compatibility(source, baseline)


def test_compile_rulepack_is_deterministic_and_has_a_verified_header(tmp_path):
    from tools.build_hbr_rulepack import MAGIC, FORMAT_VERSION, compile_rulepack

    first = tmp_path / "first.hbrpack"
    second = tmp_path / "second.hbrpack"

    compile_rulepack(SOURCE_PATH, first, BASELINE_PATH)
    compile_rulepack(SOURCE_PATH, second, BASELINE_PATH)

    first_bytes = first.read_bytes()
    assert first_bytes == second.read_bytes()
    assert first_bytes[:4] == MAGIC == b"HBRP"
    assert struct.unpack(">I", first_bytes[4:8])[0] == FORMAT_VERSION == 1

    payload_length = struct.unpack(">Q", first_bytes[8:16])[0]
    payload_hash = first_bytes[16:48]
    payload = first_bytes[48:]
    source = json.loads(SOURCE_PATH.read_text(encoding="utf-8"))
    expected_payload = json.dumps(
        source,
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
    ).encode("utf-8")

    assert payload_length == len(payload) == len(expected_payload)
    assert payload_hash == hashlib.sha256(payload).digest()
    assert payload == expected_payload


def test_mvd_identity_must_not_duplicate_a_verified_extension_identity():
    from tools.build_hbr_rulepack import validate_semantics

    source = _load_source()
    stage01_ids = {
        reference["propertyId"] for reference in source["stage01"]["fieldRefs"]
    }
    role_use_counts = {
        role["roleId"]: sum(
            role["roleId"] in rule["carrierRoleIds"]
            for rule in source["properties"]
        )
        for role in source["carrierRoles"]
    }
    extension = next(
        rule
        for rule in source["properties"]
        if rule["contractKind"] == "HIFC_EXTENSION"
        and rule["ifc"]["entity"] == "IfcSpace"
    )
    target = next(
        rule
        for rule in source["properties"]
        if rule["contractKind"] == "MVD"
        and not rule["officialPlugin"]["inExtracted166"]
        and rule["propertyId"] not in stage01_ids
        and role_use_counts[rule["carrierRoleIds"][0]] > 1
    )

    source_contract = copy.deepcopy(extension["source"])
    for field in ("artifact", "sheet", "row"):
        source_contract[field] = target["source"][field]
    parameter_guid = target["revit"]["parameterGuid"]
    target["source"] = source_contract
    target["ifc"] = copy.deepcopy(extension["ifc"])
    target["revit"] = copy.deepcopy(extension["revit"])
    target["revit"]["parameterGuid"] = parameter_guid
    target["carrierRoleIds"] = copy.deepcopy(extension["carrierRoleIds"])
    target["suggestion"] = copy.deepcopy(extension["suggestion"])
    target["ifcWrite"] = copy.deepcopy(extension["ifcWrite"])

    mvd_identities = [
        _ifc_identity(rule)
        for rule in source["properties"]
        if rule["contractKind"] == "MVD"
    ]
    all_identities = [_ifc_identity(rule) for rule in source["properties"]]
    assert len(mvd_identities) == len(set(mvd_identities)) == 356
    assert len(all_identities) == 359
    assert len(set(all_identities)) == 358

    with pytest.raises(ValueError, match="all property IFC identity"):
        validate_semantics(source)


def test_cli_compiles_to_a_path_with_spaces(tmp_path):
    output = tmp_path / "directory with spaces" / "HBR Rule Pack.hbrpack"

    result = subprocess.run(
        [
            sys.executable,
            str(COMPILER_PATH),
            "--source",
            str(SOURCE_PATH),
            "--baseline",
            str(BASELINE_PATH),
            "--output",
            str(output),
        ],
        check=False,
        capture_output=True,
        text=True,
        encoding="utf-8",
    )

    assert result.returncode == 0, result.stderr
    assert output.read_bytes()[:4] == b"HBRP"


def test_cli_requires_an_explicit_compatibility_baseline(tmp_path):
    output = tmp_path / "missing baseline.hbrpack"

    result = subprocess.run(
        [
            sys.executable,
            str(COMPILER_PATH),
            "--source",
            str(SOURCE_PATH),
            "--output",
            str(output),
        ],
        check=False,
        capture_output=True,
        text=True,
        encoding="utf-8",
    )

    assert result.returncode != 0
    assert "--baseline" in result.stderr
    assert not output.exists()


def test_cli_reports_validation_errors_and_leaves_no_output(tmp_path):
    source = _load_source()
    _hide_parameter(source)
    invalid_source = tmp_path / "invalid source.json"
    invalid_source.write_text(json.dumps(source, ensure_ascii=False), encoding="utf-8")
    output = tmp_path / "invalid output.hbrpack"

    result = subprocess.run(
        [
            sys.executable,
            str(COMPILER_PATH),
            "--source",
            str(invalid_source),
            "--baseline",
            str(BASELINE_PATH),
            "--output",
            str(output),
        ],
        check=False,
        capture_output=True,
        text=True,
        encoding="utf-8",
    )

    assert result.returncode != 0
    assert "visible" in result.stderr
    assert not output.exists()


def test_cli_reports_duplicate_source_key_and_leaves_no_output(tmp_path):
    invalid_source = tmp_path / "duplicate key source.json"
    invalid_source.write_text(
        _source_text_with_preceding_duplicate_schema_version(),
        encoding="utf-8",
    )
    output = tmp_path / "duplicate key output.hbrpack"

    result = subprocess.run(
        [
            sys.executable,
            str(COMPILER_PATH),
            "--source",
            str(invalid_source),
            "--baseline",
            str(BASELINE_PATH),
            "--output",
            str(output),
        ],
        check=False,
        capture_output=True,
        text=True,
        encoding="utf-8",
    )

    assert result.returncode == 1
    assert result.stderr.strip() == (
        "HBR rule-pack compilation failed: HBR rule source contains "
        "duplicate JSON key 'schemaVersion'"
    )
    assert not output.exists()


def test_atomic_replace_failure_leaves_no_pack_or_temporary_file(tmp_path, monkeypatch):
    from tools.build_hbr_rulepack import compile_rulepack

    output = tmp_path / "atomic.hbrpack"

    def fail_replace(source, destination):
        assert Path(source).parent == output.parent
        assert Path(destination) == output
        raise OSError("replace failed")

    monkeypatch.setattr("os.replace", fail_replace)

    with pytest.raises(OSError, match="replace failed"):
        compile_rulepack(SOURCE_PATH, output, BASELINE_PATH)

    assert not output.exists()
    assert list(tmp_path.iterdir()) == []


def test_source_and_output_must_not_refer_to_the_same_file(tmp_path):
    from tools.build_hbr_rulepack import compile_rulepack

    source = tmp_path / "rule-source.json"
    original = SOURCE_PATH.read_bytes()
    source.write_bytes(original)

    with pytest.raises(ValueError, match="different files"):
        compile_rulepack(source, source, BASELINE_PATH)

    assert source.read_bytes() == original


def test_baseline_and_output_must_not_refer_to_the_same_file(tmp_path):
    from tools.build_hbr_rulepack import compile_rulepack

    baseline = tmp_path / "compatibility-baseline.json"
    original = BASELINE_PATH.read_bytes()
    baseline.write_bytes(original)

    with pytest.raises(ValueError, match="baseline and output must refer to different files"):
        compile_rulepack(SOURCE_PATH, baseline, baseline)

    assert baseline.read_bytes() == original
    assert list(tmp_path.iterdir()) == [baseline]


def test_stage01_project_builds_exactly_one_generated_hbr_pack_resource():
    root = ET.parse(STAGE01_PROJECT_PATH).getroot()
    properties = {
        element.tag: element
        for element in root.iter()
        if element.tag
        in {
            "HbrPythonExe",
            "HbrRuleSource",
            "HbrRulePackCompiler",
            "HbrCompatibilityBaseline",
        }
    }

    assert (properties["HbrPythonExe"].text or "").strip() in {"python", "python3"}
    assert properties["HbrPythonExe"].get("Condition")
    assert "hbr_rule_source.v1.json" in (
        properties["HbrRuleSource"].text or ""
    )
    assert "build_hbr_rulepack.py" in (
        properties["HbrRulePackCompiler"].text or ""
    )
    assert properties["HbrRulePackCompiler"].get("Condition")
    assert "hbr_rule_compatibility_baseline.v1.json" in (
        properties["HbrCompatibilityBaseline"].text or ""
    )

    targets = {target.get("Name"): target for target in root.iter("Target")}
    for name in (
        "InitializeHbrRulePackPath",
        "CompileHbrRulePack",
        "RegisterHbrRulePackResource",
        "PrepareHbrRulePackResource",
    ):
        assert sum(target.get("Name") == name for target in root.iter("Target")) == 1

    initialize = targets["InitializeHbrRulePackPath"]
    pack_properties = list(initialize.iter("HbrRulePack"))
    assert len(pack_properties) == 1
    assert "$(IntermediateOutputPath)" in (pack_properties[0].text or "")
    assert "HBR_RulePack.hbrpack" in (pack_properties[0].text or "")
    assert pack_properties[0].get("Condition")
    up_to_date_inputs = [
        item.get("Include") for item in initialize.iter("UpToDateCheckInput")
    ]
    assert up_to_date_inputs == [
        "$(HbrRuleSource)",
        "$(HbrRulePackCompiler)",
        "$(HbrCompatibilityBaseline)",
    ]
    up_to_date_outputs = [
        item.get("Include") for item in initialize.iter("UpToDateCheckBuilt")
    ]
    assert up_to_date_outputs == ["$(HbrRulePack)"]

    compile_target = targets["CompileHbrRulePack"]
    assert compile_target.get("DependsOnTargets") == "InitializeHbrRulePackPath"
    assert compile_target.get("Condition") == "'$(DesignTimeBuild)' != 'true'"
    assert set(compile_target.get("Inputs").split(";")) == {
        "$(HbrRuleSource)",
        "$(HbrRulePackCompiler)",
        "$(HbrCompatibilityBaseline)",
    }
    assert compile_target.get("Outputs") == "$(HbrRulePack)"
    assert compile_target.get("BeforeTargets") is None
    assert not list(compile_target.iter("EmbeddedResource"))
    command = next(compile_target.iter("Exec")).get("Command")
    for argument, value in (
        (None, "$(HbrPythonExe)"),
        (None, "$(HbrRulePackCompiler)"),
        ("--source", "$(HbrRuleSource)"),
        ("--baseline", "$(HbrCompatibilityBaseline)"),
        ("--output", "$(HbrRulePack)"),
    ):
        assert f'"{value}"' in command
        if argument is not None:
            assert f'{argument} "{value}"' in command

    register = targets["RegisterHbrRulePackResource"]
    assert register.get("BeforeTargets") == "AssignTargetPaths"
    assert set(register.get("DependsOnTargets").split(";")) == {
        "InitializeHbrRulePackPath",
        "CompileHbrRulePack",
    }
    assert register.get("Inputs") is None
    assert register.get("Outputs") is None
    registered_resources = list(register.iter("EmbeddedResource"))
    assert len(registered_resources) == 1
    assert registered_resources[0].get("Include") == "$(HbrRulePack)"
    assert registered_resources[0].get("LogicalName") == (
        "BIMBaoGui.Stage01.Resources.HBR_RulePack.hbrpack"
    )
    assert [item.get("Include") for item in register.iter("FileWrites")] == [
        "$(HbrRulePack)"
    ]

    prepare = targets["PrepareHbrRulePackResource"]
    assert prepare.get("DependsOnTargets") == "InitializeHbrRulePackPath"
    assert set(prepare.get("BeforeTargets").split(";")) == {
        "CollectUpToDateCheckInputDesignTime",
        "CollectUpToDateCheckBuiltDesignTime",
    }

    actual_resources = {
        (resource.get("Include"), resource.get("LogicalName"))
        for resource in root.iter("EmbeddedResource")
    }
    assert actual_resources == {
        (
            "Resources\\stage01_file_initialization_registry_v0.1.json",
            "BIMBaoGui.Stage01.Resources.stage01_file_initialization_registry_v0.1.json",
        ),
        (
            "..\\..\\specs\\hifc-mapping\\v1\\generated\\GH_HIFC_ParameterBindings.json",
            "BIMBaoGui.Stage01.Resources.GH_HIFC_ParameterBindings.json",
        ),
        (
            "..\\..\\specs\\hifc-mapping\\v1\\generated\\GH_HIFC_SharedParameters.txt",
            "BIMBaoGui.Stage01.Resources.GH_HIFC_SharedParameters.txt",
        ),
        (
            "..\\..\\specs\\hifc-mapping\\v1\\data\\wuhan_planning_rules.v1.json",
            "BIMBaoGui.Stage01.Resources.wuhan_planning_rules.v1.json",
        ),
        (
            "..\\..\\specs\\hifc-mapping\\v1\\data\\official_plugin_compatibility_status.v1.json",
            "BIMBaoGui.Stage01.Resources.official_plugin_compatibility_status.v1.json",
        ),
        (
            "$(HbrRulePack)",
            "BIMBaoGui.Stage01.Resources.HBR_RulePack.hbrpack",
        ),
    }


def test_stage01_real_build_is_incremental_and_embeds_only_the_generated_pack(
    tmp_path,
):
    inputs = tmp_path / "copied inputs with spaces"
    inputs.mkdir()
    copied_source = inputs / "rule source.json"
    copied_compiler = inputs / "rule pack compiler.py"
    copied_baseline = inputs / "compatibility baseline.json"
    python_wrapper = inputs / "python wrapper.cmd"
    copied_source.write_bytes(SOURCE_PATH.read_bytes())
    copied_compiler.write_bytes(COMPILER_PATH.read_bytes())
    copied_baseline.write_bytes(BASELINE_PATH.read_bytes())
    python_wrapper.write_text(
        f'@echo off\n"{sys.executable}" %*\n',
        encoding="utf-8",
    )

    intermediate = tmp_path / "obj with spaces" / "Release" / "net48"
    output_directory = tmp_path / "bin with spaces" / "Release" / "net48"
    pack = intermediate / "HBR_RulePack.hbrpack"
    gha = output_directory / "BIMBaoGui.Stage01.gha"
    properties = [
        f"-p:HbrPythonExe={python_wrapper}",
        f"-p:HbrRuleSource={copied_source}",
        f"-p:HbrRulePackCompiler={copied_compiler}",
        f"-p:HbrCompatibilityBaseline={copied_baseline}",
        f"-p:IntermediateOutputPath={intermediate}{os.sep}",
        f"-p:OutputPath={output_directory}{os.sep}",
    ]

    def run_build(target=None):
        command = [
            "dotnet",
            "build",
            str(STAGE01_PROJECT_PATH),
            "-c",
            "Release",
            "--no-restore",
            "--nologo",
            *properties,
        ]
        if target is not None:
            command.append(f"-t:{target}")
        environment = os.environ.copy()
        environment["DOTNET_CLI_UI_LANGUAGE"] = "en"
        result = subprocess.run(
            command,
            cwd=ROOT,
            env=environment,
            check=False,
            capture_output=True,
            text=True,
            encoding="utf-8",
            timeout=120,
        )
        assert result.returncode == 0, result.stdout + result.stderr

    run_build()
    assert pack.read_bytes()[:4] == b"HBRP"
    first_mtime = pack.stat().st_mtime_ns

    run_build()
    assert pack.stat().st_mtime_ns == first_mtime

    source = json.loads(copied_source.read_text(encoding="utf-8"))
    source["properties"][0]["suggestion"]["aliases"].append(
        "增量构建源输入测试"
    )
    time.sleep(1.1)
    _write_json(copied_source, source)
    run_build()
    source_mtime = pack.stat().st_mtime_ns
    assert source_mtime > first_mtime

    time.sleep(1.1)
    copied_compiler.write_text(
        copied_compiler.read_text(encoding="utf-8")
        + "\n# incremental compiler input test\n",
        encoding="utf-8",
    )
    run_build()
    compiler_mtime = pack.stat().st_mtime_ns
    assert compiler_mtime > source_mtime

    time.sleep(1.1)
    copied_baseline.write_text(
        copied_baseline.read_text(encoding="utf-8") + "\n",
        encoding="utf-8",
    )
    run_build()
    baseline_mtime = pack.stat().st_mtime_ns
    assert baseline_mtime > compiler_mtime

    assert gha.is_file()
    manifest_environment = os.environ.copy()
    manifest_environment["HBR_TEST_ASSEMBLY"] = str(gha)
    manifest_result = subprocess.run(
        [
            "powershell.exe",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            "$assembly = [Reflection.Assembly]::ReflectionOnlyLoadFrom($env:HBR_TEST_ASSEMBLY); $assembly.GetManifestResourceNames() | Sort-Object",
        ],
        env=manifest_environment,
        check=False,
        capture_output=True,
        text=True,
        encoding="utf-8",
        timeout=30,
    )
    assert manifest_result.returncode == 0, (
        manifest_result.stdout + manifest_result.stderr
    )
    manifest_names = {
        line.strip() for line in manifest_result.stdout.splitlines() if line.strip()
    }
    assert manifest_names == {
        "BIMBaoGui.Stage01.Resources.stage01_file_initialization_registry_v0.1.json",
        "BIMBaoGui.Stage01.Resources.GH_HIFC_ParameterBindings.json",
        "BIMBaoGui.Stage01.Resources.GH_HIFC_SharedParameters.txt",
        "BIMBaoGui.Stage01.Resources.wuhan_planning_rules.v1.json",
        "BIMBaoGui.Stage01.Resources.official_plugin_compatibility_status.v1.json",
        "BIMBaoGui.Stage01.Resources.HBR_RulePack.hbrpack",
    }
    assert sum(name.endswith(".hbrpack") for name in manifest_names) == 1

    run_build("Clean")
    assert not pack.exists()


def test_stage01_official_reference_gate_reports_the_exact_published_count():
    from tools.build_hbr_rulepack import validate_semantics

    source = _load_source()
    _change_stage01_official_hit_count(source)

    with pytest.raises(ValueError, match="exactly 89 official property references"):
        validate_semantics(source)


@pytest.mark.parametrize(
    ("mutation", "message"),
    [
        (_duplicate_parameter_guid, "parameterGuid"),
        (_dangle_carrier_reference, "carrierRoleIds"),
        (_change_property_count, "359"),
        (_hide_parameter, "visible"),
        (_break_requirement_cross_field_contract, "conditionId"),
        (_create_dependency_cycle, "cycle"),
    ],
    ids=[
        "duplicate-guid",
        "dangling-reference",
        "changed-count",
        "invisible-parameter",
        "cross-field-invariant",
        "dependency-cycle",
    ],
)
def test_invalid_semantics_are_rejected_without_leaving_a_pack(
    tmp_path, mutation, message
):
    from tools.build_hbr_rulepack import compile_rulepack

    source = _load_source()
    mutation(source)
    mutated_source = tmp_path / "invalid.json"
    mutated_source.write_text(
        json.dumps(source, ensure_ascii=False),
        encoding="utf-8",
    )
    output = tmp_path / "invalid.hbrpack"

    with pytest.raises(ValueError, match=message):
        compile_rulepack(mutated_source, output, BASELINE_PATH)

    assert not output.exists()


@pytest.mark.parametrize(
    "mutation",
    [
        _change_contract_composition,
        _change_official_count,
        _change_official_mvd_count,
        _change_stage01_official_hit_count,
        _duplicate_property_id,
        _duplicate_mvd_identity,
        _use_uuid4_property_id,
        _break_new_uuid5_derivation,
        _dangle_requirement_condition,
        _dangle_task_condition,
        _dangle_profile_task,
        _dangle_alias_property,
        _dangle_stage_property,
        _change_stage_field_key,
        _blank_source_artifact,
        _use_invalid_extension_source_row,
        _use_style_sentinel,
        _use_unsupported_mvd_entity,
        _duplicate_carrier_id,
        _duplicate_task_id,
        _duplicate_condition_id,
        _duplicate_profile_id,
        _duplicate_property_reference,
        _duplicate_profile_reference,
        _duplicate_task_array_value,
        _duplicate_stage_reference,
        _duplicate_legacy_alias,
        _disable_user_modifiable,
        _break_official_plugin_cross_field_contract,
        _break_cardinality,
        _use_invalid_enum,
        _break_runtime_type_mapping,
        _use_unknown_runtime_type,
        _strip_pset_prefix,
        _break_write_strategy,
        _break_owner_strategy,
        _dangle_dependency,
        _cross_profile_dependency,
        _drop_evidence_source,
        _drop_task,
        _drop_required_property_field,
        _add_unknown_top_level_field,
    ],
)
def test_validate_semantics_rejects_every_key_contract_gate(mutation):
    from tools.build_hbr_rulepack import validate_semantics

    source = _load_source()
    mutation(source)

    with pytest.raises(ValueError) as error:
        validate_semantics(source)

    assert str(error.value)
