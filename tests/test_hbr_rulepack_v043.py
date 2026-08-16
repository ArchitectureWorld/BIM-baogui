import hashlib
import importlib.util
import json
import os
import struct
import subprocess
import sys
import uuid
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[1]
COMPILER = ROOT / "tools" / "build_hbr_rulepack_v043.py"
SOURCE = ROOT / "specs/hbr-rules/v1/source/hbr_rule_source.v1.json"
OVERLAY = ROOT / "specs/hbr-rules/v1/source/hbr_rule_source.v0.4.3-overlay.json"
BASELINE = ROOT / "specs/hbr-rules/v1/compatibility/hbr_rule_compatibility_baseline.v1.json"

METRICS = {
    "ca21e324-046b-5bfd-84c8-0d3470082303": "IfcProject|Pset_登记信息属性集|总建筑面积",
    "93e51676-237e-56a8-8f28-2da845422e2e": "IfcSite|Pset_场地信息属性集|建筑密度",
    "201a00ac-3672-5ded-83d2-ed96f81bfabf": "IfcSite|Pset_场地信息属性集|容积率",
    "f630ad47-b006-5127-badd-b1660cf996c3": "IfcSite|Pset_场地信息属性集|绿地率",
    "c62cfd5f-2a50-5230-9c5d-4037c39061bf": "IfcSpatialZone|Pset_停车场信息属性集|机动车位数量",
    "84df74c2-a7e5-5a98-a5e0-4458e49a3973": "IfcSpatialZone|Pset_停车场信息属性集|非机动车位数量",
}

EXPECTED_OFFICIAL_ACCEPTANCE_PROPERTY_IDS = [
    "0c15f6bd-0dfb-545a-bd4f-8c773f7aa6b5",
    "11110e9f-aaae-5576-ac0d-447a6f4b8524",
    "1a64ef8d-e97c-5fa1-b53f-52b969b6198a",
    "1b1f9357-ac7a-5339-9c7f-770bb91ac10f",
    "1b387099-43df-5c34-86eb-f6183395a934",
    "201a00ac-3672-5ded-83d2-ed96f81bfabf",
    "20c734f0-64ea-52a9-a73b-a335d6a811db",
    "21ac8910-524a-5f61-9d0c-a0a7bdd0c1a3",
    "21ef1d33-e8c4-51f9-8bb0-23250c872ad3",
    "22438777-1419-5970-b97d-2c3c44f7a6e5",
    "2b251cbc-2dfb-5bd9-8013-d5be0d846e69",
    "2bc733cd-c7a1-57fa-b206-1baeb71881d4",
    "35675fd2-c3d2-5553-8db6-855980a201a4",
    "38a62192-ecac-5e2d-9fc0-3b8d40afd27b",
    "3a7d90c2-9ccb-5d74-b13c-30c892693048",
    "3c02cb6f-d4bc-5b3d-b44d-6c0d258b9bb2",
    "3fd74b35-3164-5248-9fe9-c675992a4292",
    "40f7e661-5c21-5ed0-afe2-def81322eb06",
    "41a8f3ca-d057-5263-9dce-30bf795ae20d",
    "4225a5de-c942-54aa-874a-28a1e67ce39c",
    "422ad455-a88f-5d62-8b5d-562bc1aaf5f1",
    "43b52fa7-4954-5409-9389-4eddb97807a2",
    "4d9d7775-e83c-5357-8f3e-1e6a6692e793",
    "4f90183f-8105-58c4-93f6-e7525c4096b3",
    "50164757-c346-5005-a1b8-7b423c6b8de5",
    "504e3237-da89-5de9-a39a-4e5df0008903",
    "5d5f3dba-3ae9-59c6-9aee-aa24e88f312c",
    "5f1a2f9e-7fb5-56c2-b410-e094a079f40e",
    "5f9489f6-9809-5899-b949-2fa58d00cc1f",
    "6b407894-09d4-529a-9f9f-a031219cdeaa",
    "6b9e1517-695c-54fc-bf8a-3800d18019a0",
    "6cc053e3-891d-51b1-b861-af498733f73a",
    "7e970f3c-876f-5873-9f00-dcec9a6f1366",
    "81f745fe-f61f-55a1-922f-4a1fb05baaa0",
    "82dd52d2-1192-5d97-aa4b-a912cccb2709",
    "84df74c2-a7e5-5a98-a5e0-4458e49a3973",
    "85c3a1fe-4965-53d3-828c-bdf2298f3db8",
    "93e51676-237e-56a8-8f28-2da845422e2e",
    "960ac606-d0c1-5a1b-9f2e-d70d3a8eb712",
    "9b49b6b0-545b-5280-9b00-bb338cdc2ef6",
    "a99a0961-05fe-56fd-b8a0-865410bfe72f",
    "ace69397-c24a-5c6a-a253-3bf5fc657cd0",
    "aef64f95-dc27-5aff-9f13-3121f6c896a0",
    "af44d874-a366-5fb5-a65b-6242ad8f452f",
    "b45c2d7d-690c-55c2-a23c-626ad81962fb",
    "b970d6b1-92c9-51d2-8fac-187808a07801",
    "bc08027b-56bf-5c99-b9e7-9ba2f2a0e2d4",
    "c42ea80f-4a12-5d4b-8bba-2374135d9d2a",
    "c62cfd5f-2a50-5230-9c5d-4037c39061bf",
    "c94f1ae2-0a02-5479-aae4-c8f59af71fe0",
    "ca21e324-046b-5bfd-84c8-0d3470082303",
    "ce26e8a2-a98b-57b6-8c37-798d17c553cb",
    "da99190d-498d-5db7-b542-681c434f3ca9",
    "dc982a98-fc31-5e99-ac67-5ee489daeb86",
    "ddc7523d-e3aa-527e-9689-6ed93b2ba850",
    "e63e116d-b988-56fb-946d-e19937c1cb00",
    "e931547d-497e-5582-849e-451eee359411",
    "e951b3e2-8e17-5443-9b6b-570d07067856",
    "f020407e-9400-5eab-bf3b-0e3bcc138ba6",
    "f1c6c634-887c-51bd-9b12-2b734d5aa7bd",
    "f2fafcc9-abfd-55de-a54a-30f05180351b",
    "f630ad47-b006-5127-badd-b1660cf996c3",
]


def compile_pack(output: Path) -> bytes:
    subprocess.run([
        sys.executable, str(COMPILER), "--source", str(SOURCE),
        "--overlay", str(OVERLAY), "--baseline", str(BASELINE),
        "--output", str(output),
    ], check=True)
    return output.read_bytes()


def load_compiler():
    spec = importlib.util.spec_from_file_location("hbr_rulepack_v043", COMPILER)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def copy_compiler_inputs(tmp_path):
    copies = {}
    for name, source in (
        ("source", SOURCE),
        ("baseline", BASELINE),
        ("overlay", OVERLAY),
    ):
        copy = tmp_path / source.name
        copy.write_bytes(source.read_bytes())
        copies[name] = copy
    return copies


def test_v043_reporting_pack_is_deterministic_and_exact(tmp_path):
    first = compile_pack(tmp_path / "a.hbrpack")
    second = compile_pack(tmp_path / "b.hbrpack")
    assert first == second
    assert hashlib.sha256(first).hexdigest() == hashlib.sha256(second).hexdigest()
    assert first[:4] == b"HBRP"
    assert struct.unpack(">I", first[4:8])[0] == 1
    payload_length = struct.unpack(">Q", first[8:16])[0]
    payload_bytes = first[48:]
    assert payload_length == len(payload_bytes)
    assert first[16:48] == hashlib.sha256(payload_bytes).digest()
    payload = json.loads(payload_bytes.decode("utf-8"))
    assert len(payload["properties"]) == 363
    assert len(payload["carrierRoles"]) == 15
    reporting = payload["nativeReporting"]
    assert reporting["schemaVersion"] == "1.0.0"
    assert len(reporting["profiles"]) == 1
    profile = reporting["profiles"][0]
    assert profile["modelFileType"] == "总平模型"
    assert profile["strictNoNotApplicable"] is True
    assert len(profile["taskIds"]) == 15
    assert len(reporting["semanticRoles"]) == 13
    assert len(reporting["internalProperties"]) == 10
    mappings = [
        mapping
        for role in reporting["semanticRoles"]
        for mapping in role["attributeMappings"]
    ]
    assert len(mappings) == 37
    assert all("linkedCarrierRoleId" not in role for role in reporting["semanticRoles"])
    assert all(
        mapping["definitionSource"] in {"RULE_PROPERTY", "NATIVE_INTERNAL_EXTENSION"}
        for mapping in mappings
    )
    assert (
        reporting["officialAcceptancePropertyIds"]
        == EXPECTED_OFFICIAL_ACCEPTANCE_PROPERTY_IDS
    )
    properties_by_id = {item["propertyId"]: item for item in payload["properties"]}
    assert all(
        properties_by_id[property_id]["revit"]["parameterGuid"] == property_id
        and properties_by_id[property_id]["revit"]["bindingScope"] == "INSTANCE"
        and properties_by_id[property_id]["ifc"]["declaredType"]
            in {"IfcLabel", "IfcText", "IfcInteger", "IfcReal", "IfcDateTime"}
        for property_id in reporting["officialAcceptancePropertyIds"]
    )
    assert len(reporting["stage01FieldKeys"]) == 24
    assert len(reporting["planningTargetPropertyIds"]) == 10
    assert {m["propertyId"]: m["identity"] for m in reporting["stage02BMetrics"]} == METRICS
    assert all(not m["officialExportVerified"] for m in reporting["stage02BMetrics"])
    assert {item["checkId"] for item in reporting["systemChecks"]} == {
        "CROSS.DOCUMENT_IDENTITY", "CROSS.MODEL_PROFILE",
        "CROSS.RULE_PACKAGE", "CROSS.RESULT_FRESHNESS",
        "EXPORT.REVIT_DOCUMENT", "EXPORT.OUTPUT_DIRECTORY",
        "EXPORT.RAW_IFC_PIPELINE", "EXPORT.REPORT_WRITER",
    }


@pytest.mark.parametrize("case", [
    "duplicate_role", "duplicate_metric", "aliases_unsorted",
    "aliases_duplicate", "invalid_role_status", "orphan_carrier",
    "orphan_probe", "orphan_evidence", "derived_check_id_collision",
    "missing_attribute_mapping", "duplicate_attribute_mapping",
    "unknown_internal_property", "orphan_internal_property",
    "invalid_internal_uuid5", "profile_not_strict",
    "profile_task_count", "semantic_role_count",
    "missing_geometry_policy", "unknown_geometry_reference",
    "missing_property_policy", "unknown_property_policy_reference",
    "wrong_existing_attribute_property", "wrong_known_geometry_mode",
    "missing_geometry_subject", "wrong_existing_property_policy_target",
    "mutated_v042_overlay_property",
    "invalid_schema_version", "changed_metric_sequence",
    "invalid_internal_value_kind", "invalid_official_projection_policy",
    "duplicate_system_sequence", "mutated_role_alias",
    "substituted_stage01_internal_field", "planning_targets_reordered",
    "semantic_role_unproved_verified", "semantic_role_internal_only",
    "metric_internal_only", "official_policy_internal_only",
])
def test_invalid_native_reporting_overlay_is_rejected_atomically(tmp_path, case):
    overlay = json.loads(OVERLAY.read_text(encoding="utf-8"))
    reporting = overlay["nativeReporting"]
    metric = reporting["stage02BMetrics"][0]
    property_id = metric["propertyId"]

    if case == "duplicate_role":
        reporting["semanticRoles"].append(dict(reporting["semanticRoles"][0]))
    elif case == "duplicate_metric":
        reporting["stage02BMetrics"].append(dict(metric))
    elif case == "aliases_unsorted":
        role = reporting["semanticRoles"][0]
        role["candidateAliases"] = list(reversed(role["candidateAliases"]))
    elif case == "aliases_duplicate":
        role = reporting["semanticRoles"][0]
        role["candidateAliases"].append(role["candidateAliases"][0])
    elif case == "invalid_role_status":
        reporting["semanticRoles"][0]["officialCarrierStatus"] = "BOGUS"
    elif case == "missing_attribute_mapping":
        reporting["semanticRoles"][0]["attributeMappings"].pop()
    elif case == "duplicate_attribute_mapping":
        role = reporting["semanticRoles"][0]
        role["attributeMappings"].append(dict(role["attributeMappings"][0]))
    elif case == "unknown_internal_property":
        internal_mapping = next(
            mapping
            for role in reporting["semanticRoles"]
            for mapping in role["attributeMappings"]
            if mapping["definitionSource"] == "NATIVE_INTERNAL_EXTENSION"
        )
        internal_mapping["internalPropertyId"] = str(
            uuid.uuid5(uuid.NAMESPACE_DNS, "unknown-native-property"))
    elif case == "orphan_internal_property":
        orphan_id = reporting["internalProperties"][0]["propertyId"]
        replacement_id = reporting["internalProperties"][1]["propertyId"]
        orphan_mapping = next(
            mapping
            for role in reporting["semanticRoles"]
            for mapping in role["attributeMappings"]
            if mapping["internalPropertyId"] == orphan_id
        )
        orphan_mapping["internalPropertyId"] = replacement_id
    elif case == "invalid_internal_uuid5":
        reporting["internalProperties"][0]["propertyId"] = str(
            uuid.uuid5(uuid.NAMESPACE_DNS, "invalid-native-internal-id"))
    elif case == "profile_not_strict":
        reporting["profiles"][0]["strictNoNotApplicable"] = False
    elif case == "profile_task_count":
        reporting["profiles"][0]["taskIds"].pop()
    elif case == "semantic_role_count":
        reporting["semanticRoles"].pop()
    elif case == "missing_geometry_policy":
        reporting["geometryEvaluationPolicies"].pop()
    elif case == "unknown_geometry_reference":
        reporting["geometryEvaluationPolicies"][0]["referenceRoleId"] = "UNKNOWN_ROLE"
    elif case == "missing_property_policy":
        reporting["propertyEvaluationPolicies"].pop()
    elif case == "unknown_property_policy_reference":
        reporting["propertyEvaluationPolicies"][0]["propertyId"] = str(
            uuid.uuid5(uuid.NAMESPACE_DNS, "unknown-policy-property"))
    elif case == "wrong_existing_attribute_property":
        role = reporting["semanticRoles"][0]
        role["attributeMappings"][0]["internalPropertyId"] = (
            role["attributeMappings"][2]["internalPropertyId"]
        )
    elif case == "wrong_known_geometry_mode":
        policy = next(
            item for item in reporting["geometryEvaluationPolicies"]
            if item["mode"] == "AUTO_CLOSED_BOUNDARY"
        )
        policy["mode"] = "MANUAL_CURRENT_SNAPSHOT_REVIEW"
    elif case == "missing_geometry_subject":
        policy = next(
            item for item in reporting["geometryEvaluationPolicies"]
            if item["mode"] == "AUTO_CLOSED_BOUNDARY"
        )
        policy.pop("subjectRoleId")
    elif case == "wrong_existing_property_policy_target":
        policies = reporting["propertyEvaluationPolicies"]
        policies[0]["propertyId"] = policies[1]["propertyId"]
    elif case == "mutated_v042_overlay_property":
        overlay["properties"][0]["ifc"]["property"] = "篡改的绿地分类名称"
    elif case == "invalid_schema_version":
        reporting["schemaVersion"] = "9.9.9"
    elif case == "changed_metric_sequence":
        reporting["stage02BMetrics"][0]["sequence"] = 5
    elif case == "invalid_internal_value_kind":
        reporting["internalProperties"][0]["valueKind"] = "BOOLEAN"
    elif case == "invalid_official_projection_policy":
        reporting["officialCarrierPolicies"][0]["projectionPolicy"] = "ALLOW_ALWAYS"
    elif case == "duplicate_system_sequence":
        reporting["systemChecks"][1]["sequence"] = (
            reporting["systemChecks"][0]["sequence"]
        )
    elif case == "mutated_role_alias":
        reporting["semanticRoles"][0]["candidateAliases"][0] = "错误别名"
        reporting["semanticRoles"][0]["candidateAliases"].sort()
    elif case == "substituted_stage01_internal_field":
        index = reporting["stage01FieldKeys"].index("HBR|ProjectUnits|Angle")
        reporting["stage01FieldKeys"][index] = "HBR|Workflow|Version"
    elif case == "planning_targets_reordered":
        reporting["planningTargetPropertyIds"] = list(
            reversed(reporting["planningTargetPropertyIds"])
        )
    elif case == "semantic_role_unproved_verified":
        reporting["semanticRoles"][0]["officialCarrierStatus"] = "VERIFIED"
    elif case == "semantic_role_internal_only":
        reporting["semanticRoles"][0]["officialCarrierStatus"] = "INTERNAL_ONLY"
    elif case == "metric_internal_only":
        reporting["stage02BMetrics"][0]["officialCarrierStatus"] = "INTERNAL_ONLY"
    elif case == "official_policy_internal_only":
        reporting["officialCarrierPolicies"][0]["evidenceStatus"] = "INTERNAL_ONLY"
    elif case == "orphan_carrier":
        reporting["officialProjectionCarriers"].append({
            "carrierId": "OFFICIAL.ORPHAN.V1",
            "propertyId": property_id,
            "selectorKind": "PROJECT_INFORMATION",
            "roleId": "",
            "categoryBuiltInId": "",
            "elementClass": "Autodesk.Revit.DB.ProjectInfo",
            "bindingScope": "INSTANCE",
            "parameterGuid": property_id,
        })
    elif case == "orphan_probe":
        reporting["officialCarrierProbeRecords"].append({
            "probeId": "PROBE.ORPHAN.000000000000",
            "propertyId": property_id,
            "sourceGoldenRvtSha256": "0" * 64,
            "probeSeedManifestSha256": "1" * 64,
            "probeRvtSha256": "2" * 64,
            "probeIfcSha256": "3" * 64,
            "hifcToolManifestSha256": "4" * 64,
            "hifcToolDllSha256": "5" * 64,
            "hifcToolProductVersion": "1.0.0",
            "observedRevitUniqueId": "orphan-revit-unique-id",
            "observedIfcGlobalId": "orphan-ifc-global-id",
            "observedBindingScope": "INSTANCE",
            "observedParameterGuid": property_id,
            "observedSentinel": "700001.000001",
        })
    elif case == "orphan_evidence":
        reporting["officialEvidenceRecords"].append({
            "evidenceId": "EVIDENCE.ORPHAN.000000000000",
            "propertyId": property_id,
            "goldenRvtSha256": "0" * 64,
            "hifctoolManifestSha256": "1" * 64,
            "hifctoolDllSha256": "2" * 64,
            "hifctoolProductVersion": "1.0.0",
            "officialIfcSha256": "3" * 64,
            "ifcFluxProductVersion": "0.1.0",
            "ifcFluxReportSha256": "4" * 64,
            "observedRevitUniqueId": "orphan-revit-unique-id",
            "observedIfcGlobalId": "orphan-ifc-global-id",
            "observedBindingScope": "INSTANCE",
            "observedParameterGuid": property_id,
        })
    elif case == "derived_check_id_collision":
        reporting["systemChecks"].append({
            "sequence": 99999,
            "checkId": f"STAGE02B.METRIC.{property_id}",
            "displayName": "派生检查编号冲突",
            "sourceStage": "CROSS_STAGE",
            "applicableBasis": "负向合同测试",
            "remediationTarget": "RECHECK_ALL",
        })

    invalid_overlay = tmp_path / f"{case}.json"
    invalid_overlay.write_text(
        json.dumps(overlay, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    output = tmp_path / "existing.hbrpack"
    output.write_bytes(b"existing-output-must-survive")
    compiler = load_compiler()

    with pytest.raises(ValueError):
        compiler.compile_rulepack(SOURCE, BASELINE, invalid_overlay, output)

    assert output.read_bytes() == b"existing-output-must-survive"
    assert list(tmp_path.glob(f".{output.name}.*.tmp")) == []


def test_replace_failure_preserves_output_and_cleans_temporary_file(
    tmp_path, monkeypatch
):
    compiler = load_compiler()
    output = tmp_path / "existing.hbrpack"
    output.write_bytes(b"existing-output-must-survive")

    def fail_replace(source, destination):
        raise OSError("replace failed for atomic-write contract test")

    monkeypatch.setattr(compiler.os, "replace", fail_replace)
    with pytest.raises(OSError, match="atomic-write contract test"):
        compiler.compile_rulepack(SOURCE, BASELINE, OVERLAY, output)

    assert output.read_bytes() == b"existing-output-must-survive"
    assert list(tmp_path.glob(f".{output.name}.*.tmp")) == []


@pytest.mark.parametrize("output_input", ["source", "baseline", "overlay"])
def test_output_cannot_overwrite_an_authoritative_input(tmp_path, output_input):
    compiler = load_compiler()
    inputs = copy_compiler_inputs(tmp_path)
    original = {name: path.read_bytes() for name, path in inputs.items()}

    with pytest.raises(ValueError, match="different files"):
        compiler.compile_rulepack(
            inputs["source"], inputs["baseline"], inputs["overlay"],
            inputs[output_input],
        )

    assert {
        name: path.read_bytes() for name, path in inputs.items()
    } == original
    assert list(tmp_path.glob(".*.tmp")) == []


def test_output_hardlink_to_an_authoritative_input_is_rejected(tmp_path):
    compiler = load_compiler()
    inputs = copy_compiler_inputs(tmp_path)
    output = tmp_path / "hardlinked-output.hbrpack"
    try:
        os.link(inputs["overlay"], output)
    except OSError as error:
        pytest.skip(f"hard links unavailable: {error}")
    original_overlay = inputs["overlay"].read_bytes()

    with pytest.raises(ValueError, match="different files"):
        compiler.compile_rulepack(
            inputs["source"], inputs["baseline"], inputs["overlay"], output
        )

    assert inputs["overlay"].read_bytes() == original_overlay
    assert output.read_bytes() == original_overlay
    assert list(tmp_path.glob(".*.tmp")) == []


def test_duplicate_json_key_in_overlay_is_rejected_atomically(tmp_path):
    compiler = load_compiler()
    duplicate_key_overlay = tmp_path / "duplicate-key-overlay.json"
    duplicate_key_overlay.write_text(
        OVERLAY.read_text(encoding="utf-8").replace(
            '  "overlaySchemaVersion": "1.0.0",',
            '  "overlaySchemaVersion": "1.0.0",\n'
            '  "overlaySchemaVersion": "1.0.0",',
            1,
        ),
        encoding="utf-8",
    )
    output = tmp_path / "existing.hbrpack"
    output.write_bytes(b"existing-output-must-survive")

    with pytest.raises(ValueError, match="duplicate JSON key"):
        compiler.compile_rulepack(SOURCE, BASELINE, duplicate_key_overlay, output)

    assert output.read_bytes() == b"existing-output-must-survive"
    assert list(tmp_path.glob(f".{output.name}.*.tmp")) == []
