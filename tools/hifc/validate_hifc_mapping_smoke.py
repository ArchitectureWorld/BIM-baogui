#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import math
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, Iterable, List, Mapping, Sequence, Tuple

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


def validate(mapping_path: Path, ifc_path: Path) -> Dict[str, object]:
    if not mapping_path.is_file():
        raise AssertionError(f"映射文件不存在：{mapping_path}")
    if not ifc_path.is_file():
        raise AssertionError(f"IFC文件不存在：{ifc_path}")

    mapping_payload = json.loads(mapping_path.read_text(encoding="utf-8"))
    expected_rules = normalize_rules(mapping_payload)
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

    x_key = ("IFCPROJECT", "Pset_申报信息属性集", "基点坐标 X")
    y_key = ("IFCPROJECT", "Pset_申报信息属性集", "基点坐标 Y")
    if actual[x_key]["typed_token"] != "IFCREAL(3353559.52)":
        raise AssertionError(f"X/南北坐标值错误：{actual[x_key]['typed_token']}")
    if actual[y_key]["typed_token"] != "IFCREAL(38345264.397)":
        raise AssertionError(f"Y/东西坐标值错误：{actual[y_key]['typed_token']}")
    if ("IFCPROJECT", "Pset_申报信息属性集", "基点坐标X") in actual:
        raise AssertionError("最终IFC不得双写无空格别名“基点坐标X”。")
    if ("IFCPROJECT", "Pset_申报信息属性集", "基点坐标Y") in actual:
        raise AssertionError("最终IFC不得双写无空格别名“基点坐标Y”。")

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

    return {
        "status": "PASS",
        "ifc": str(ifc_path),
        "stepEntities": len(entities),
        "properties": len(actual),
        "propertySets": len(property_sets),
        "attachments": len(attachments),
        "ownerTypes": sorted(actual_owner_types),
        "extrudedSolids": counts.get("IFCEXTRUDEDAREASOLID", 0),
    }


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Validate the HBR-HIFC full-mapping IFC4 smoke fixture."
    )
    parser.add_argument("--mapping", type=Path, required=True)
    parser.add_argument("--ifc", type=Path, required=True)
    arguments = parser.parse_args()
    try:
        result = validate(arguments.mapping, arguments.ifc)
    except Exception as exception:
        print(f"FAIL: {exception}", file=sys.stderr)
        return 1
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
