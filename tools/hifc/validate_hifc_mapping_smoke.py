#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import re
import sys
import tempfile
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, Iterable, List, Mapping, Sequence, Tuple

ROOT = Path(__file__).resolve().parents[2]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from tools.build_hbr_rulepack import (  # noqa: E402
    canonical_source_sha256,
    effective_ifc_identity,
    load_validated_rule_source,
)

ENTITY_RE = re.compile(r"^#(?P<id>\d+)=(?P<type>[A-Z0-9_]+)\((?P<args>.*)\);$")
TYPED_RE = re.compile(r"^(?P<type>IFC[A-Z0-9_]+)\((?P<value>.*)\)$")
GUID_RE = re.compile(r"^[0-3][0-9A-Za-z_$]{21}$")
REF_RE = re.compile(r"#(\d+)")
DATE_RE = re.compile(r"^\d{4}-\d{2}-\d{2}$")
DATETIME_RE = re.compile(r"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$")

EXPECTED_ARGUMENT_COUNTS: Mapping[str, int] = {
    "IFCORGANIZATION": 5,
    "IFCAPPLICATION": 4,
    "IFCPERSON": 8,
    "IFCPERSONANDORGANIZATION": 3,
    "IFCOWNERHISTORY": 8,
    "IFCSIUNIT": 4,
    "IFCUNITASSIGNMENT": 1,
    "IFCCARTESIANPOINT": 1,
    "IFCAXIS2PLACEMENT3D": 3,
    "IFCAXIS2PLACEMENT2D": 2,
    "IFCDIRECTION": 1,
    "IFCGEOMETRICREPRESENTATIONCONTEXT": 6,
    "IFCGEOMETRICREPRESENTATIONSUBCONTEXT": 10,
    "IFCLOCALPLACEMENT": 2,
    "IFCPROJECT": 9,
    "IFCSITE": 14,
    "IFCBUILDING": 12,
    "IFCBUILDINGSTOREY": 10,
    "IFCRECTANGLEPROFILEDEF": 5,
    "IFCEXTRUDEDAREASOLID": 4,
    "IFCSHAPEREPRESENTATION": 4,
    "IFCPRODUCTDEFINITIONSHAPE": 3,
    "IFCSPACE": 11,
    "IFCSPATIALZONE": 9,
    "IFCWALL": 9,
    "IFCSLAB": 9,
    "IFCROOF": 9,
    "IFCWINDOW": 13,
    "IFCSTAIRFLIGHT": 13,
    "IFCDOOR": 13,
    "IFCDUCTSEGMENT": 9,
    "IFCACTOR": 6,
    "IFCRELAGGREGATES": 6,
    "IFCRELCONTAINEDINSPATIALSTRUCTURE": 6,
    "IFCRELREFERENCEDINSPATIALSTRUCTURE": 6,
    "IFCPROPERTYSINGLEVALUE": 4,
    "IFCPROPERTYSET": 5,
    "IFCRELDEFINESBYPROPERTIES": 6,
}

EXPECTED_OWNER_TYPES = {
    "IFCACTOR",
    "IFCBUILDING",
    "IFCBUILDINGSTOREY",
    "IFCDOOR",
    "IFCDUCTSEGMENT",
    "IFCPROJECT",
    "IFCROOF",
    "IFCSITE",
    "IFCSLAB",
    "IFCSPACE",
    "IFCSPATIALZONE",
    "IFCSTAIRFLIGHT",
    "IFCWALL",
    "IFCWINDOW",
}

GLOBAL_ID_ENTITY_TYPES = {
    "IFCPROJECT",
    "IFCSITE",
    "IFCBUILDING",
    "IFCBUILDINGSTOREY",
    "IFCSPACE",
    "IFCSPATIALZONE",
    "IFCWALL",
    "IFCSLAB",
    "IFCROOF",
    "IFCWINDOW",
    "IFCSTAIRFLIGHT",
    "IFCDOOR",
    "IFCDUCTSEGMENT",
    "IFCACTOR",
    "IFCPROPERTYSET",
    "IFCRELDEFINESBYPROPERTIES",
    "IFCRELAGGREGATES",
    "IFCRELCONTAINEDINSPATIALSTRUCTURE",
    "IFCRELREFERENCEDINSPATIALSTRUCTURE",
}


@dataclass(frozen=True)
class Entity:
    entity_id: int
    entity_type: str
    arguments: Tuple[str, ...]


@dataclass(frozen=True)
class RuleIdentity:
    attachment_owner: str
    property_set: str
    property_name: str
    declared_type: str


def split_top_level(text: str) -> List[str]:
    values: List[str] = []
    start = 0
    depth = 0
    in_string = False
    index = 0
    while index < len(text):
        character = text[index]
        if in_string:
            if character == "'":
                if index + 1 < len(text) and text[index + 1] == "'":
                    index += 2
                    continue
                in_string = False
            index += 1
            continue
        if character == "'":
            in_string = True
        elif character == "(":
            depth += 1
        elif character == ")":
            depth -= 1
            if depth < 0:
                raise AssertionError(f"STEP括号不平衡：{text}")
        elif character == "," and depth == 0:
            values.append(text[start:index].strip())
            start = index + 1
        index += 1
    if in_string or depth != 0:
        raise AssertionError(f"STEP参数未闭合：{text}")
    values.append(text[start:].strip())
    return values


def parse_entities(ifc_text: str) -> Dict[int, Entity]:
    entities: Dict[int, Entity] = {}
    for line_number, raw_line in enumerate(ifc_text.splitlines(), start=1):
        line = raw_line.strip()
        if not line.startswith("#"):
            continue
        match = ENTITY_RE.match(line)
        if match is None:
            raise AssertionError(
                f"STEP实体必须单行且语法完整，行{line_number}：{line[:160]}"
            )
        entity_id = int(match.group("id"))
        if entity_id in entities:
            raise AssertionError(f"重复STEP编号：#{entity_id}")
        entities[entity_id] = Entity(
            entity_id=entity_id,
            entity_type=match.group("type"),
            arguments=tuple(split_top_level(match.group("args"))),
        )
    if not entities:
        raise AssertionError("IFC DATA段没有解析到任何STEP实体。")
    return entities


def decode_ifc_string(token: str) -> str:
    if not (token.startswith("'") and token.endswith("'")):
        raise AssertionError(f"预期IFC字符串，实际：{token}")
    body = token[1:-1].replace("''", "'")
    output: List[str] = []
    index = 0
    while index < len(body):
        if body.startswith("\\X2\\", index):
            end = body.find("\\X0\\", index + 4)
            if end < 0:
                raise AssertionError(f"IFC X2转义未闭合：{token}")
            hex_text = body[index + 4 : end]
            if len(hex_text) % 4:
                raise AssertionError(f"IFC X2 UTF-16BE长度错误：{token}")
            output.append(bytes.fromhex(hex_text).decode("utf-16-be"))
            index = end + 4
            continue
        output.append(body[index])
        index += 1
    return "".join(output)


def parse_ref(token: str) -> int:
    if not token.startswith("#"):
        raise AssertionError(f"预期STEP引用，实际：{token}")
    return int(token[1:])


def parse_ref_list(token: str) -> List[int]:
    if not (token.startswith("(") and token.endswith(")")):
        raise AssertionError(f"预期STEP引用列表，实际：{token}")
    inner = token[1:-1].strip()
    if not inner:
        return []
    return [parse_ref(item.strip()) for item in split_top_level(inner)]


def normalize_rules(payload: Mapping[str, object]) -> List[RuleIdentity]:
    identities: List[RuleIdentity] = []
    if "properties" in payload:
        properties = payload["properties"]
        if not isinstance(properties, list):
            raise AssertionError("规则源properties必须是数组。")
        for rule in properties:
            ifc = rule["ifc"]
            semantic_owner = str(ifc["entity"])
            attachment_owner = "IfcActor" if semantic_owner == "IfcOrganization" else semantic_owner
            identities.append(
                RuleIdentity(
                    attachment_owner=attachment_owner.upper(),
                    property_set=str(ifc["propertySet"]),
                    property_name=str(ifc["property"]),
                    declared_type=str(ifc["declaredType"]).upper(),
                )
            )
    elif "rules" in payload:
        rules = payload["rules"]
        if not isinstance(rules, list):
            raise AssertionError("基线rules必须是数组。")
        for rule in rules:
            identities.append(
                RuleIdentity(
                    attachment_owner=str(rule["attachmentOwnerEntity"]).upper(),
                    property_set=str(rule["propertySet"]),
                    property_name=str(rule["property"]),
                    declared_type=str(rule["declaredIfcType"]).upper(),
                )
            )
    else:
        raise AssertionError("映射文件既没有properties，也没有rules。")
    return identities


def typed_value_parts(token: str) -> Tuple[str, str]:
    match = TYPED_RE.match(token)
    if match is None:
        raise AssertionError(f"NominalValue不是typed IFC token：{token}")
    return match.group("type"), match.group("value")


def count_entities(entities: Mapping[int, Entity]) -> Dict[str, int]:
    counts: Dict[str, int] = {}
    for entity in entities.values():
        counts[entity.entity_type] = counts.get(entity.entity_type, 0) + 1
    return counts


def build_actual_mapping(
    entities: Mapping[int, Entity],
) -> Tuple[Dict[Tuple[str, str, str], Dict[str, object]], List[Entity], List[Entity]]:
    property_sets = [entity for entity in entities.values() if entity.entity_type == "IFCPROPERTYSET"]
    relationships = [
        entity for entity in entities.values() if entity.entity_type == "IFCRELDEFINESBYPROPERTIES"
    ]
    relationship_by_pset: Dict[int, Entity] = {}
    for relationship in relationships:
        pset_id = parse_ref(relationship.arguments[5])
        if pset_id in relationship_by_pset:
            raise AssertionError(f"Pset #{pset_id}被多个IfcRelDefinesByProperties重复挂接。")
        relationship_by_pset[pset_id] = relationship

    actual: Dict[Tuple[str, str, str], Dict[str, object]] = {}
    for property_set in property_sets:
        pset_name = decode_ifc_string(property_set.arguments[2])
        relationship = relationship_by_pset.get(property_set.entity_id)
        if relationship is None:
            raise AssertionError(f"{pset_name}没有IfcRelDefinesByProperties挂接关系。")
        owners = parse_ref_list(relationship.arguments[4])
        if len(owners) != 1:
            raise AssertionError(f"验证样例要求{pset_name}恰好挂到一个Owner，实际{len(owners)}。")
        owner = entities[owners[0]]
        property_ids = parse_ref_list(property_set.arguments[4])
        if not property_ids:
            raise AssertionError(f"{pset_name}为空属性集。")
        seen_names = set()
        for property_id in property_ids:
            property_entity = entities[property_id]
            if property_entity.entity_type != "IFCPROPERTYSINGLEVALUE":
                raise AssertionError(f"{pset_name}包含非IfcPropertySingleValue实体。")
            property_name = decode_ifc_string(property_entity.arguments[0])
            if property_name in seen_names:
                raise AssertionError(f"{pset_name}内重复属性：{property_name}")
            seen_names.add(property_name)
            key = (owner.entity_type, pset_name, property_name)
            if key in actual:
                raise AssertionError(f"重复Canonical挂接路径：{key}")
            actual_type, payload = typed_value_parts(property_entity.arguments[2])
            actual[key] = {
                "type": actual_type,
                "payload": payload,
                "typed_token": property_entity.arguments[2],
                "owner_id": owner.entity_id,
                "property_id": property_entity.entity_id,
                "property_set_id": property_set.entity_id,
                "relationship_id": relationship.entity_id,
            }
    return actual, property_sets, relationships


def expect_aggregate(
    entities: Mapping[int, Entity], parent_type: str, child_type: str
) -> None:
    for relationship in entities.values():
        if relationship.entity_type != "IFCRELAGGREGATES":
            continue
        parent = entities[parse_ref(relationship.arguments[4])]
        children = [entities[entity_id] for entity_id in parse_ref_list(relationship.arguments[5])]
        if parent.entity_type == parent_type and any(
            child.entity_type == child_type for child in children
        ):
            return
    raise AssertionError(f"缺少空间分解关系：{parent_type} → {child_type}")


def _repository_root(source_path: Path) -> Path:
    for candidate in (source_path.resolve().parent, *source_path.resolve().parents):
        if (candidate / ".git").exists():
            return candidate
    raise AssertionError(f"无法从规则源定位仓库：{source_path}")


def _logical_path(root: Path, path: Path) -> str:
    try:
        return path.resolve().relative_to(root.resolve()).as_posix()
    except ValueError:
        return path.name


def _sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def validate(
    source_path: Path,
    baseline_path: Path,
    ifc_path: Path,
    manifest_path: Path,
) -> Dict[str, object]:
    source_path = Path(source_path)
    baseline_path = Path(baseline_path)
    ifc_path = Path(ifc_path)
    manifest_path = Path(manifest_path)
    if not source_path.is_file():
        raise AssertionError(f"规则源不存在：{source_path}")
    if not ifc_path.is_file():
        raise AssertionError(f"IFC文件不存在：{ifc_path}")
    if not manifest_path.is_file():
        raise AssertionError(f"manifest不存在：{manifest_path}")

    source = load_validated_rule_source(source_path, baseline_path)
    expected_rules = []
    for rule in source["properties"]:
        owner, property_set, property_name = effective_ifc_identity(rule)
        expected_rules.append(
            RuleIdentity(
                attachment_owner=("IfcActor" if owner == "IfcOrganization" else owner).upper(),
                property_set=property_set,
                property_name=property_name,
                declared_type=str(rule["ifc"]["declaredType"]).upper(),
            )
        )
    if len(expected_rules) != 359:
        raise AssertionError(f"唯一规则源应为359条，实际{len(expected_rules)}条。")

    ifc_text = ifc_path.read_text(encoding="utf-8")
    if "FILE_SCHEMA(('IFC4'));" not in ifc_text:
        raise AssertionError("验证样例必须声明IFC4。")
    if "ViewDefinition [ReferenceView_V1.2]" not in ifc_text:
        raise AssertionError("验证样例必须声明ReferenceView_V1.2。")
    if not ifc_text.startswith("ISO-10303-21;\nHEADER;"):
        raise AssertionError("IFC STEP文件头不正确。")
    if not ifc_text.rstrip().endswith("END-ISO-10303-21;"):
        raise AssertionError("IFC STEP文件尾不正确。")

    entities = parse_entities(ifc_text)
    counts = count_entities(entities)

    unknown_types = sorted(
        {entity.entity_type for entity in entities.values()} - set(EXPECTED_ARGUMENT_COUNTS)
    )
    if unknown_types:
        raise AssertionError(f"验证器缺少实体参数合同：{unknown_types}")
    for entity in entities.values():
        expected_count = EXPECTED_ARGUMENT_COUNTS[entity.entity_type]
        if len(entity.arguments) != expected_count:
            raise AssertionError(
                f"#{entity.entity_id} {entity.entity_type}参数数量错误："
                f"{len(entity.arguments)} != {expected_count}"
            )
        for referenced_id in (int(value) for value in REF_RE.findall(",".join(entity.arguments))):
            if referenced_id not in entities:
                raise AssertionError(
                    f"#{entity.entity_id} {entity.entity_type}引用不存在的#{referenced_id}。"
                )

    actual, property_sets, attachments = build_actual_mapping(entities)
    expected: Dict[Tuple[str, str, str], str] = {}
    for rule in expected_rules:
        key = (rule.attachment_owner, rule.property_set, rule.property_name)
        if key in expected:
            raise AssertionError(f"唯一规则源存在重复路径：{key}")
        expected[key] = rule.declared_type

    if len(expected) != 359:
        raise AssertionError(f"唯一映射路径数量错误：{len(expected)}")
    if len(actual) != 359:
        raise AssertionError(f"IFC实际属性数量错误：{len(actual)}")
    if len(property_sets) != 52:
        raise AssertionError(f"IFC实际Pset数量错误：{len(property_sets)}")
    if len(attachments) != 52:
        raise AssertionError(f"IFC实际Pset挂接关系数量错误：{len(attachments)}")

    missing = sorted(set(expected) - set(actual))
    unexpected = sorted(set(actual) - set(expected))
    if missing or unexpected:
        raise AssertionError(
            "映射路径不一致；"
            f"缺失示例={missing[:5]}；多余示例={unexpected[:5]}"
        )

    for key, declared_type in expected.items():
        actual_value = actual[key]
        if actual_value["type"] != declared_type:
            raise AssertionError(
                f"{key}类型错误：{actual_value['type']} != {declared_type}"
            )
        payload = str(actual_value["payload"])
        if declared_type in {"IFCLABEL", "IFCTEXT"}:
            decoded = decode_ifc_string(payload)
            if not decoded.strip():
                raise AssertionError(f"{key}字符串为空。")
            if declared_type == "IFCLABEL" and len(decoded) > 255:
                raise AssertionError(f"{key}超过IfcLabel 255字符限制。")
        elif declared_type == "IFCDATE":
            decoded = decode_ifc_string(payload)
            if not DATE_RE.match(decoded):
                raise AssertionError(f"{key}不是yyyy-MM-dd：{decoded}")
        elif declared_type == "IFCDATETIME":
            decoded = decode_ifc_string(payload)
            if not DATETIME_RE.match(decoded):
                raise AssertionError(f"{key}不是带时区的IfcDateTime：{decoded}")
        elif declared_type == "IFCBOOLEAN":
            if payload != ".T.":
                raise AssertionError(
                    f"本结构试件中的Boolean必须统一为.T.，实际{key}={payload}"
                )
        elif declared_type == "IFCINTEGER":
            int(payload)
        elif declared_type == "IFCREAL":
            numeric = float(payload)
            if not math.isfinite(numeric):
                raise AssertionError(f"{key}不是有限实数。")
        else:
            raise AssertionError(f"不支持的声明类型：{declared_type}")

    actual_owner_types = {key[0] for key in actual}
    if actual_owner_types != EXPECTED_OWNER_TYPES:
        raise AssertionError(
            f"属性Owner类型不完整：{sorted(actual_owner_types)}"
        )

    # Standard spatial decomposition, not name-based inference.
    expect_aggregate(entities, "IFCPROJECT", "IFCSITE")
    expect_aggregate(entities, "IFCSITE", "IFCBUILDING")
    expect_aggregate(entities, "IFCBUILDING", "IFCBUILDINGSTOREY")
    expect_aggregate(entities, "IFCBUILDINGSTOREY", "IFCSPACE")

    containment = [
        entity
        for entity in entities.values()
        if entity.entity_type == "IFCRELCONTAINEDINSPATIALSTRUCTURE"
    ]
    if len(containment) != 1:
        raise AssertionError("验证样例必须恰好存在一个构件楼层归属关系。")
    related_types = {
        entities[entity_id].entity_type
        for entity_id in parse_ref_list(containment[0].arguments[4])
    }
    required_products = {
        "IFCWALL",
        "IFCSLAB",
        "IFCROOF",
        "IFCWINDOW",
        "IFCSTAIRFLIGHT",
        "IFCDOOR",
        "IFCDUCTSEGMENT",
    }
    if not required_products.issubset(related_types):
        raise AssertionError(
            f"楼层构件归属不完整：{sorted(required_products - related_types)}"
        )
    if entities[parse_ref(containment[0].arguments[5])].entity_type != "IFCBUILDINGSTOREY":
        raise AssertionError("构件必须归属IfcBuildingStorey。")

    zones = [entity for entity in entities.values() if entity.entity_type == "IFCSPATIALZONE"]
    if len(zones) != 1:
        raise AssertionError("验证样例必须恰好创建一个真实IfcSpatialZone。")
    zone_relations = [
        entity
        for entity in entities.values()
        if entity.entity_type == "IFCRELREFERENCEDINSPATIALSTRUCTURE"
    ]
    if len(zone_relations) != 1:
        raise AssertionError("IfcSpatialZone必须通过引用关系关联楼层。")
    if zones[0].entity_id not in parse_ref_list(zone_relations[0].arguments[4]):
        raise AssertionError("IfcSpatialZone没有进入楼层引用关系。")
    if entities[parse_ref(zone_relations[0].arguments[5])].entity_type != "IFCBUILDINGSTOREY":
        raise AssertionError("IfcSpatialZone应引用到IfcBuildingStorey。")

    actors = [entity for entity in entities.values() if entity.entity_type == "IFCACTOR"]
    if len(actors) != 1:
        raise AssertionError("组织属性必须恰好使用一个IfcActor包装。")
    organization = entities[parse_ref(actors[0].arguments[5])]
    if organization.entity_type != "IFCORGANIZATION":
        raise AssertionError("IfcActor.TheActor必须指向IfcOrganization。")
    if not any(
        key[0] == "IFCACTOR" and key[1] == "Pset_组织通用属性集"
        for key in actual
    ):
        raise AssertionError("Pset_组织通用属性集没有挂到IfcActor。")
    for relationship in attachments:
        owner_ids = parse_ref_list(relationship.arguments[4])
        if organization.entity_id in owner_ids:
            raise AssertionError("IfcOrganization不得被非法直接挂接Pset。")

    spaced_x = ("IFCPROJECT", "Pset_申报信息属性集", "基点坐标 X")
    spaced_y = ("IFCPROJECT", "Pset_申报信息属性集", "基点坐标 Y")
    if spaced_x in actual or spaced_y in actual:
        raise AssertionError("最终 IFC 含带空格坐标 identity。")
    x_key = ("IFCPROJECT", "Pset_申报信息属性集", "基点坐标X")
    y_key = ("IFCPROJECT", "Pset_申报信息属性集", "基点坐标Y")
    if actual[x_key]["typed_token"] != "IFCREAL(3353559.52)":
        raise AssertionError(f"X/南北坐标值错误：{actual[x_key]['typed_token']}")
    if actual[y_key]["typed_token"] != "IFCREAL(38345264.397)":
        raise AssertionError(f"Y/东西坐标值错误：{actual[y_key]['typed_token']}")
    if counts.get("IFCEXTRUDEDAREASOLID", 0) < 9:
        raise AssertionError("至少9类可视对象应具有简单拉伸体几何。")

    global_ids: List[str] = []
    for entity in entities.values():
        if entity.entity_type not in GLOBAL_ID_ENTITY_TYPES:
            continue
        global_id = decode_ifc_string(entity.arguments[0])
        if GUID_RE.match(global_id) is None:
            raise AssertionError(
                f"#{entity.entity_id} {entity.entity_type} GlobalId不规范：{global_id}"
            )
        global_ids.append(global_id)
    if len(global_ids) != len(set(global_ids)):
        raise AssertionError("IFC GlobalId存在重复。")

    summary = {
        "stepEntities": len(entities),
        "properties": len(actual),
        "propertySets": len(property_sets),
        "attachments": len(attachments),
        "ownerTypes": [
            name.replace("IFC", "Ifc", 1)
            for name in sorted(actual_owner_types)
        ],
        "extrudedSolids": counts.get("IFCEXTRUDEDAREASOLID", 0),
    }
    display_names = {
        "IFCACTOR": "IfcActor",
        "IFCBUILDING": "IfcBuilding",
        "IFCBUILDINGSTOREY": "IfcBuildingStorey",
        "IFCDOOR": "IfcDoor",
        "IFCDUCTSEGMENT": "IfcDuctSegment",
        "IFCPROJECT": "IfcProject",
        "IFCROOF": "IfcRoof",
        "IFCSITE": "IfcSite",
        "IFCSLAB": "IfcSlab",
        "IFCSPACE": "IfcSpace",
        "IFCSPATIALZONE": "IfcSpatialZone",
        "IFCSTAIRFLIGHT": "IfcStairFlight",
        "IFCWALL": "IfcWall",
        "IFCWINDOW": "IfcWindow",
    }
    summary["ownerTypes"] = sorted(display_names.values())

    root = _repository_root(source_path)
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    expected_manifest_keys = {
        "schemaVersion", "fixtureId", "generator", "source", "fixture", "summary", "policies"
    }
    if set(manifest) != expected_manifest_keys:
        raise AssertionError("manifest顶层字段不符合固定合同。")
    expected_nested_keys = {
        "generator": {"path", "version", "sha256"},
        "source": {
            "path", "sha256", "canonicalSha256", "compatibilityBaselinePath",
            "compatibilityBaselineSha256", "packageId", "packageVersion",
        },
        "fixture": {
            "path", "sha256", "bytes", "encoding", "lineEnding", "schema", "viewDefinition",
        },
        "policies": {"valueProfile", "booleanSample"},
    }
    for section, keys in expected_nested_keys.items():
        if not isinstance(manifest.get(section), dict) or set(manifest[section]) != keys:
            raise AssertionError(f"manifest {section}字段不符合固定合同。")
    if manifest.get("schemaVersion") != "1.0.0":
        raise AssertionError("manifest schemaVersion错误。")
    if manifest.get("fixtureId") != "HBR-HIFC-FULL-MAPPING-V1":
        raise AssertionError("manifest fixtureId错误。")
    generator = manifest["generator"]
    generator_path = root / generator["path"]
    if generator["path"] != "tools/hifc/generate_hifc_mapping_smoke.py":
        raise AssertionError("manifest generator path错误。")
    if generator["version"] != "1.0.0" or generator["sha256"] != _sha256(generator_path):
        raise AssertionError("manifest generator版本或SHA256不一致。")
    if manifest.get("fixture", {}).get("sha256") != _sha256(ifc_path):
        raise AssertionError("manifest IFC SHA256与实际文件不一致。")
    if manifest.get("fixture", {}).get("bytes") != len(ifc_path.read_bytes()):
        raise AssertionError("manifest IFC字节数与实际文件不一致。")
    if manifest.get("source", {}).get("sha256") != _sha256(source_path):
        raise AssertionError("manifest规则源SHA256不一致。")
    if manifest.get("source", {}).get("canonicalSha256") != canonical_source_sha256(source):
        raise AssertionError("manifest canonicalSha256不一致。")
    if manifest.get("source", {}).get("compatibilityBaselineSha256") != _sha256(baseline_path):
        raise AssertionError("manifest兼容基线SHA256不一致。")
    source_manifest = manifest["source"]
    if source_manifest["path"] != _logical_path(root, source_path):
        raise AssertionError("manifest规则源路径不一致。")
    if source_manifest["compatibilityBaselinePath"] != _logical_path(root, baseline_path):
        raise AssertionError("manifest兼容基线路径不一致。")
    if source_manifest["packageId"] != source["packageId"] or source_manifest["packageVersion"] != source["packageVersion"]:
        raise AssertionError("manifest规则包身份不一致。")
    fixture_manifest = manifest["fixture"]
    if fixture_manifest["encoding"] != "UTF-8" or fixture_manifest["lineEnding"] != "LF":
        raise AssertionError("manifest IFC编码或换行合同错误。")
    if fixture_manifest["schema"] != "IFC4" or fixture_manifest["viewDefinition"] != "ReferenceView_V1.2":
        raise AssertionError("manifest IFC schema/viewDefinition错误。")
    if manifest["policies"] != {
        "valueProfile": "STRUCTURAL_SMOKE_V1",
        "booleanSample": "ALWAYS_TRUE_FOR_IFCFLUX_SMOKE",
    }:
        raise AssertionError("manifest policies错误。")
    if manifest.get("summary") != summary:
        raise AssertionError("manifest summary与IFC实际结构不一致。")

    return {
        "status": "PASS",
        "inputs": {
            "source": {"path": _logical_path(root, source_path), "sha256": _sha256(source_path)},
            "baseline": {"path": _logical_path(root, baseline_path), "sha256": _sha256(baseline_path)},
            "manifest": {"path": _logical_path(root, manifest_path), "sha256": _sha256(manifest_path)},
        },
        "ifc": {"path": _logical_path(root, ifc_path), "sha256": _sha256(ifc_path)},
        "summary": summary,
    }


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Validate the HBR-HIFC full-mapping IFC4 smoke fixture."
    )
    source_group = parser.add_mutually_exclusive_group(required=True)
    source_group.add_argument("--source", type=Path)
    source_group.add_argument("--mapping", type=Path, help="--source的兼容别名")
    parser.add_argument("--baseline", type=Path, required=True)
    parser.add_argument("--ifc", type=Path, required=True)
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--report", type=Path)
    arguments = parser.parse_args(argv)
    try:
        result = validate(
            arguments.source or arguments.mapping,
            arguments.baseline,
            arguments.ifc,
            arguments.manifest,
        )
    except Exception as exception:
        print(f"FAIL: {exception}", file=sys.stderr)
        return 1
    payload = (json.dumps(result, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    if arguments.report:
        arguments.report.parent.mkdir(parents=True, exist_ok=True)
        descriptor, temporary = tempfile.mkstemp(
            dir=arguments.report.parent,
            prefix=f".{arguments.report.name}.",
            suffix=".tmp",
        )
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(payload)
        os.replace(temporary, arguments.report)
    print(payload.decode("utf-8"), end="")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
