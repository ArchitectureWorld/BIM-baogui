import argparse
import hashlib
import importlib.util
import json
import os
import re
import sys
import tempfile
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
V042_COMPILER_PATH = ROOT / "tools" / "build_hbr_rulepack_v042.py"
V042_OVERLAY_PATH = (
    ROOT / "specs" / "hbr-rules" / "v1" / "source"
    / "hbr_rule_source.v0.4.2-overlay.json"
)
DEFAULT_OVERLAY = (
    ROOT / "specs" / "hbr-rules" / "v1" / "source"
    / "hbr_rule_source.v0.4.3-overlay.json"
)
EXPECTED_V042_OVERLAY_SHA256 = (
    "66cd7447bd8f17a9e0db973dc495ddedfa370121f46c043a39e97f3cc8437483"
)
EXPECTED_ATTRIBUTE_MAPPINGS_SHA256 = (
    "6d82bde0823720e62a7ccc6664213d8480b1ae09c56c8afdabea073dd97f2005"
)
EXPECTED_GEOMETRY_POLICIES_SHA256 = (
    "aa90feeb5c2f1d0046e30adb350968962655055ccc6c00c9044d2a8c79a0176a"
)
EXPECTED_PROPERTY_POLICIES_SHA256 = (
    "a1ecf6c6483ae7ea672a56ef8595e98eaa3fd7aa0a38f3c1d55cc13d647933c9"
)
EXPECTED_OFFICIAL_ACCEPTANCE_IDS_SHA256 = (
    "838c3981bb07ff6422ed1b7756b1e0d106e1247ad7b20300d11afdcbbae27a8b"
)
EXPECTED_PROFILES_SHA256 = (
    "d27c307eca9bcc0bda533dd68f62bba469c283164eefeb8cb6575210bb14952c"
)
EXPECTED_SEMANTIC_ROLE_STATIC_SHA256 = (
    "9ccbcff1cc7406e8f2a7496361d159d9b9d42902f172ed54f4eb160151196ce6"
)
EXPECTED_INTERNAL_PROPERTIES_SHA256 = (
    "163f7b79a4250c3b622a49b8e16d7cb61bfe071dddbe8f1db4800dc8a3fd3aed"
)
EXPECTED_METRIC_STATIC_SHA256 = (
    "a72387927021c92612f77bfde2835666f466becf3cd3f5dd38e70edbbaa4f207"
)
EXPECTED_OFFICIAL_POLICY_STATIC_SHA256 = (
    "2e44efd78d897bdb4e2d4a9049dbe0085ba0ce726fc4658eecdd25be27995817"
)
EXPECTED_STAGE01_FIELD_KEYS_SHA256 = (
    "8be9b68415a2a26576a429ed7eec4c43af8a988d3e7439ba3313aae2c8f2684b"
)
EXPECTED_PLANNING_TARGET_IDS_SHA256 = (
    "617ff75c3938e0975fc53de9c880614d236d1244ceb4025f9397dd1cd115c822"
)
EXPECTED_SYSTEM_CHECKS_SHA256 = (
    "2218c51f181ea23d920039b74961db6427dca2364a690b557b579c8e38cd39df"
)

def _load_json(path):
    def reject_duplicate_keys(pairs):
        value = {}
        for key, item in pairs:
            _require(
                key not in value,
                f"{path} contains duplicate JSON key {key!r}",
            )
            value[key] = item
        return value

    with Path(path).open(encoding="utf-8") as stream:
        return json.load(stream, object_pairs_hook=reject_duplicate_keys)

def _require(condition, message):
    if not condition:
        raise ValueError(message)

def _sha16(value):
    return hashlib.sha256(value.encode("utf-8")).hexdigest()[:16]


def _canonical_sha256(value):
    payload = json.dumps(
        value,
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
    ).encode("utf-8")
    return hashlib.sha256(payload).hexdigest()

def _load_v042_compiler():
    spec = importlib.util.spec_from_file_location(
        "hbr_rulepack_v042_compiler", V042_COMPILER_PATH
    )
    if spec is None or spec.loader is None:
        raise RuntimeError("cannot load v0.4.2 HBR rule-pack compiler")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module

def build_native_reporting_catalog(merged, overlay):
    reporting = overlay["nativeReporting"]
    _require(
        reporting["schemaVersion"] == "1.0.0",
        "native reporting schemaVersion must be 1.0.0",
    )
    properties = {item["propertyId"]: item for item in merged["properties"]}
    tasks = {item["taskId"]: item for item in merged["tasks"]}
    roles = {item["roleId"] for item in merged["carrierRoles"]}
    valid_official_statuses = {"VERIFIED", "PENDING_GOLDEN_RVT"}
    profiles = reporting["profiles"]
    _require(
        [item["modelFileType"] for item in profiles] == ["总平模型"],
        "phase1 native reporting must contain exactly the total-plan profile",
    )
    profile = profiles[0]
    _require(profile.get("strictNoNotApplicable") is True, "total-plan profile must be strict")
    _require(len(profile["taskIds"]) == 15, "total-plan profile must contain exactly 15 tasks")
    _require(
        _canonical_sha256(profiles) == EXPECTED_PROFILES_SHA256,
        "total-plan profile contract mismatch",
    )
    semantic_roles = reporting["semanticRoles"]
    semantic_role_id_list = [item["roleId"] for item in semantic_roles]
    _require(
        len(semantic_role_id_list) == len(set(semantic_role_id_list)),
        "duplicate semantic roleId",
    )
    _require(len(semantic_roles) == 13, "total-plan profile must contain exactly 13 semantic roles")
    semantic_role_ids = set(semantic_role_id_list)
    namespace = uuid.UUID(merged["guidNamespace"])
    internal_property_list = reporting["internalProperties"]
    internal_properties = {
        item["propertyId"]: item for item in internal_property_list
    }
    _require(
        len(internal_properties) == len(internal_property_list) == 10,
        "native internal properties must contain exactly 10 unique definitions",
    )
    for item in internal_properties.values():
        expected_id = str(uuid.uuid5(namespace, item["canonicalKey"]))
        _require(item["propertyId"] == expected_id, "native internal property UUIDv5 mismatch")
        _require(item["revit"]["parameterGuid"] == expected_id, "native internal parameter GUID mismatch")
        _require(item["revit"]["bindingScope"] == "INSTANCE", "native internal binding must be INSTANCE")
        _require(item["evidenceStatus"] == "INTERNAL_ONLY", "native internal evidence must be INTERNAL_ONLY")
        _require(item["officialExportVerified"] is False, "native internal property cannot be official")
    _require(
        _canonical_sha256(internal_property_list)
        == EXPECTED_INTERNAL_PROPERTIES_SHA256,
        "native internal property contract mismatch",
    )
    referenced_internal_extension_ids = set()
    metrics = reporting["stage02BMetrics"]
    metric_property_ids = [item["propertyId"] for item in metrics]
    _require(
        len(metric_property_ids) == len(set(metric_property_ids)),
        "duplicate 02B propertyId",
    )
    metric_sequences = [item["sequence"] for item in metrics]
    _require(
        metric_sequences == sorted(metric_sequences)
        and len(metric_sequences) == len(set(metric_sequences)),
        "02B metric sequence must be sorted and unique",
    )
    metric_static_contract = [
        {
            key: metric[key]
            for key in ("sequence", "propertyId", "identity", "source")
        }
        for metric in metrics
    ]
    _require(len(metrics) == 6, "02B catalog must contain exactly 6 metrics")
    _require(
        _canonical_sha256(metric_static_contract)
        == EXPECTED_METRIC_STATIC_SHA256,
        "02B metric static contract mismatch",
    )
    projection_carriers = {
        item["carrierId"]: item
        for item in reporting["officialProjectionCarriers"]
    }
    evidence_records = {
        item["evidenceId"]: item
        for item in reporting["officialEvidenceRecords"]
    }
    probe_records = {
        item["probeId"]: item
        for item in reporting["officialCarrierProbeRecords"]
    }
    _require(
        len(projection_carriers) == len(reporting["officialProjectionCarriers"]),
        "duplicate official projection carrierId",
    )
    _require(
        len(evidence_records) == len(reporting["officialEvidenceRecords"]),
        "duplicate official evidenceId",
    )
    _require(
        len(probe_records) == len(reporting["officialCarrierProbeRecords"]),
        "duplicate official carrier probeId",
    )
    for carrier in projection_carriers.values():
        _require(
            carrier["selectorKind"] in {
                "PROJECT_INFORMATION", "CONFIRMED_SEMANTIC_ROLE"
            },
            f"unsupported official selector: {carrier['carrierId']}",
        )
        _require(carrier["bindingScope"] == "INSTANCE", "official binding must be INSTANCE")
        _require(carrier["parameterGuid"] == carrier["propertyId"], "official parameter GUID mismatch")
        if carrier["selectorKind"] == "PROJECT_INFORMATION":
            _require(not carrier["roleId"] and not carrier["categoryBuiltInId"], "ProjectInformation selector cannot use role/category")
            _require(carrier["elementClass"] == "Autodesk.Revit.DB.ProjectInfo", "ProjectInformation class mismatch")
        else:
            _require(carrier["roleId"] in semantic_role_ids, "semantic selector role is unknown")
            _require(bool(carrier["categoryBuiltInId"].strip()), "semantic selector category missing")
            _require(bool(carrier["elementClass"].strip()), "semantic selector class missing")
    sha256 = re.compile(r"^[0-9a-f]{64}$")
    for evidence in evidence_records.values():
        for key in (
            "goldenRvtSha256", "hifctoolManifestSha256", "hifctoolDllSha256",
            "officialIfcSha256", "ifcFluxReportSha256",
        ):
            _require(bool(sha256.fullmatch(evidence[key])), f"invalid {key}: {evidence['evidenceId']}")
        for key in (
            "hifctoolProductVersion", "ifcFluxProductVersion",
            "observedRevitUniqueId", "observedIfcGlobalId",
        ):
            _require(bool(evidence[key].strip()), f"missing {key}: {evidence['evidenceId']}")
        _require(evidence["observedBindingScope"] == "INSTANCE", "evidence binding mismatch")
        _require(evidence["observedParameterGuid"] == evidence["propertyId"], "evidence parameter GUID mismatch")
    for probe in probe_records.values():
        for key in (
            "sourceGoldenRvtSha256", "probeSeedManifestSha256", "probeRvtSha256",
            "probeIfcSha256", "hifcToolManifestSha256", "hifcToolDllSha256",
        ):
            _require(bool(sha256.fullmatch(probe[key])), f"invalid probe {key}: {probe['probeId']}")
        for key in (
            "hifcToolProductVersion", "observedRevitUniqueId", "observedIfcGlobalId",
            "observedSentinel",
        ):
            _require(bool(probe[key].strip()), f"missing probe {key}: {probe['probeId']}")
        _require(probe["observedBindingScope"] == "INSTANCE", "probe binding mismatch")
        _require(probe["observedParameterGuid"] == probe["propertyId"], "probe parameter GUID mismatch")
    referenced_carrier_ids = set()
    referenced_probe_ids = set()
    referenced_evidence_ids = set()
    for metric in metrics:
        prop = properties.get(metric["propertyId"])
        _require(prop is not None, f"unknown 02B propertyId: {metric['propertyId']}")
        identity = "|".join([
            prop["ifc"]["entity"], prop["ifc"]["propertySet"],
            prop["ifc"]["property"],
        ])
        _require(identity == metric["identity"], f"02B identity mismatch: {metric['propertyId']}")
        _require(metric["source"] == "MANUAL_INPUT", "02B phase1 source must be manual")
        _require(isinstance(metric["officialExportVerified"], bool), "metric verified flag must be boolean")
        _require(
            metric["officialCarrierStatus"] in valid_official_statuses,
            f"invalid metric carrier status: {metric['propertyId']}",
        )
        if metric["officialCarrierStatus"] == "VERIFIED":
            carrier = projection_carriers.get(metric["officialProjectionCarrierId"])
            probe = probe_records.get(metric["officialCarrierProbeRef"])
            _require(carrier is not None, "verified metric carrier ref missing")
            _require(probe is not None, "verified metric carrier probe ref missing")
            _require(carrier["propertyId"] == metric["propertyId"], "carrier propertyId mismatch")
            _require(probe["propertyId"] == metric["propertyId"], "probe propertyId mismatch")
            referenced_carrier_ids.add(metric["officialProjectionCarrierId"])
            referenced_probe_ids.add(metric["officialCarrierProbeRef"])
            if metric["officialExportVerified"]:
                evidence = evidence_records.get(metric["officialEvidenceRef"])
                _require(evidence is not None, "verified export evidence ref missing")
                _require(evidence["propertyId"] == metric["propertyId"], "evidence propertyId mismatch")
                referenced_evidence_ids.add(metric["officialEvidenceRef"])
            else:
                _require(not metric["officialEvidenceRef"], "unverified export evidence ref must be empty")
        else:
            _require(metric["officialExportVerified"] is False, "unproved metric cannot be verified")
            _require(not metric["officialProjectionCarrierId"], "pending metric carrier ref must be empty")
            _require(not metric["officialCarrierProbeRef"], "pending metric probe ref must be empty")
            _require(not metric["officialEvidenceRef"], "pending metric evidence ref must be empty")
    _require(
        set(projection_carriers) == referenced_carrier_ids,
        "orphan or missing official projection carrier",
    )
    _require(
        set(probe_records) == referenced_probe_ids,
        "orphan or missing official carrier probe",
    )
    _require(
        set(evidence_records) == referenced_evidence_ids,
        "orphan or missing official evidence record",
    )
    for profile in profiles:
        _require(
            len(profile["taskIds"]) == len(set(profile["taskIds"])),
            f"duplicate profile taskId: {profile['modelFileType']}",
        )
        for task_id in profile["taskIds"]:
            _require(task_id in tasks, f"unknown reporting taskId: {task_id}")
    for role in semantic_roles:
        _require(role["taskId"] in tasks, f"unknown semantic taskId: {role['taskId']}")
        _require(
            role.get("internalCarrierStatus") == "INTERNAL_ONLY",
            f"semantic role internal status mismatch: {role['roleId']}",
        )
        aliases = role["candidateAliases"]
        _require(
            aliases == sorted(aliases) and len(aliases) == len(set(aliases)),
            f"candidateAliases must be ordinal-sorted and unique: {role['roleId']}",
        )
        _require(
            role["officialCarrierStatus"] == "PENDING_GOLDEN_RVT",
            f"semantic role carrier must remain pending until Golden RVT proof: {role['roleId']}",
        )
        mappings = role["attributeMappings"]
        mapping_keys = [item["attributeRequirement"] for item in mappings]
        _require(
            mapping_keys == tasks[role["taskId"]]["attributeRequirements"],
            f"attribute mapping must exactly cover task literals in order: {role['roleId']}",
        )
        _require(
            len(mapping_keys) == len(set(mapping_keys)),
            f"duplicate attribute mapping: {role['roleId']}",
        )
        for mapping in mappings:
            property_id = mapping["internalPropertyId"]
            source = mapping["definitionSource"]
            _require(
                source in {"RULE_PROPERTY", "NATIVE_INTERNAL_EXTENSION"},
                f"invalid attribute definition source: {role['roleId']}",
            )
            if source == "RULE_PROPERTY":
                _require(property_id in properties, f"unknown rule property mapping: {property_id}")
            else:
                _require(property_id in internal_properties, f"unknown native internal property: {property_id}")
                referenced_internal_extension_ids.add(property_id)
    _require(
        referenced_internal_extension_ids == set(internal_properties),
        "orphan or unused native internal property",
    )
    _require(
        sum(len(item["attributeMappings"]) for item in semantic_roles) == 37,
        "total-plan roles must contain exactly 37 attribute mappings",
    )
    static_attribute_mappings = [
        {
            "roleId": role["roleId"],
            "taskId": role["taskId"],
            **mapping,
        }
        for role in semantic_roles
        for mapping in role["attributeMappings"]
    ]
    _require(
        _canonical_sha256(static_attribute_mappings)
        == EXPECTED_ATTRIBUTE_MAPPINGS_SHA256,
        "total-plan attribute mapping contract mismatch",
    )
    semantic_role_static_contract = [
        {
            key: value
            for key, value in role.items()
            if key not in {"officialCarrierStatus", "attributeMappings"}
        }
        for role in semantic_roles
    ]
    _require(
        _canonical_sha256(semantic_role_static_contract)
        == EXPECTED_SEMANTIC_ROLE_STATIC_SHA256,
        "total-plan semantic role static contract mismatch",
    )
    geometry_policy_keys = [
        (item["taskId"], item["ruleText"])
        for item in reporting["geometryEvaluationPolicies"]
    ]
    expected_geometry_keys = [
        (task_id, rule_text)
        for task_id in profile["taskIds"]
        for rule_text in tasks[task_id]["geometryChecks"]
    ]
    _require(geometry_policy_keys == expected_geometry_keys, "geometry policies must exactly cover task geometry rules")
    valid_geometry_modes = {
        "AUTO_SHARED_COORDINATE", "AUTO_PLANAR_REFERENCE", "AUTO_CLOSED_BOUNDARY",
        "AUTO_NO_SELF_INTERSECTION", "AUTO_POSITIVE_AREA", "AUTO_CONTAINED_BY_ROLE",
        "AUTO_NO_DUPLICATE_GEOMETRY", "AUTO_CONTINUOUS_CURVE_CHAIN",
        "AUTO_MIN_SEGMENT_LENGTH", "MANUAL_CURRENT_SNAPSHOT_REVIEW",
    }
    for policy in reporting["geometryEvaluationPolicies"]:
        _require(policy["mode"] in valid_geometry_modes, f"unsupported geometry policy mode: {policy['mode']}")
        subject = policy.get("subjectRoleId", "")
        reference = policy.get("referenceRoleId", "")
        _require(not subject or subject in semantic_role_ids, f"unknown geometry subject role: {subject}")
        _require(not reference or reference in semantic_role_ids, f"unknown geometry reference role: {reference}")
        if policy["mode"] == "AUTO_MIN_SEGMENT_LENGTH":
            _require(policy.get("thresholdSource") == "REVIT_APPLICATION_SHORT_CURVE_TOLERANCE", "short-line threshold source mismatch")
    _require(
        _canonical_sha256(reporting["geometryEvaluationPolicies"])
        == EXPECTED_GEOMETRY_POLICIES_SHA256,
        "total-plan geometry policy contract mismatch",
    )
    property_policy_keys = [
        (item["taskId"], item["ruleText"])
        for item in reporting["propertyEvaluationPolicies"]
    ]
    expected_property_keys = [
        (task_id, rule_text)
        for task_id in profile["taskIds"]
        for rule_text in tasks[task_id]["propertyChecks"]
    ]
    _require(property_policy_keys == expected_property_keys, "property policies must exactly cover task property rules")
    _require(
        {item["mode"] for item in reporting["propertyEvaluationPolicies"]}
        == {"AUTO_PROJECTED_AREA_MATCH", "AUTO_GREEN_CONVERTED_AREA_FINITE"},
        "unexpected property evaluation policy mode",
    )
    for policy in reporting["propertyEvaluationPolicies"]:
        if policy["mode"] == "AUTO_PROJECTED_AREA_MATCH":
            _require(
                policy["propertyId"] in properties,
                f"unknown projected-area policy property: {policy['propertyId']}",
            )
        else:
            _require(
                policy["areaPropertyId"] in properties
                and policy["factorPropertyId"] in properties,
                "unknown green-area policy property",
            )
    _require(
        _canonical_sha256(reporting["propertyEvaluationPolicies"])
        == EXPECTED_PROPERTY_POLICIES_SHA256,
        "total-plan property policy contract mismatch",
    )
    policies = reporting["officialCarrierPolicies"]
    policy_entities = [item["ifcEntity"] for item in policies]
    _require(
        len(policy_entities) == len(set(policy_entities)),
        "duplicate official carrier policy entity",
    )
    controlled_entities = sorted(
        {item["identity"].split("|", 1)[0] for item in metrics}
    )
    _require(
        sorted(policy_entities) == controlled_entities,
        "official carrier policies must exactly cover 02B entities",
    )
    for policy in policies:
        _require(
            isinstance(policy["officialExportVerified"], bool),
            f"entity verified flag must be boolean: {policy['ifcEntity']}",
        )
        _require(
            policy["evidenceStatus"] in valid_official_statuses,
            f"invalid entity carrier status: {policy['ifcEntity']}",
        )
        entity_metrics = [
            item for item in metrics
            if item["identity"].split("|", 1)[0] == policy["ifcEntity"]
        ]
        expected_refs = sorted(
            item["officialEvidenceRef"] for item in entity_metrics
            if item["officialEvidenceRef"]
        )
        expected_probe_refs = sorted(
            item["officialCarrierProbeRef"] for item in entity_metrics
            if item["officialCarrierProbeRef"]
        )
        if policy["evidenceStatus"] == "VERIFIED":
            _require(entity_metrics and all(item["officialCarrierStatus"] == "VERIFIED" for item in entity_metrics), "entity carrier policy cannot outrun properties")
            _require(policy["probeRefs"] == expected_probe_refs, "entity probeRefs mismatch")
            _require(policy["officialExportVerified"] == all(item["officialExportVerified"] for item in entity_metrics), "entity export flag mismatch")
            _require(policy["evidenceRefs"] == expected_refs, "entity evidenceRefs mismatch")
        else:
            _require(policy["officialExportVerified"] is False, "pending entity cannot be verified")
            _require(policy["probeRefs"] == [], "pending entity probeRefs must be empty")
            _require(policy["evidenceRefs"] == [], "pending entity evidenceRefs must be empty")
    official_policy_static_contract = [
        {
            key: policy[key]
            for key in ("ifcEntity", "internalCarrier", "projectionPolicy")
        }
        for policy in policies
    ]
    _require(
        _canonical_sha256(official_policy_static_contract)
        == EXPECTED_OFFICIAL_POLICY_STATIC_SHA256,
        "official carrier policy static contract mismatch",
    )
    stage01_keys = {
        item["fieldKey"] for item in merged["stage01"]["fieldRefs"]
    } | {
        item["fieldKey"] for item in merged["stage01"]["internalWorkflowFields"]
    }
    _require(
        len(reporting["stage01FieldKeys"])
        == len(set(reporting["stage01FieldKeys"])),
        "duplicate Stage01 fieldKey",
    )
    _require(
        _canonical_sha256(reporting["stage01FieldKeys"])
        == EXPECTED_STAGE01_FIELD_KEYS_SHA256,
        "Stage01 field catalog mismatch",
    )
    for field_key in reporting["stage01FieldKeys"]:
        _require(field_key in stage01_keys, f"unknown Stage01 fieldKey: {field_key}")
    planning_target_ids = reporting["planningTargetPropertyIds"]
    _require(
        len(planning_target_ids) == len(set(planning_target_ids)),
        "duplicate planning target propertyId",
    )
    _require(
        _canonical_sha256(planning_target_ids)
        == EXPECTED_PLANNING_TARGET_IDS_SHA256,
        "planning target catalog mismatch",
    )
    for property_id in planning_target_ids:
        _require(property_id in properties, f"unknown planning target: {property_id}")
        _require(
            properties[property_id]["ifc"]["propertySet"] == "Pset_项目控制指标信息属性集",
            f"planning target identity mismatch: {property_id}",
        )
    field_property_ids = {
        item["fieldKey"]: item["propertyId"]
        for item in merged["stage01"]["fieldRefs"]
    }
    official_acceptance_ids = {
        field_property_ids[key]
        for key in reporting["stage01FieldKeys"]
        if key in field_property_ids
    }
    official_acceptance_ids.update(planning_target_ids)
    official_acceptance_ids.update(metric_property_ids)
    official_acceptance_ids.update(
        mapping["internalPropertyId"]
        for role in semantic_roles
        for mapping in role["attributeMappings"]
        if mapping["definitionSource"] == "RULE_PROPERTY"
    )
    official_acceptance_ids.update(
        item["propertyId"]
        for item in merged["properties"]
        if "SITE_GREEN_OBJECT" in item["carrierRoleIds"]
    )
    for property_id in official_acceptance_ids:
        prop = properties[property_id]
        _require(
            prop["revit"]["parameterGuid"] == property_id,
            f"official acceptance parameter GUID mismatch: {property_id}",
        )
        _require(
            prop["revit"]["bindingScope"] == "INSTANCE",
            f"official acceptance binding must be INSTANCE: {property_id}",
        )
        _require(
            prop["ifc"]["declaredType"]
            in {"IfcLabel", "IfcText", "IfcInteger", "IfcReal", "IfcDateTime"},
            f"unsupported official acceptance type: {property_id}",
        )
    official_acceptance_property_ids = sorted(official_acceptance_ids)
    _require(
        len(official_acceptance_property_ids) == 62,
        "official acceptance catalog must contain exactly 62 properties",
    )
    _require(
        _canonical_sha256(official_acceptance_property_ids)
        == EXPECTED_OFFICIAL_ACCEPTANCE_IDS_SHA256,
        "official acceptance property contract mismatch",
    )
    reporting["officialAcceptancePropertyIds"] = official_acceptance_property_ids
    system_check_ids = []
    for check in reporting["systemChecks"]:
        check_id = check.get("checkId")
        _require(
            isinstance(check_id, str) and bool(check_id.strip()),
            "systemChecks.checkId must be non-empty",
        )
        check_id = check_id.strip()
        system_check_ids.append(check_id)
        _require(
            check.get("sourceStage") in {"CROSS_STAGE", "EXPORT_PREPARATION"},
            f"invalid system check sourceStage: {check_id}",
        )
    _require(
        len(system_check_ids) == len(set(system_check_ids)),
        "duplicate system checkId",
    )
    system_check_sequences = [
        item["sequence"] for item in reporting["systemChecks"]
    ]
    _require(
        system_check_sequences == sorted(system_check_sequences)
        and len(system_check_sequences) == len(set(system_check_sequences)),
        "system check sequence must be sorted and unique",
    )
    _require(
        _canonical_sha256(reporting["systemChecks"])
        == EXPECTED_SYSTEM_CHECKS_SHA256,
        "system check catalog mismatch",
    )
    derived_check_ids = []
    derived_check_ids.extend(
        f"STAGE01.FIELD.{_sha16(value)}"
        for value in reporting["stage01FieldKeys"]
    )
    derived_check_ids.extend(
        f"STAGE01.TARGET.{value}" for value in planning_target_ids
    )
    derived_check_ids.extend(
        f"STAGE02A.ROLE.{value}" for value in semantic_role_id_list
    )
    for task_id in profiles[0]["taskIds"]:
        task = tasks[task_id]
        derived_check_ids.extend(
            f"STAGE02A.ATTRIBUTE.{task_id}.{_sha16(value)}"
            for value in task["attributeRequirements"]
        )
        derived_check_ids.extend(
            f"STAGE02A.GEOMETRY.{task_id}.{_sha16(value)}"
            for value in task["geometryChecks"]
        )
        derived_check_ids.extend(
            f"STAGE02A.PROPERTY.{task_id}.{_sha16(value)}"
            for value in task["propertyChecks"]
        )
        derived_check_ids.extend(
            f"STAGE03.TARGET.{task_id}.{value}"
            for value in task["targetComparisons"]
        )
    derived_check_ids.extend(
        f"STAGE02B.METRIC.{value}" for value in metric_property_ids
    )
    derived_check_ids.extend(system_check_ids)
    _require(
        len(derived_check_ids) == len(set(derived_check_ids)),
        "duplicate derived native reporting checkId",
    )
    return reporting

def merge_overlay(base_source, overlay):
    v042 = _load_v042_compiler()
    frozen_v042_overlay = _load_json(V042_OVERLAY_PATH)
    _require(
        _canonical_sha256(frozen_v042_overlay)
        == EXPECTED_V042_OVERLAY_SHA256,
        "frozen v0.4.2 overlay digest mismatch",
    )
    inherited_overlay = {
        key: value for key, value in overlay.items()
        if key != "nativeReporting"
    }
    _require(
        inherited_overlay == frozen_v042_overlay,
        "v0.4.3 must inherit the v0.4.2 overlay without changes",
    )
    v042._validate_overlay(overlay, base_source)
    merged = v042.merge_overlay(base_source, overlay)
    merged["nativeReporting"] = build_native_reporting_catalog(merged, overlay)
    return merged

def compile_rulepack(source_path, baseline_path, overlay_path, output_path):
    source_path = Path(source_path)
    baseline_path = Path(baseline_path)
    overlay_path = Path(overlay_path)
    output_path = Path(output_path)
    v042 = _load_v042_compiler()
    base = v042._load_base_compiler()
    for label, input_path in (
        ("source", source_path),
        ("baseline", baseline_path),
        ("overlay", overlay_path),
        ("frozen v0.4.2 overlay", V042_OVERLAY_PATH),
    ):
        _require(
            not base._paths_refer_to_same_file(input_path, output_path),
            f"{label} and output must refer to different files",
        )
    source = base.load_validated_rule_source(
        source_path, baseline_path
    )
    overlay = _load_json(overlay_path)
    merged = merge_overlay(source, overlay)
    payload = base.build_rulepack_bytes(merged)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary_name = tempfile.mkstemp(
        dir=str(output_path.parent),
        prefix=f".{output_path.name}.",
        suffix=".tmp",
    )
    try:
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(payload)
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
    parser = argparse.ArgumentParser(
        description="Compile HBR v0.4.3 native total-plan reporting rule-pack"
    )
    parser.add_argument("--source", required=True)
    parser.add_argument("--baseline", required=True)
    parser.add_argument("--overlay", default=str(DEFAULT_OVERLAY))
    parser.add_argument("--output", required=True)
    return parser

def main(argv=None):
    args = _parser().parse_args(argv)
    try:
        compile_rulepack(
            args.source, args.baseline, args.overlay, args.output
        )
    except (KeyError, OSError, TypeError, UnicodeError, ValueError, RuntimeError) as error:
        print(
            f"HBR v0.4.3 rule-pack compilation failed: {error}",
            file=sys.stderr,
        )
        return 1
    return 0

if __name__ == "__main__":
    raise SystemExit(main())
