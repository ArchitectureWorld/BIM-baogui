import copy
import hashlib
import json
import struct
import subprocess
import sys
import uuid
import xml.etree.ElementTree as ET
from pathlib import Path

import pytest


ROOT = Path(__file__).resolve().parents[1]
SOURCE_PATH = ROOT / "specs/hbr-rules/v1/source/hbr_rule_source.v1.json"
COMPILER_PATH = ROOT / "tools/build_hbr_rulepack.py"
STAGE01_PROJECT_PATH = ROOT / "src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj"


def _load_source():
    return json.loads(SOURCE_PATH.read_text(encoding="utf-8"))


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
    next(ref for ref in refs if ref["propertyId"] in official_ids)[
        "propertyId"
    ] = replacement


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


def test_compile_rulepack_is_deterministic_and_has_a_verified_header(tmp_path):
    from tools.build_hbr_rulepack import MAGIC, FORMAT_VERSION, compile_rulepack

    first = tmp_path / "first.hbrpack"
    second = tmp_path / "second.hbrpack"

    compile_rulepack(SOURCE_PATH, first)
    compile_rulepack(SOURCE_PATH, second)

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


def test_cli_compiles_to_a_path_with_spaces(tmp_path):
    output = tmp_path / "directory with spaces" / "HBR Rule Pack.hbrpack"

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

    assert result.returncode == 0, result.stderr
    assert output.read_bytes()[:4] == b"HBRP"


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


def test_atomic_replace_failure_leaves_no_pack_or_temporary_file(tmp_path, monkeypatch):
    from tools.build_hbr_rulepack import compile_rulepack

    output = tmp_path / "atomic.hbrpack"

    def fail_replace(source, destination):
        assert Path(source).parent == output.parent
        assert Path(destination) == output
        raise OSError("replace failed")

    monkeypatch.setattr("os.replace", fail_replace)

    with pytest.raises(OSError, match="replace failed"):
        compile_rulepack(SOURCE_PATH, output)

    assert not output.exists()
    assert list(tmp_path.iterdir()) == []


def test_source_and_output_must_not_refer_to_the_same_file(tmp_path):
    from tools.build_hbr_rulepack import compile_rulepack

    source = tmp_path / "rule-source.json"
    original = SOURCE_PATH.read_bytes()
    source.write_bytes(original)

    with pytest.raises(ValueError, match="different files"):
        compile_rulepack(source, source)

    assert source.read_bytes() == original


def test_stage01_project_builds_exactly_one_generated_hbr_pack_resource():
    root = ET.parse(STAGE01_PROJECT_PATH).getroot()
    properties = {element.tag: (element.text or "").strip() for element in root.iter()}

    assert "hbr_rule_source.v1.json" in properties["HbrRuleSource"]
    assert "$(IntermediateOutputPath)" in properties["HbrRulePack"]
    assert "HBR_RulePack.hbrpack" in properties["HbrRulePack"]
    assert any(
        (element.text or "").strip() in {"python", "python3"}
        for element in root.iter("HbrPythonExe")
    )

    targets = [
        target for target in root.iter("Target") if target.get("Name") == "CompileHbrRulePack"
    ]
    assert len(targets) == 1
    assert targets[0].get("BeforeTargets") == "AssignTargetPaths"
    command = next(targets[0].iter("Exec")).get("Command")
    assert "--source" in command and "--output" in command
    for value in (
        "$(HbrPythonExe)",
        "$(_HbrRulePackCompiler)",
        "$(HbrRuleSource)",
        "$(HbrRulePack)",
    ):
        assert f'"{value}"' in command

    expected_name = "BIMBaoGui.Stage01.Resources.HBR_RulePack.hbrpack"
    resources = [
        resource
        for resource in root.iter("EmbeddedResource")
        if (resource.get("LogicalName") or "").endswith(".hbrpack")
    ]
    assert len(resources) == 1
    assert resources[0].get("LogicalName") == expected_name
    assert resources[0].get("Include") == "$(HbrRulePack)"
    logical_names = {
        resource.get("LogicalName") for resource in root.iter("EmbeddedResource")
    }
    assert {
        "BIMBaoGui.Stage01.Resources.stage01_file_initialization_registry_v0.1.json",
        "BIMBaoGui.Stage01.Resources.GH_HIFC_ParameterBindings.json",
        "BIMBaoGui.Stage01.Resources.GH_HIFC_SharedParameters.txt",
        "BIMBaoGui.Stage01.Resources.wuhan_planning_rules.v1.json",
        "BIMBaoGui.Stage01.Resources.official_plugin_compatibility_status.v1.json",
    } <= logical_names


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
        compile_rulepack(mutated_source, output)

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
