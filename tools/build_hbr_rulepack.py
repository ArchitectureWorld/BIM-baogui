import argparse
import hashlib
import json
import os
import re
import struct
import sys
import tempfile
import uuid
from pathlib import Path


MAGIC = b"HBRP"
FORMAT_VERSION = 1

_TOP_LEVEL_FIELDS = {
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
_PROPERTY_FIELDS = {
    "propertyId",
    "canonicalKey",
    "contractKind",
    "source",
    "ifc",
    "revit",
    "officialPlugin",
    "carrierRoleIds",
    "requirement",
    "stageOwnership",
    "suggestion",
    "ifcWrite",
}
_SOURCE_FIELDS = {
    "artifact",
    "sheet",
    "row",
    "rawEntityCn",
    "rawEntityId",
    "rawIfcElementOrType",
    "rawPropertySetId",
    "rawPropertySetName",
    "rawProperty",
    "rawValueKind",
    "rawDeclaredType",
    "rawUnit",
}
_IFC_FIELDS = {
    "entity",
    "propertySet",
    "property",
    "sourceUnit",
    "declaredType",
    "canonicalUnit",
    "allowedRuntimeTypes",
}
_REVIT_FIELDS = {
    "parameterGuid",
    "parameterName",
    "legacyNames",
    "visible",
    "userModifiable",
    "bindingScope",
    "storageType",
    "parameterType",
}
_OFFICIAL_FIELDS = {"inExtracted166", "evidenceStatus", "originalIdentity"}
_LEGACY_PROJECTION_FIELDS = {
    "category",
    "carrier",
    "persistenceMode",
    "sharedParameterType",
    "officialSourceParameterGroup",
    "sourceParameterOverride",
}
_INTERNAL_WORKFLOW_FIELD_FIELDS = {
    "fieldKey",
    "label",
    "type",
    "uiGroup",
    "sourceKind",
    "allowedValues",
    "defaultValue",
}
_STAGE01_FIELD_REF_FIELDS = {
    "fieldKey",
    "propertyId",
    "sourceRow",
    "uiGroup",
    "sourceKind",
    "writeInStage01",
}
_ENTITY_POLICY_FIELDS = {
    "ifcEntity",
    "officialObjectMappingEvidence",
    "revitCarrier",
    "writePolicy",
    "officialExportVerified",
}
_OFFICIAL_PLUGIN_EXCEPTION_FIELDS = {"fieldKey", "reason"}
_REQUIREMENT_FIELDS = {"level", "conditionId"}
_SUGGESTION_FIELDS = {"kind", "aliases"}
_IFC_WRITE_FIELDS = {"writeStrategy", "ownerStrategy"}
_CARRIER_FIELDS = {
    "roleId",
    "displayName",
    "modelFileTypes",
    "ifcEntity",
    "revitCategories",
    "allowedElementKinds",
    "nameAliases",
    "familyAliases",
    "typeAliases",
    "cardinality",
    "selectionPolicy",
    "ifcOwnerStrategy",
}
_TASK_FIELDS = {
    "taskId",
    "modelFileType",
    "name",
    "objectCode",
    "requirement",
    "conditionId",
    "sequence",
    "skeletonTask",
    "attributeRequirements",
    "dependencies",
    "geometryChecks",
    "propertyChecks",
    "targetComparisons",
    "source",
}

_CONTRACT_KINDS = {"MVD", "HIFC_EXTENSION"}
_STAGES = {"STAGE01", "STAGE02", "STAGE03"}
_PROPERTY_REQUIREMENTS = {
    "REQUIRED",
    "CONDITIONAL",
    "OPTIONAL",
    "NOT_APPLICABLE",
    "UNCLASSIFIED",
}
_TASK_REQUIREMENTS = {"REQUIRED", "CONDITIONAL"}
_OWNER_STRATEGIES = {
    "SINGLE_ENTITY_BY_TYPE",
    "BY_EXPORT_GUID",
    "USER_SELECTED_EXPORTABLE_GENERIC_MODEL",
    "CANONICAL_SPATIAL_ZONE_RECORD",
}
_IFC_DECLARED_TYPES = {
    "IfcBoolean",
    "IfcDate",
    "IfcDateTime",
    "IfcInteger",
    "IfcLabel",
    "IfcReal",
    "IfcText",
}
_IFC_RUNTIME_TYPES = _IFC_DECLARED_TYPES | {
    "IfcAreaMeasure",
    "IfcLengthMeasure",
    "IfcPlaneAngleMeasure",
    "IfcVolumeMeasure",
}
_REAL_UNIT_RUNTIME_TYPES = {
    "m": "IfcLengthMeasure",
    "mm": "IfcLengthMeasure",
    "m2": "IfcAreaMeasure",
    "m3": "IfcVolumeMeasure",
    "deg": "IfcPlaneAngleMeasure",
}
_REAL_UNIT_PARAMETER_TYPES = {
    "m": "Length",
    "mm": "Length",
    "m2": "Area",
    "m3": "Volume",
    "deg": "Angle",
}
_STORAGE_TYPES = {"String", "Integer", "Double"}
_PARAMETER_TYPES = {
    "Text",
    "Integer",
    "Area",
    "Number",
    "Length",
    "Angle",
    "Volume",
    "YesNo",
}
_EXPECTED_EXTENSION_IDENTITIES = {
    ("IfcDoor", "Pset_门信息属性集", "开启方向"),
    ("IfcDuctSegment", "Pset_风管段信息属性集", "隔热层厚度"),
    ("IfcSpace", "Pset_建筑空间信息属性集", "空间形成方式"),
}
_MVD_ENTITIES = {
    "IfcProject",
    "IfcSite",
    "IfcBuilding",
    "IfcBuildingStorey",
    "IfcSpace",
    "IfcSpatialZone",
    "IfcWall",
    "IfcSlab",
    "IfcRoof",
    "IfcWindow",
    "IfcStairFlight",
    "IfcOrganization",
}
_EXPECTED_PROFILE_SIZES = {
    "总平模型": 15,
    "单体建筑—地上": 7,
    "单体建筑—地下": 6,
}
_EXPECTED_PROFILE_ACTIVATION_RULE_IDS = {
    "总平模型": [
        "HBR.SITE.BASE",
        "HBR.SITE.BUILDING_FOOTPRINT",
        "HBR.SITE.NET_LAND",
        "HBR.SITE.TOTAL_LAND",
        "HBR.TARGET.BUILDING_DENSITY",
        "HBR.TARGET.FLOOR_AREA_RATIO",
        "HBR.TARGET.GREEN_RATE",
    ],
    "单体建筑—地上": [
        "HBR.BUILDING.ABOVE.BASE",
        "HBR.BUILDING.ABOVE.BODY",
        "HBR.BUILDING.ABOVE.LEVELS",
        "HBR.TARGET.BUILDING_DENSITY",
        "HBR.TARGET.FLOOR_AREA_RATIO",
        "HBR.TARGET.GREEN_RATE",
    ],
    "单体建筑—地下": [
        "HBR.BUILDING.UNDERGROUND.BASE",
        "HBR.BUILDING.UNDERGROUND.BODY",
        "HBR.BUILDING.UNDERGROUND.LEVELS",
        "HBR.TARGET.BUILDING_DENSITY",
        "HBR.TARGET.FLOOR_AREA_RATIO",
        "HBR.TARGET.GREEN_RATE",
    ],
}
_SHARED_PARAMETER_TYPES = {
    "TEXT",
    "INTEGER",
    "AREA",
    "NUMBER",
    "LENGTH",
    "ANGLE",
    "VOLUME",
    "YESNO",
}
_COMPATIBILITY_BASELINE_FIELDS = {
    "schemaVersion",
    "baselineId",
    "baselineVersion",
    "workbookEvidence",
    "officialProperties",
    "legacyMetadataDigests",
}
_COMPATIBILITY_PROPERTY_FIELDS = {
    "propertyId",
    "canonicalKey",
    "parameterGuid",
    "originalIdentity",
}
_COMPATIBILITY_BASELINE_ID = "HBR-WUHAN-PLANNING-COMPATIBILITY"
_COMPATIBILITY_BASELINE_VERSION = "1.1.0"
_COMPATIBILITY_WORKBOOK_SOURCE = "《MVD》规划报建.xlsx"
_COMPATIBILITY_WORKBOOK_SHA256 = (
    "63fac01de41f3bd149e4e857a81256e623382bbe9b3437ed69a2b5ace90628e4"
)
_LEGACY_METADATA_DIGEST_NAMES = (
    "internalWorkflowFields",
    "stage01FieldMetadata",
    "officialLegacyProjection",
    "entityPolicies",
    "exceptions",
    "profileActivationRuleIds",
)
_LEGACY_PROJECTION_FIELDS = (
    "category",
    "carrier",
    "persistenceMode",
    "sharedParameterType",
    "officialSourceParameterGroup",
    "sourceParameterOverride",
)


def canonical_bytes(source):
    return json.dumps(
        source,
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
    ).encode("utf-8")


def _legacy_metadata_projections(source):
    stage01 = source["stage01"]
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
                        for key in _LEGACY_PROJECTION_FIELDS
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


def _legacy_metadata_digests(source):
    projections = _legacy_metadata_projections(source)
    return {
        name: hashlib.sha256(canonical_bytes(projections[name])).hexdigest()
        for name in _LEGACY_METADATA_DIGEST_NAMES
    }


def _require(condition, message):
    if not condition:
        raise ValueError(message)


def _require_unique(values, label):
    keys = [canonical_bytes(value) for value in values]
    _require(len(keys) == len(set(keys)), f"{label} values must be unique")


def _load_json_without_duplicate_keys(path, label):
    def reject_duplicate_keys(pairs):
        value = {}
        for key, item in pairs:
            _require(
                key not in value,
                f"{label} contains duplicate JSON key {key!r}",
            )
            value[key] = item
        return value

    with Path(path).open(encoding="utf-8") as stream:
        return json.load(stream, object_pairs_hook=reject_duplicate_keys)


def _expect_object(value, path, required, optional=()):
    _require(type(value) is dict, f"{path} must be an object")
    required = set(required)
    allowed = required | set(optional)
    missing = sorted(required - set(value))
    unexpected = sorted(set(value) - allowed)
    _require(not missing, f"{path} is missing required fields: {', '.join(missing)}")
    _require(
        not unexpected,
        f"{path} contains unexpected fields: {', '.join(unexpected)}",
    )


def _expect_array(value, path, minimum=0, unique=False):
    _require(type(value) is list, f"{path} must be an array")
    _require(len(value) >= minimum, f"{path} must contain at least {minimum} items")
    if unique:
        _require_unique(value, path)


def _expect_string(value, path, nonempty=False):
    _require(type(value) is str, f"{path} must be a string")
    if nonempty:
        _require(bool(value), f"{path} must be a non-empty string")


def _expect_nullable_string(value, path):
    _require(value is None or type(value) is str, f"{path} must be a string or null")


def _expect_integer(value, path):
    _require(type(value) is int, f"{path} must be an integer")


def _expect_boolean(value, path):
    _require(type(value) is bool, f"{path} must be a boolean")


def _expect_string_array(value, path, minimum=0):
    _expect_array(value, path, minimum=minimum, unique=True)
    for index, item in enumerate(value):
        _expect_string(item, f"{path}[{index}]")


def _validate_evidence_shape(evidence, path):
    _expect_object(
        evidence,
        path,
        required={"source"},
        optional={"sha256", "sheet", "range", "count"},
    )
    _expect_string(evidence["source"], f"{path}.source", nonempty=True)
    for key in ("sha256", "sheet", "range"):
        if key in evidence:
            _expect_string(evidence[key], f"{path}.{key}")
    if "count" in evidence:
        _expect_integer(evidence["count"], f"{path}.count")


def _validate_legacy_projection_shape(projection, path):
    _expect_object(projection, path, required=_LEGACY_PROJECTION_FIELDS)
    for key in _LEGACY_PROJECTION_FIELDS:
        _expect_string(projection[key], f"{path}.{key}")
    for key in (
        "carrier",
        "persistenceMode",
        "sharedParameterType",
        "officialSourceParameterGroup",
    ):
        _require(
            bool(projection[key]),
            f"{path}.{key} must be a non-empty string",
        )


def _validate_property_shape(rule, path):
    _expect_object(rule, path, required=_PROPERTY_FIELDS, optional={"extensionReason"})
    for key in ("propertyId", "canonicalKey", "contractKind"):
        _expect_string(rule[key], f"{path}.{key}", nonempty=True)

    raw = rule["source"]
    _expect_object(raw, f"{path}.source", required=_SOURCE_FIELDS)
    for key in _SOURCE_FIELDS - {"row"}:
        _expect_string(raw[key], f"{path}.source.{key}")
    for key in ("artifact", "sheet"):
        _require(bool(raw[key]), f"{path}.source.{key} must be non-empty")
    _require(
        raw["row"] is None or type(raw["row"]) is int,
        f"{path}.source.row must be an integer or null",
    )
    _require(
        raw["row"] is None or raw["row"] >= 2,
        f"{path}.source.row must be null or at least 2",
    )

    ifc = rule["ifc"]
    _expect_object(ifc, f"{path}.ifc", required=_IFC_FIELDS)
    for key in ("entity", "propertySet", "property", "declaredType"):
        _expect_string(ifc[key], f"{path}.ifc.{key}", nonempty=True)
    _expect_nullable_string(ifc["sourceUnit"], f"{path}.ifc.sourceUnit")
    _expect_nullable_string(ifc["canonicalUnit"], f"{path}.ifc.canonicalUnit")
    _expect_string_array(
        ifc["allowedRuntimeTypes"],
        f"{path}.ifc.allowedRuntimeTypes",
        minimum=1,
    )

    revit = rule["revit"]
    _expect_object(revit, f"{path}.revit", required=_REVIT_FIELDS)
    for key in (
        "parameterGuid",
        "parameterName",
        "bindingScope",
        "storageType",
        "parameterType",
    ):
        _expect_string(revit[key], f"{path}.revit.{key}", nonempty=True)
    _expect_string_array(revit["legacyNames"], f"{path}.revit.legacyNames", minimum=1)
    _expect_boolean(revit["visible"], f"{path}.revit.visible")
    _expect_boolean(revit["userModifiable"], f"{path}.revit.userModifiable")

    official = rule["officialPlugin"]
    _expect_object(
        official,
        f"{path}.officialPlugin",
        required=_OFFICIAL_FIELDS,
        optional={"legacyProjection"},
    )
    _expect_boolean(
        official["inExtracted166"],
        f"{path}.officialPlugin.inExtracted166",
    )
    _expect_string(
        official["evidenceStatus"],
        f"{path}.officialPlugin.evidenceStatus",
        nonempty=True,
    )
    _expect_nullable_string(
        official["originalIdentity"],
        f"{path}.officialPlugin.originalIdentity",
    )
    migrated_path = f"migrated metadata {path}.officialPlugin.legacyProjection"
    if official["inExtracted166"]:
        _require(
            "legacyProjection" in official,
            f"{migrated_path} is required for an official property",
        )
        _validate_legacy_projection_shape(
            official["legacyProjection"],
            migrated_path,
        )
    else:
        _require(
            "legacyProjection" not in official,
            f"{migrated_path} must be absent for a non-official property",
        )

    _expect_string_array(rule["carrierRoleIds"], f"{path}.carrierRoleIds", minimum=1)

    requirement = rule["requirement"]
    _expect_object(requirement, f"{path}.requirement", required=_REQUIREMENT_FIELDS)
    _expect_string(requirement["level"], f"{path}.requirement.level", nonempty=True)
    _expect_nullable_string(
        requirement["conditionId"],
        f"{path}.requirement.conditionId",
    )

    _expect_string_array(rule["stageOwnership"], f"{path}.stageOwnership", minimum=1)

    suggestion = rule["suggestion"]
    _expect_object(suggestion, f"{path}.suggestion", required=_SUGGESTION_FIELDS)
    _expect_string(suggestion["kind"], f"{path}.suggestion.kind", nonempty=True)
    _expect_string_array(suggestion["aliases"], f"{path}.suggestion.aliases", minimum=1)

    ifc_write = rule["ifcWrite"]
    _expect_object(ifc_write, f"{path}.ifcWrite", required=_IFC_WRITE_FIELDS)
    _expect_string(
        ifc_write["writeStrategy"],
        f"{path}.ifcWrite.writeStrategy",
        nonempty=True,
    )
    _expect_string(
        ifc_write["ownerStrategy"],
        f"{path}.ifcWrite.ownerStrategy",
        nonempty=True,
    )
    if "extensionReason" in rule:
        _expect_string(
            rule["extensionReason"],
            f"{path}.extensionReason",
            nonempty=True,
        )


def _validate_carrier_shape(role, path):
    _expect_object(role, path, required=_CARRIER_FIELDS)
    for key in (
        "roleId",
        "displayName",
        "ifcEntity",
        "selectionPolicy",
        "ifcOwnerStrategy",
    ):
        _expect_string(role[key], f"{path}.{key}", nonempty=True)
    for key, minimum in (
        ("modelFileTypes", 1),
        ("revitCategories", 1),
        ("allowedElementKinds", 1),
        ("nameAliases", 0),
        ("familyAliases", 0),
        ("typeAliases", 0),
    ):
        _expect_string_array(role[key], f"{path}.{key}", minimum=minimum)
    cardinality = role["cardinality"]
    _expect_object(cardinality, f"{path}.cardinality", required={"min", "max"})
    _expect_integer(cardinality["min"], f"{path}.cardinality.min")
    _require(
        cardinality["max"] is None or type(cardinality["max"]) is int,
        f"{path}.cardinality.max must be an integer or null",
    )


def _validate_task_shape(task, path):
    _expect_object(task, path, required=_TASK_FIELDS)
    for key in (
        "taskId",
        "modelFileType",
        "name",
        "objectCode",
        "requirement",
        "source",
    ):
        _expect_string(task[key], f"{path}.{key}")
    _expect_nullable_string(task["conditionId"], f"{path}.conditionId")
    _expect_integer(task["sequence"], f"{path}.sequence")
    _expect_boolean(task["skeletonTask"], f"{path}.skeletonTask")
    for key in (
        "attributeRequirements",
        "dependencies",
        "geometryChecks",
        "propertyChecks",
        "targetComparisons",
    ):
        _expect_string_array(task[key], f"{path}.{key}")


def _validate_structure(source):
    _expect_object(source, "source", required=_TOP_LEVEL_FIELDS)
    for key in ("schemaVersion", "packageId", "packageVersion", "guidNamespace"):
        _expect_string(source[key], key, nonempty=True)

    for key in (
        "evidenceSources",
        "properties",
        "carrierRoles",
        "modelProfiles",
        "conditions",
        "tasks",
        "legacyAliases",
    ):
        _expect_array(source[key], key, minimum=1, unique=True)

    for index, evidence in enumerate(source["evidenceSources"]):
        _validate_evidence_shape(evidence, f"evidenceSources[{index}]")
    for index, rule in enumerate(source["properties"]):
        _validate_property_shape(rule, f"properties[{index}]")
    for index, role in enumerate(source["carrierRoles"]):
        _validate_carrier_shape(role, f"carrierRoles[{index}]")

    for index, profile in enumerate(source["modelProfiles"]):
        path = f"modelProfiles[{index}]"
        migrated_path = f"migrated metadata {path}"
        _expect_object(
            profile,
            migrated_path,
            required={"profileId", "taskIds", "activationRuleIds"},
        )
        _expect_string(profile["profileId"], f"{path}.profileId", nonempty=True)
        _expect_string_array(profile["taskIds"], f"{path}.taskIds", minimum=1)
        _expect_string_array(
            profile["activationRuleIds"],
            f"{migrated_path}.activationRuleIds",
            minimum=1,
        )

    for index, condition in enumerate(source["conditions"]):
        path = f"conditions[{index}]"
        _expect_object(
            condition,
            path,
            required={
                "conditionId",
                "displayName",
                "group",
                "activationRuleId",
                "evidenceStatus",
                "source",
            },
        )
        for key in ("conditionId", "displayName", "group", "evidenceStatus", "source"):
            _expect_string(condition[key], f"{path}.{key}")
        _expect_nullable_string(condition["activationRuleId"], f"{path}.activationRuleId")

    for index, task in enumerate(source["tasks"]):
        _validate_task_shape(task, f"tasks[{index}]")

    for index, alias in enumerate(source["legacyAliases"]):
        path = f"legacyAliases[{index}]"
        _expect_object(alias, path, required={"propertyId", "alias"})
        _expect_string(alias["propertyId"], f"{path}.propertyId", nonempty=True)
        _expect_string(alias["alias"], f"{path}.alias", nonempty=True)

    stage01 = source["stage01"]
    migrated_stage01 = "migrated metadata stage01"
    _expect_object(
        stage01,
        migrated_stage01,
        required={
            "fieldRefs",
            "internalWorkflowFields",
            "officialPluginCompatibility",
        },
    )
    _expect_array(stage01["fieldRefs"], "stage01.fieldRefs", minimum=1, unique=True)
    for index, reference in enumerate(stage01["fieldRefs"]):
        path = f"{migrated_stage01}.fieldRefs[{index}]"
        _expect_object(reference, path, required=_STAGE01_FIELD_REF_FIELDS)
        _expect_string(reference["fieldKey"], f"{path}.fieldKey", nonempty=True)
        _expect_string(reference["propertyId"], f"{path}.propertyId", nonempty=True)
        _expect_integer(reference["sourceRow"], f"{path}.sourceRow")
        _expect_string(reference["uiGroup"], f"{path}.uiGroup", nonempty=True)
        _expect_string(reference["sourceKind"], f"{path}.sourceKind", nonempty=True)
        _expect_boolean(reference["writeInStage01"], f"{path}.writeInStage01")

    internal_fields = stage01["internalWorkflowFields"]
    _expect_array(
        internal_fields,
        f"{migrated_stage01}.internalWorkflowFields",
        minimum=1,
    )
    for index, field in enumerate(internal_fields):
        path = f"{migrated_stage01}.internalWorkflowFields[{index}]"
        _expect_object(field, path, required=_INTERNAL_WORKFLOW_FIELD_FIELDS)
        for key in ("fieldKey", "label", "type", "uiGroup", "sourceKind"):
            _expect_string(field[key], f"{path}.{key}", nonempty=True)
        _expect_string_array(field["allowedValues"], f"{path}.allowedValues")
        _expect_nullable_string(field["defaultValue"], f"{path}.defaultValue")

    compatibility = stage01["officialPluginCompatibility"]
    compatibility_path = f"{migrated_stage01}.officialPluginCompatibility"
    _expect_object(
        compatibility,
        compatibility_path,
        required={"entityPolicies", "exceptions"},
    )
    _expect_array(
        compatibility["entityPolicies"],
        f"{compatibility_path}.entityPolicies",
        minimum=1,
    )
    for index, policy in enumerate(compatibility["entityPolicies"]):
        path = f"{compatibility_path}.entityPolicies[{index}]"
        _expect_object(policy, path, required=_ENTITY_POLICY_FIELDS)
        for key in ("ifcEntity", "officialObjectMappingEvidence", "writePolicy"):
            _expect_string(policy[key], f"{path}.{key}", nonempty=True)
        _expect_string(policy["revitCarrier"], f"{path}.revitCarrier")
        _expect_boolean(
            policy["officialExportVerified"],
            f"{path}.officialExportVerified",
        )
    _expect_array(
        compatibility["exceptions"],
        f"{compatibility_path}.exceptions",
        minimum=1,
    )
    for index, exception in enumerate(compatibility["exceptions"]):
        path = f"{compatibility_path}.exceptions[{index}]"
        _expect_object(
            exception,
            path,
            required=_OFFICIAL_PLUGIN_EXCEPTION_FIELDS,
        )
        _expect_string(exception["fieldKey"], f"{path}.fieldKey", nonempty=True)
        _expect_string(exception["reason"], f"{path}.reason", nonempty=True)
        _require(
            bool(exception["reason"].strip()),
            f"{path}.reason must be non-empty after trimming",
        )


def _parse_uuid5(value, label):
    try:
        parsed = uuid.UUID(value)
    except (AttributeError, TypeError, ValueError) as error:
        raise ValueError(f"{label} must be a UUID: {value!r}") from error
    _require(str(parsed) == value, f"{label} must use canonical lowercase UUID format")
    _require(parsed.version == 5, f"{label} must be a UUID version 5 value")
    return parsed


def _validate_requirement(requirement, condition_ids, label, allowed_levels):
    level = requirement["level"]
    condition_id = requirement["conditionId"]
    _require(level in allowed_levels, f"{label}.level has unsupported value {level!r}")
    if level == "CONDITIONAL":
        _require(
            type(condition_id) is str and bool(condition_id),
            f"{label}.conditionId must be a non-empty string for CONDITIONAL",
        )
        _require(
            condition_id in condition_ids,
            f"{label}.conditionId references unknown condition {condition_id!r}",
        )
    else:
        _require(
            condition_id is None,
            f"{label}.conditionId must be null when level is not CONDITIONAL",
        )


def _validate_runtime_contract(rule, label):
    ifc = rule["ifc"]
    revit = rule["revit"]
    declared_type = ifc["declaredType"]
    allowed = set(ifc["allowedRuntimeTypes"])
    unit = ifc["canonicalUnit"]

    _require(
        declared_type in _IFC_DECLARED_TYPES,
        f"{label}.ifc.declaredType has unsupported value {declared_type!r}",
    )
    _require(
        allowed <= _IFC_RUNTIME_TYPES,
        f"{label}.ifc.allowedRuntimeTypes contains unsupported runtime types",
    )

    expected_runtime_types = {declared_type}
    if declared_type == "IfcReal" and unit in _REAL_UNIT_RUNTIME_TYPES:
        expected_runtime_types.add(_REAL_UNIT_RUNTIME_TYPES[unit])
    _require(
        allowed == expected_runtime_types,
        f"{label}.ifc.allowedRuntimeTypes must be {sorted(expected_runtime_types)!r}",
    )

    _require(
        revit["storageType"] in _STORAGE_TYPES,
        f"{label}.revit.storageType has unsupported value {revit['storageType']!r}",
    )
    _require(
        revit["parameterType"] in _PARAMETER_TYPES,
        f"{label}.revit.parameterType has unsupported value {revit['parameterType']!r}",
    )
    if declared_type == "IfcReal":
        expected_storage = "Double"
        expected_parameter = _REAL_UNIT_PARAMETER_TYPES.get(unit, "Number")
    elif declared_type == "IfcInteger":
        expected_storage = "Integer"
        expected_parameter = "Integer"
    elif declared_type == "IfcBoolean":
        expected_storage = "Integer"
        expected_parameter = "YesNo"
    else:
        expected_storage = "String"
        expected_parameter = "Text"
    _require(
        revit["storageType"] == expected_storage,
        f"{label}.revit.storageType must be {expected_storage!r}",
    )
    _require(
        revit["parameterType"] == expected_parameter,
        f"{label}.revit.parameterType must be {expected_parameter!r}",
    )


def validate_semantics(source):
    _validate_structure(source)

    _require(source["schemaVersion"] == "1.0.0", "schemaVersion must be 1.0.0")
    _require(
        source["packageId"] == "HBR-WUHAN-PLANNING",
        "packageId must be HBR-WUHAN-PLANNING",
    )
    _require(source["packageVersion"] == "1.0.0", "packageVersion must be 1.0.0")
    namespace = _parse_uuid5(source["guidNamespace"], "guidNamespace")

    properties = source["properties"]
    carriers = source["carrierRoles"]
    profiles = source["modelProfiles"]
    conditions = source["conditions"]
    task_list = source["tasks"]
    aliases = source["legacyAliases"]
    stage01 = source["stage01"]
    stage_refs = stage01["fieldRefs"]
    internal_fields = stage01["internalWorkflowFields"]
    compatibility = stage01["officialPluginCompatibility"]
    entity_policies = compatibility["entityPolicies"]
    plugin_exceptions = compatibility["exceptions"]
    evidence_sources = source["evidenceSources"]

    _require(len(properties) == 359, "properties must contain exactly 359 rules")
    _require(len(carriers) == 14, "carrierRoles must contain exactly 14 records")
    _require(len(profiles) == 3, "modelProfiles must contain exactly 3 records")
    _require(len(conditions) == 14, "conditions must contain exactly 14 records")
    _require(len(task_list) == 28, "tasks must contain exactly 28 records")
    _require(len(aliases) == 166, "legacyAliases must contain exactly 166 records")
    _require(len(stage_refs) == 102, "stage01.fieldRefs must contain exactly 102 records")
    _require(
        len(internal_fields) == 12,
        "migrated metadata internalWorkflowFields must contain exactly 12 records",
    )
    _require(
        len(entity_policies) == 9,
        "migrated metadata entityPolicies must contain exactly 9 records",
    )
    _require(
        len(plugin_exceptions) == 13,
        "migrated metadata exceptions must contain exactly 13 records",
    )
    _require(
        len(evidence_sources) == 3,
        "evidenceSources must contain exactly 3 records",
    )

    internal_field_keys = [field["fieldKey"] for field in internal_fields]
    _require_unique(
        internal_field_keys,
        "migrated metadata internalWorkflowFields.fieldKey",
    )
    for index, field in enumerate(internal_fields):
        label = f"migrated metadata internalWorkflowFields[{index}]"
        _require(
            field["type"] in {"string", "enum", "guid", "number", "boolean"},
            f"{label}.type has an unsupported value",
        )
        if field["type"] == "enum":
            _require(
                bool(field["allowedValues"]),
                f"{label}.allowedValues must be non-empty for enum fields",
            )
        if field["defaultValue"] is not None and field["allowedValues"]:
            _require(
                field["defaultValue"] in field["allowedValues"],
                f"{label}.defaultValue must belong to allowedValues",
            )

    mvd_rules = [rule for rule in properties if rule["contractKind"] == "MVD"]
    extension_rules = [
        rule for rule in properties if rule["contractKind"] == "HIFC_EXTENSION"
    ]
    _require(
        all(rule["contractKind"] in _CONTRACT_KINDS for rule in properties),
        "contractKind contains an unsupported enum value",
    )
    _require(
        len(mvd_rules) == 356 and len(extension_rules) == 3,
        "properties must contain exactly 356 MVD and 3 HIFC_EXTENSION rules",
    )

    official_rules = [
        rule for rule in properties if rule["officialPlugin"]["inExtracted166"]
    ]
    _require(len(official_rules) == 166, "officialPlugin set must contain exactly 166 rules")
    _require(
        sum(
            rule["officialPlugin"]["legacyProjection"]["category"] == ""
            for rule in official_rules
        )
        == 25,
        "migrated metadata legacyProjection must preserve exactly 25 empty categories",
    )
    _require(
        all(
            rule["officialPlugin"]["legacyProjection"][
                "sourceParameterOverride"
            ]
            == ""
            for rule in official_rules
        ),
        "migrated metadata legacyProjection must preserve all 166 empty sourceParameterOverride values",
    )
    _require(
        sum(rule["contractKind"] == "MVD" for rule in official_rules) == 163,
        "officialPlugin set must contain exactly 163 MVD rules",
    )
    _require(
        sum(
            rule["contractKind"] == "MVD"
            and not rule["officialPlugin"]["inExtracted166"]
            for rule in properties
        )
        == 193,
        "non-official MVD set must contain exactly 193 rules",
    )

    extension_identities = {
        (rule["ifc"]["entity"], rule["ifc"]["propertySet"], rule["ifc"]["property"])
        for rule in extension_rules
    }
    _require(
        extension_identities == _EXPECTED_EXTENSION_IDENTITIES,
        "HIFC_EXTENSION identities must match the three verified extensions",
    )
    _require(
        all(bool(rule.get("extensionReason")) for rule in extension_rules),
        "every HIFC_EXTENSION rule must have a non-empty extensionReason",
    )

    property_ids = [rule["propertyId"] for rule in properties]
    canonical_keys = [rule["canonicalKey"] for rule in properties]
    parameter_guids = [rule["revit"]["parameterGuid"] for rule in properties]
    _require_unique(property_ids, "propertyId")
    _require_unique(canonical_keys, "canonicalKey")
    _require_unique(parameter_guids, "parameterGuid")

    for index, rule in enumerate(properties):
        label = f"properties[{index}]"
        property_id = rule["propertyId"]
        parameter_guid = rule["revit"]["parameterGuid"]
        _parse_uuid5(property_id, f"{label}.propertyId")
        _parse_uuid5(parameter_guid, f"{label}.revit.parameterGuid")
        expected_id = str(uuid.uuid5(namespace, rule["canonicalKey"]))
        _require(
            property_id == expected_id,
            f"{label}.propertyId must equal UUIDv5(guidNamespace, canonicalKey)",
        )
        _require(
            parameter_guid == property_id,
            f"{label}.revit.parameterGuid must equal propertyId",
        )

    mvd_identities = [
        (rule["ifc"]["entity"], rule["ifc"]["propertySet"], rule["ifc"]["property"])
        for rule in mvd_rules
    ]
    _require_unique(mvd_identities, "MVD IFC identity")
    _require(
        len(set(mvd_identities)) == 356,
        "MVD IFC identity set must contain exactly 356 identities",
    )
    all_property_identities = [
        (
            rule["ifc"]["entity"],
            rule["ifc"]["propertySet"]
            if rule["ifc"]["propertySet"].startswith("Pset_")
            else f"Pset_{rule['ifc']['propertySet']}",
            rule["ifc"]["property"],
        )
        for rule in properties
    ]
    _require_unique(all_property_identities, "all property IFC identity")
    _require(
        {rule["ifc"]["entity"] for rule in mvd_rules} <= _MVD_ENTITIES,
        "MVD ifc.entity contains an unsupported value",
    )
    mvd_rows = [rule["source"]["row"] for rule in mvd_rules]
    _require(
        set(mvd_rows) == set(range(2, 358)) and len(mvd_rows) == 356,
        "MVD source.row values must be unique and cover rows 2 through 357",
    )

    carrier_ids = [role["roleId"] for role in carriers]
    condition_ids_list = [condition["conditionId"] for condition in conditions]
    task_ids_list = [task["taskId"] for task in task_list]
    profile_ids_list = [profile["profileId"] for profile in profiles]
    _require_unique(carrier_ids, "carrierRoles.roleId")
    _require_unique(condition_ids_list, "conditions.conditionId")
    _require_unique(task_ids_list, "tasks.taskId")
    _require_unique(profile_ids_list, "modelProfiles.profileId")

    roles_by_id = {role["roleId"]: role for role in carriers}
    condition_ids = set(condition_ids_list)
    tasks = {task["taskId"]: task for task in task_list}
    profile_ids = set(profile_ids_list)

    _require(
        {profile["profileId"]: len(profile["taskIds"]) for profile in profiles}
        == _EXPECTED_PROFILE_SIZES,
        "modelProfiles must contain the verified 15/7/6 task partitions",
    )
    known_fixed_activation_ids = {
        rule_id
        for rule_ids in _EXPECTED_PROFILE_ACTIVATION_RULE_IDS.values()
        for rule_id in rule_ids
    }
    for index, profile in enumerate(profiles):
        label = f"migrated metadata modelProfiles[{index}].activationRuleIds"
        for rule_id in profile["activationRuleIds"]:
            _require(
                rule_id in known_fixed_activation_ids,
                f"{label} contains an unknown activation rule reference {rule_id!r}",
            )
        _require(
            profile["activationRuleIds"]
            == _EXPECTED_PROFILE_ACTIVATION_RULE_IDS[profile["profileId"]],
            f"{label} must match the verified fixed activation output",
        )

    for index, role in enumerate(carriers):
        label = f"carrierRoles[{index}]"
        strategy = role["ifcOwnerStrategy"]
        _require(
            strategy in _OWNER_STRATEGIES,
            f"{label}.ifcOwnerStrategy has unsupported value {strategy!r}",
        )
        cardinality = role["cardinality"]
        _require(cardinality["min"] == 0, f"{label}.cardinality.min must be 0")
        maximum = cardinality["max"]
        _require(
            maximum is None or maximum >= cardinality["min"],
            f"{label}.cardinality.max must be null or greater than or equal to min",
        )
        if role["ifcEntity"] in {"IfcProject", "IfcSite", "IfcBuilding"}:
            _require(maximum == 1, f"{label}.cardinality.max must be 1")
        else:
            _require(maximum is None, f"{label}.cardinality.max must be null")
        _require(
            set(role["modelFileTypes"]) <= profile_ids,
            f"{label}.modelFileTypes contains an unknown profile reference",
        )

    referenced_carrier_ids = set()
    referenced_condition_ids = set()
    for index, rule in enumerate(properties):
        label = f"properties[{index}]"
        raw = rule["source"]
        ifc = rule["ifc"]
        revit = rule["revit"]
        official = rule["officialPlugin"]
        requirement = rule["requirement"]
        ifc_write = rule["ifcWrite"]

        _require(ifc["propertySet"].startswith("Pset_"), f"{label}.ifc.propertySet must start with Pset_")
        _require(ifc["entity"] == raw["rawEntityId"], f"{label}.ifc.entity must match source.rawEntityId")
        _require(
            ifc["propertySet"] == raw["rawPropertySetId"],
            f"{label}.ifc.propertySet must match source.rawPropertySetId",
        )
        _require(ifc["property"] == raw["rawProperty"], f"{label}.ifc.property must match source.rawProperty")
        expected_source_unit = None if raw["rawUnit"] in {"", "14"} else raw["rawUnit"]
        _require(
            ifc["sourceUnit"] == expected_source_unit,
            f"{label}.ifc.sourceUnit must match normalized source.rawUnit",
        )
        if raw["rawDeclaredType"].casefold() == "ifctext":
            _require(ifc["declaredType"] == "IfcText", f"{label}.ifc.declaredType must normalize IfcText spelling")
        if rule["contractKind"] == "MVD":
            _require(
                all(raw[key] != "14" for key in ("rawValueKind", "rawUnit", "rawIfcElementOrType")),
                f"{label}.source contains a style sentinel value",
            )
            _require(
                all(ifc[key] != "14" for key in ("sourceUnit", "canonicalUnit", "declaredType")),
                f"{label}.ifc contains a style sentinel value",
            )
            _require(
                "14" not in ifc["entity"]
                and "/" not in ifc["entity"]
                and "14" not in rule["canonicalKey"]
                and "/" not in rule["canonicalKey"],
                f"{label} contains a polluted entity or canonicalKey",
            )

        _require(revit["visible"] is True, f"{label}.revit.visible must be true")
        _require(
            revit["userModifiable"] is True,
            f"{label}.revit.userModifiable must be true",
        )
        _require(revit["bindingScope"] == "INSTANCE", f"{label}.revit.bindingScope must be INSTANCE")
        pset_name = raw["rawPropertySetName"].replace("Pset_", "")
        expected_parameter_name = f"HBR｜{pset_name}｜{raw['rawProperty']}"
        legacy_name = f"HIFC.{pset_name}.{raw['rawProperty']}"
        _require(
            revit["parameterName"] == expected_parameter_name,
            f"{label}.revit.parameterName must match the canonical HBR name",
        )
        _require(legacy_name in revit["legacyNames"], f"{label}.revit.legacyNames must include {legacy_name!r}")

        _validate_requirement(
            requirement,
            condition_ids,
            f"{label}.requirement",
            _PROPERTY_REQUIREMENTS,
        )
        if requirement["conditionId"] is not None:
            referenced_condition_ids.add(requirement["conditionId"])

        _require(
            set(rule["stageOwnership"]) <= _STAGES,
            f"{label}.stageOwnership contains an unsupported enum value",
        )
        _require(
            rule["suggestion"]["kind"] == "EXISTING_OR_ALIAS",
            f"{label}.suggestion.kind must be EXISTING_OR_ALIAS",
        )
        _require(
            legacy_name in rule["suggestion"]["aliases"]
            and raw["rawProperty"] in rule["suggestion"]["aliases"],
            f"{label}.suggestion.aliases must include the property and legacy names",
        )
        _require(
            ifc_write["writeStrategy"] == "CREATE_OR_UPDATE_PSET",
            f"{label}.ifcWrite.writeStrategy must be CREATE_OR_UPDATE_PSET",
        )
        _require(
            ifc_write["ownerStrategy"] in _OWNER_STRATEGIES,
            f"{label}.ifcWrite.ownerStrategy has an unsupported value",
        )

        for role_id in rule["carrierRoleIds"]:
            _require(
                role_id in roles_by_id,
                f"{label}.carrierRoleIds contains unknown role {role_id!r}",
            )
            role = roles_by_id[role_id]
            _require(
                role["ifcEntity"] == ifc["entity"],
                f"{label}.carrierRoleIds role {role_id!r} has a mismatched IFC entity",
            )
            _require(
                role["ifcOwnerStrategy"] == ifc_write["ownerStrategy"],
                f"{label}.ifcWrite.ownerStrategy must match carrier role {role_id!r}",
            )
            referenced_carrier_ids.add(role_id)

        if official["inExtracted166"]:
            projection = official["legacyProjection"]
            _require(
                official["evidenceStatus"] == "OFFICIAL_EXTRACTED",
                f"{label}.officialPlugin.evidenceStatus must be OFFICIAL_EXTRACTED",
            )
            _require(
                type(official["originalIdentity"]) is str
                and bool(official["originalIdentity"]),
                f"{label}.officialPlugin.originalIdentity must be non-empty",
            )
            _require(
                projection["persistenceMode"]
                in {
                    "DATASTORE_ONLY",
                    "HYBRID_AREA_AND_DATASTORE",
                    "SHARED_PARAMETER_AND_DATASTORE",
                },
                f"migrated metadata {label}.officialPlugin.legacyProjection.persistenceMode has an unsupported value",
            )
            shared_parameter_type = projection["sharedParameterType"]
            _require(
                shared_parameter_type in _SHARED_PARAMETER_TYPES,
                f"migrated metadata {label}.officialPlugin.legacyProjection.sharedParameterType has an unsupported value",
            )
        else:
            _require(
                official["evidenceStatus"] == "MVD_WORKBOOK",
                f"{label}.officialPlugin.evidenceStatus must be MVD_WORKBOOK",
            )
            _require(
                official["originalIdentity"] is None,
                f"{label}.officialPlugin.originalIdentity must be null",
            )

        _validate_runtime_contract(rule, label)

    _require(
        referenced_carrier_ids == set(carrier_ids),
        "every carrierRoles record must be referenced by at least one property",
    )

    task_to_profile = {}
    for index, profile in enumerate(profiles):
        label = f"modelProfiles[{index}]"
        for task_id in profile["taskIds"]:
            _require(task_id in tasks, f"{label}.taskIds references unknown task {task_id!r}")
            _require(
                task_id not in task_to_profile,
                f"task {task_id!r} must belong to exactly one model profile",
            )
            task_to_profile[task_id] = profile["profileId"]
    _require(
        set(task_to_profile) == set(task_ids_list),
        "every task must be referenced by exactly one model profile",
    )

    for index, task in enumerate(task_list):
        label = f"tasks[{index}]"
        _require(
            task["modelFileType"] == task_to_profile[task["taskId"]],
            f"{label}.modelFileType must match its model profile",
        )
        _require(task["source"] == "TaskRuleCatalog.cs", f"{label}.source must be TaskRuleCatalog.cs")
        task_requirement = {
            "level": task["requirement"],
            "conditionId": task["conditionId"],
        }
        _validate_requirement(
            task_requirement,
            condition_ids,
            label,
            _TASK_REQUIREMENTS,
        )
        if task["conditionId"] is not None:
            referenced_condition_ids.add(task["conditionId"])
        for dependency in task["dependencies"]:
            _require(
                dependency in tasks,
                f"{label}.dependencies references unknown task {dependency!r}",
            )
            _require(
                task_to_profile[dependency] == task_to_profile[task["taskId"]],
                f"{label}.dependencies task {dependency!r} belongs to another profile",
            )

    visiting = set()
    visited = set()

    def visit(task_id):
        _require(task_id not in visiting, f"task dependency cycle at {task_id}")
        if task_id in visited:
            return
        visiting.add(task_id)
        for dependency in tasks[task_id]["dependencies"]:
            visit(dependency)
        visiting.remove(task_id)
        visited.add(task_id)

    for task_id in task_ids_list:
        visit(task_id)

    for index, condition in enumerate(conditions):
        label = f"conditions[{index}]"
        _require(
            condition["source"]
            in {"RuleActivationCatalog.cs", "Stage01RegistryProvider.cs"},
            f"{label}.source has an unsupported value",
        )
        _require(
            condition["evidenceStatus"]
            in {
                "LEGACY_ACTIVATION_CATALOG",
                "NOT_IN_LEGACY_ACTIVATION_CATALOG",
            },
            f"{label}.evidenceStatus has an unsupported value",
        )
        if condition["source"] == "RuleActivationCatalog.cs":
            _require(
                condition["evidenceStatus"] == "LEGACY_ACTIVATION_CATALOG"
                and type(condition["activationRuleId"]) is str
                and bool(condition["activationRuleId"]),
                f"{label} legacy activation fields are inconsistent",
            )
        else:
            _require(
                condition["evidenceStatus"]
                == "NOT_IN_LEGACY_ACTIVATION_CATALOG"
                and condition["activationRuleId"] is None,
                f"{label} non-legacy activation fields are inconsistent",
            )
    _require(
        referenced_condition_ids == condition_ids,
        "every condition must be referenced by a property or task",
    )

    alias_property_ids = [alias["propertyId"] for alias in aliases]
    alias_names = [alias["alias"] for alias in aliases]
    _require_unique(alias_property_ids, "legacyAliases.propertyId")
    _require_unique(alias_names, "legacyAliases.alias")
    official_ids = {rule["propertyId"] for rule in official_rules}
    _require(
        set(alias_property_ids) == official_ids,
        "legacyAliases must contain exactly one reference for every official property",
    )
    properties_by_id = {rule["propertyId"]: rule for rule in properties}
    for index, alias in enumerate(aliases):
        _require(
            alias["alias"] in properties_by_id[alias["propertyId"]]["revit"]["legacyNames"],
            f"legacyAliases[{index}].alias must resolve to the property's legacyNames",
        )

    stage_field_keys = [reference["fieldKey"] for reference in stage_refs]
    stage_property_ids = [reference["propertyId"] for reference in stage_refs]
    stage_rows = [reference["sourceRow"] for reference in stage_refs]
    _require_unique(stage_field_keys, "stage01.fieldRefs.fieldKey")
    _require_unique(stage_property_ids, "stage01.fieldRefs.propertyId")
    _require_unique(stage_rows, "stage01.fieldRefs.sourceRow")
    for index, reference in enumerate(stage_refs):
        property_id = reference["propertyId"]
        _require(
            property_id in properties_by_id,
            f"migrated metadata stage01.fieldRefs[{index}] has an unknown property reference",
        )
        _require(
            properties_by_id[property_id]["source"]["row"] == reference["sourceRow"],
            f"stage01.fieldRefs[{index}].sourceRow must match the referenced property",
        )
        rule = properties_by_id[property_id]
        expected_field_key = "|".join(
            (rule["ifc"]["entity"], rule["ifc"]["propertySet"], rule["ifc"]["property"])
        )
        _require(
            reference["fieldKey"] == expected_field_key,
            f"stage01.fieldRefs[{index}].fieldKey must match the referenced IFC identity",
        )
    _require(
        sum(property_id in official_ids for property_id in stage_property_ids) == 89,
        "stage01.fieldRefs must contain exactly 89 official property references",
    )
    _require(
        sum(not reference["writeInStage01"] for reference in stage_refs) == 1,
        "migrated metadata stage01.fieldRefs must preserve exactly one non-writable field",
    )

    entity_policy_ids = [policy["ifcEntity"] for policy in entity_policies]
    _require_unique(
        entity_policy_ids,
        "migrated metadata entityPolicies.ifcEntity",
    )
    official_entity_ids = {rule["ifc"]["entity"] for rule in official_rules}
    _require(
        set(entity_policy_ids) == official_entity_ids,
        "migrated metadata entityPolicies must reference exactly the official IFC entities",
    )
    _require(
        sum(policy["revitCarrier"] == "" for policy in entity_policies) == 5,
        "migrated metadata entityPolicies must preserve exactly 5 empty revitCarrier values",
    )
    _require(
        all(not policy["officialExportVerified"] for policy in entity_policies),
        "migrated metadata entityPolicies must preserve all 9 unverified export states",
    )

    exception_field_keys = [exception["fieldKey"] for exception in plugin_exceptions]
    _require_unique(
        exception_field_keys,
        "migrated metadata exceptions.fieldKey",
    )
    stage_field_key_set = set(stage_field_keys)
    for index, field_key in enumerate(exception_field_keys):
        _require(
            field_key in stage_field_key_set,
            f"migrated metadata exceptions[{index}] contains an unknown field reference",
        )
    nonofficial_stage_field_keys = {
        reference["fieldKey"]
        for reference in stage_refs
        if reference["propertyId"] not in official_ids
    }
    _require(
        set(exception_field_keys) == nonofficial_stage_field_keys,
        "migrated metadata exceptions must cover exactly the 13 non-official stage01 fields",
    )

    workbook_evidence = [item for item in evidence_sources if "sha256" in item]
    count_evidence = [item for item in evidence_sources if "count" in item]
    _require(
        len(workbook_evidence) == 1,
        "evidenceSources must contain exactly one workbook evidence record",
    )
    workbook = workbook_evidence[0]
    _require(
        re.fullmatch(r"[0-9a-f]{64}", workbook["sha256"]) is not None,
        "workbook evidence sha256 must contain 64 lowercase hex characters",
    )
    _require(
        bool(workbook.get("sheet")) and bool(workbook.get("range")),
        "workbook evidence must include non-empty sheet and range",
    )
    _require(
        len(count_evidence) == 2
        and {item["count"] for item in count_evidence} == {102, 166},
        "evidenceSources count records must describe the 102 and 166 source sets",
    )


def validate_compatibility(source, baseline):
    _expect_object(
        baseline,
        "compatibility baseline",
        required=_COMPATIBILITY_BASELINE_FIELDS,
    )
    for key in ("schemaVersion", "baselineId", "baselineVersion"):
        _expect_string(baseline[key], f"compatibility baseline.{key}", nonempty=True)
    _require(
        baseline["schemaVersion"] == _COMPATIBILITY_BASELINE_VERSION,
        "compatibility baseline.schemaVersion must be 1.1.0",
    )
    _require(
        baseline["baselineId"] == _COMPATIBILITY_BASELINE_ID,
        f"compatibility baseline.baselineId must be {_COMPATIBILITY_BASELINE_ID}",
    )
    _require(
        baseline["baselineVersion"] == _COMPATIBILITY_BASELINE_VERSION,
        "compatibility baseline.baselineVersion must be 1.1.0",
    )

    legacy_metadata_digests = baseline["legacyMetadataDigests"]
    _expect_object(
        legacy_metadata_digests,
        "compatibility baseline.legacyMetadataDigests",
        required=_LEGACY_METADATA_DIGEST_NAMES,
    )
    for name in _LEGACY_METADATA_DIGEST_NAMES:
        path = f"compatibility baseline.legacyMetadataDigests.{name}"
        _expect_string(legacy_metadata_digests[name], path, nonempty=True)
        _require(
            re.fullmatch(r"[0-9a-f]{64}", legacy_metadata_digests[name])
            is not None,
            f"{path} must contain 64 lowercase hex characters",
        )

    workbook = baseline["workbookEvidence"]
    _expect_object(
        workbook,
        "compatibility baseline.workbookEvidence",
        required={"logicalSource", "sha256"},
    )
    for key in ("logicalSource", "sha256"):
        _expect_string(
            workbook[key],
            f"compatibility baseline.workbookEvidence.{key}",
            nonempty=True,
        )
    _require(
        workbook["logicalSource"] == _COMPATIBILITY_WORKBOOK_SOURCE,
        "compatibility baseline.workbookEvidence.logicalSource must match the published workbook",
    )
    _require(
        workbook["sha256"] == _COMPATIBILITY_WORKBOOK_SHA256,
        "compatibility baseline.workbookEvidence.sha256 must match the published workbook",
    )

    official_properties = baseline["officialProperties"]
    _expect_array(official_properties, "compatibility baseline.officialProperties")
    _require(
        len(official_properties) == 166,
        "compatibility baseline.officialProperties must contain exactly 166 records",
    )
    for index, item in enumerate(official_properties):
        path = f"compatibility baseline.officialProperties[{index}]"
        _expect_object(item, path, required=_COMPATIBILITY_PROPERTY_FIELDS)
        for key in _COMPATIBILITY_PROPERTY_FIELDS:
            _expect_string(item[key], f"{path}.{key}", nonempty=True)
        _parse_uuid5(item["propertyId"], f"{path}.propertyId")
        _parse_uuid5(item["parameterGuid"], f"{path}.parameterGuid")

    for key in ("propertyId", "canonicalKey", "parameterGuid", "originalIdentity"):
        _require_unique(
            [item[key] for item in official_properties],
            f"compatibility baseline.officialProperties.{key}",
        )

    source_workbooks = [
        item for item in source["evidenceSources"] if "sha256" in item
    ]
    _require(
        len(source_workbooks) == 1,
        "source must expose one workbook record for compatibility baseline validation",
    )
    source_workbook = source_workbooks[0]
    _require(
        source_workbook["source"] == workbook["logicalSource"],
        "source workbook logical source does not match compatibility baseline",
    )
    _require(
        source_workbook["sha256"] == workbook["sha256"],
        "source workbook sha256 does not match compatibility baseline",
    )

    source_official = [
        item
        for item in source["properties"]
        if item["officialPlugin"]["inExtracted166"]
    ]
    baseline_by_id = {item["propertyId"]: item for item in official_properties}
    source_by_id = {item["propertyId"]: item for item in source_official}
    _require(
        set(source_by_id) == set(baseline_by_id),
        "source official propertyId set does not match compatibility baseline",
    )
    for property_id, rule in source_by_id.items():
        expected = baseline_by_id[property_id]
        actual = {
            "propertyId": rule["propertyId"],
            "canonicalKey": rule["canonicalKey"],
            "parameterGuid": rule["revit"]["parameterGuid"],
            "originalIdentity": rule["officialPlugin"]["originalIdentity"],
        }
        for key in _COMPATIBILITY_PROPERTY_FIELDS:
            _require(
                actual[key] == expected[key],
                f"source official property {property_id} {key} does not match compatibility baseline",
            )

    actual_legacy_metadata_digests = _legacy_metadata_digests(source)
    for name in _LEGACY_METADATA_DIGEST_NAMES:
        _require(
            actual_legacy_metadata_digests[name]
            == legacy_metadata_digests[name],
            f"migrated metadata {name} does not preserve legacy equivalence recorded by compatibility baseline",
        )


def _paths_refer_to_same_file(first_path, second_path):
    if first_path.resolve(strict=False) == second_path.resolve(strict=False):
        return True
    if not first_path.exists() or not second_path.exists():
        return False
    try:
        return os.path.samefile(first_path, second_path)
    except OSError:
        return False


def compile_rulepack(source_path, output_path, baseline_path):
    source_path = Path(source_path)
    output_path = Path(output_path)
    baseline_path = Path(baseline_path)
    _require(
        not _paths_refer_to_same_file(source_path, output_path),
        "source and output must refer to different files",
    )
    _require(
        not _paths_refer_to_same_file(baseline_path, output_path),
        "baseline and output must refer to different files",
    )
    with source_path.open(encoding="utf-8") as stream:
        source = json.load(stream)
    baseline = _load_json_without_duplicate_keys(
        baseline_path,
        "compatibility baseline",
    )
    validate_semantics(source)
    validate_compatibility(source, baseline)
    payload = canonical_bytes(source)
    header = (
        MAGIC
        + struct.pack(">I", FORMAT_VERSION)
        + struct.pack(">Q", len(payload))
        + hashlib.sha256(payload).digest()
    )
    output_path.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary_name = tempfile.mkstemp(
        dir=str(output_path.parent),
        prefix=f".{output_path.name}.",
        suffix=".tmp",
    )
    try:
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(header)
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


def _argument_parser():
    parser = argparse.ArgumentParser(
        description="Compile the canonical HBR JSON rule source into a deterministic pack."
    )
    parser.add_argument("--source", required=True, help="UTF-8 HBR rule source JSON")
    parser.add_argument(
        "--baseline",
        required=True,
        help="Versioned HBR published-compatibility baseline JSON",
    )
    parser.add_argument("--output", required=True, help="Destination .hbrpack path")
    return parser


def main(argv=None):
    arguments = _argument_parser().parse_args(argv)
    try:
        compile_rulepack(arguments.source, arguments.output, arguments.baseline)
    except (OSError, UnicodeError, ValueError) as error:
        print(f"HBR rule-pack compilation failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
