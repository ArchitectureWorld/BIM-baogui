"""Read-only IFC Pset ownership inventory for user-confirmed successful cases."""

import argparse
from collections import Counter, defaultdict
import csv
from datetime import datetime
import hashlib
import html
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / ".deps"))
import ifcopenshell
import ifcopenshell.util.element


def simple(value):
    if value is None or isinstance(value, (str, int, float, bool)):
        return value
    if isinstance(value, (tuple, list)):
        return [simple(v) for v in value]
    if hasattr(value, "wrappedValue"):
        return simple(value.wrappedValue)
    return {"ifc_type": value.is_a(), "step_id": value.id(), "name": getattr(value, "Name", None)}


def property_record(prop):
    nominal = getattr(prop, "NominalValue", None)
    result = {"name": prop.Name, "kind": prop.is_a(), "nominal_type": nominal.is_a() if nominal is not None else None,
              "value": simple(nominal), "explicit_unit": str(prop.Unit) if getattr(prop, "Unit", None) else None}
    if prop.is_a("IfcComplexProperty"):
        result["children"] = [property_record(p) for p in prop.HasProperties]
    for key in ("EnumerationValues", "ListValues", "UpperBoundValue", "LowerBoundValue", "SetPointValue", "DefiningValues", "DefinedValues", "PropertyReference"):
        if hasattr(prop, key):
            result[key] = simple(getattr(prop, key))
    return result


def property_sets(definition):
    if isinstance(definition, (tuple, list)):
        return [p for d in definition for p in property_sets(d)]
    if definition.is_a("IfcPropertySet"):
        return [definition]
    if definition.is_a("IfcPropertySetDefinitionSet"):
        return property_sets(definition[0])
    return []


def inventory_model(model, known_names):
    sets = model.by_type("IfcPropertySet")
    psets = [{"id": p.id(), "global_id": p.GlobalId, "name": p.Name,
              "reference_hifc": p.Name in known_names, "property_names": [x.Name for x in p.HasProperties],
              "properties": [property_record(x) for x in p.HasProperties] if p.Name in known_names or p.Name in {"IFC参数(实例)", "IFC参数(类型)"} else []}
             for p in sets]
    parents, type_relations = {}, defaultdict(list)
    for rel in model.by_type("IfcRelAggregates") + model.by_type("IfcRelNests"):
        for child in rel.RelatedObjects:
            parents[child.id()] = rel.RelatingObject
    for rel in model.by_type("IfcRelContainedInSpatialStructure"):
        for child in rel.RelatedElements:
            parents[child.id()] = rel.RelatingStructure
    for rel in model.by_type("IfcRelDefinesByType"):
        for owner in rel.RelatedObjects:
            type_relations[rel.RelatingType.id()].append((owner, rel.id()))
    owner_cache = {}

    def owner_info(owner):
        if owner.id() not in owner_cache:
            building, level, current, seen = None, None, owner, set()
            while current is not None and current.id() not in seen:
                seen.add(current.id())
                if current.is_a("IfcBuilding"):
                    building = current.Name
                if current.is_a("IfcBuildingStorey"):
                    level = current.Name
                current = parents.get(current.id())
            owner_cache[owner.id()] = {"owner_id": owner.id(), "owner_global_id": getattr(owner, "GlobalId", None),
                "owner_entity": owner.is_a(), "owner_name": getattr(owner, "Name", None), "owner_tag": getattr(owner, "Tag", None),
                "building": building, "level": level, "owner_is_type": owner.is_a("IfcTypeObject"),
                "has_representation": getattr(owner, "Representation", None) is not None}
        return owner_cache[owner.id()]

    bindings = {}

    def add(owner, pset, origin, relation_id=None, type_owner=None):
        key = (owner.id(), pset.id(), origin)
        if key not in bindings:
            bindings[key] = dict(owner_info(owner), pset_id=pset.id(), pset_name=pset.Name, origin=origin,
                                 reference_hifc=pset.Name in known_names, relation_ids=[], type_owner_id=type_owner.id() if type_owner else None)
        if relation_id is not None and relation_id not in bindings[key]["relation_ids"]:
            bindings[key]["relation_ids"].append(relation_id)

    for rel in model.by_type("IfcRelDefinesByProperties"):
        for pset in property_sets(rel.RelatingPropertyDefinition):
            for owner in rel.RelatedObjects:
                add(owner, pset, "direct", rel.id())
    for kind in model.by_type("IfcTypeObject"):
        for pset in getattr(kind, "HasPropertySets", ()) or ():
            if not pset.is_a("IfcPropertySet"):
                continue
            add(kind, pset, "type_definition")
            for owner, relation in type_relations.get(kind.id(), []):
                add(owner, pset, "type_inherited", relation, kind)
    bound = {b["pset_id"] for b in bindings.values()}
    return {"psets": psets, "bindings": list(bindings.values()), "orphan_pset_ids": [p.id() for p in sets if p.id() not in bound]}


def summarize(inventory, definitions):
    groups = defaultdict(list)
    by_set = defaultdict(list)
    for pset in inventory["psets"]:
        groups[pset["name"]].append(pset)
    for binding in inventory["bindings"]:
        by_set[binding["pset_name"]].append(binding)
    rows = []
    for name, psets in sorted(groups.items()):
        bindings = by_set[name]
        direct = {b["owner_id"]: b for b in bindings if b["origin"] == "direct" and not b["owner_is_type"]}
        inherited = {b["owner_id"]: b for b in bindings if b["origin"] == "type_inherited"}
        types = {b["owner_id"]: b for b in bindings if b["origin"] == "type_definition"}
        properties = sorted({p for s in psets for p in s["property_names"]})
        expected = defaultdict(set)
        for definition in definitions:
            if definition["property_set"] == name:
                expected[definition["binding"]].add(definition["ifc_property"])
        rows.append({"pset": name, "reference_hifc": psets[0]["reference_hifc"], "pset_instances": len(psets),
            "direct_owner_entities": dict(Counter(b["owner_entity"] for b in direct.values())),
            "type_definition_entities": dict(Counter(b["owner_entity"] for b in types.values())),
            "inherited_owner_entities": dict(Counter(b["owner_entity"] for b in inherited.values())),
            "effective_occurrences": len(set(direct) | set(inherited)), "direct_occurrences": len(direct),
            "inherited_occurrences": len(inherited), "type_definitions": len(types), "observed_properties": properties,
            "observed_empty_properties": sorted({p["name"] for s in psets for p in s["properties"] if p["kind"] == "IfcPropertySingleValue" and p["value"] is None}),
            "reference_fields_by_binding": {k: sorted(v) for k, v in expected.items()},
            "not_observed_reference_fields": sorted(set().union(*expected.values()) - set(properties)) if expected else [],
            "extra_observed_fields": sorted(set(properties) - set().union(*expected.values())) if expected else []})
    return rows


def write_csv(path, rows, fields):
    with path.open("w", encoding="utf-8-sig", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=fields)
        writer.writeheader()
        writer.writerows(rows)


def table(headers, rows):
    esc = lambda value: html.escape(str(value) if value is not None else "—")
    return "<table><thead><tr>" + "".join("<th>" + esc(x) + "</th>" for x in headers) + "</tr></thead><tbody>" + "".join(
        "<tr>" + "".join("<td>" + esc(x) + "</td>" for x in row) + "</tr>" for row in rows) + "</tbody></table>"


def render_report(models, profiles):
    blocks = []
    for model in sorted(models, key=lambda x: (x["scope"] != "总图", x["file"])):
        business = [r for r in model["summary"] if r["reference_hifc"] and r["effective_occurrences"] > 0]
        blocks.append("<section><h2>" + html.escape(model["file"]) + "</h2><p>" + html.escape(model["scope"]) +
                      " · IFC4 · " + str(model["product_count"]) + " 个 IFC 产品 · " + str(model["hifc_occurrence_count"]) + " 个载体带有参考 HIFC 属性集（已计入类型继承）。</p>")
        rows = []
        for r in business:
            entities = "、".join(f"{k} ×{v}" for k, v in r["direct_owner_entities"].items())
            if r["type_definitions"]:
                entities += ("；" if entities else "") + "类型：" + "、".join(f"{k} ×{v}" for k, v in r["type_definition_entities"].items())
            mode = "实例直接挂载" if r["direct_occurrences"] else "类型定义 → 实例继承"
            fields = "、".join(p + ("（存在空值）" if p in r["observed_empty_properties"] else "") for p in r["observed_properties"])
            rows.append([r["pset"], entities, mode, r["effective_occurrences"], fields])
        blocks.append(table(["成功案例中的属性集", "实际载体", "挂载方式", "覆盖实例/信息载体数", "实际出现的字段"], rows))
        gaps = [[r["pset"], "、".join(r["not_observed_reference_fields"])] for r in business if r["not_observed_reference_fields"]]
        if gaps:
            blocks.append("<h3>配置中有，但这个成功案例没有输出的字段</h3><p>这些是配置与实际文件的差异，不能据此认定案例失败或字段必须补填；实例/类型配置分别保留在 JSON。</p>" + table(["属性集", "本案例未观察到"], gaps))
        bindings = [r for r in model["inventory"]["bindings"] if r["reference_hifc"] and r["origin"] != "type_definition"]
        blocks.append("<details><summary>展开 HIFC 载体定位明细（" + str(len(bindings)) + " 条挂载关系）</summary>" + table(
            ["STEP ID", "IFC 类", "Revit Tag", "构件/信息载体名称", "楼层", "属性集", "来源"],
            [["#" + str(b["owner_id"]), b["owner_entity"], b["owner_tag"], b["owner_name"], b["level"], b["pset_name"], b["origin"]] for b in bindings]) + "</details>")
        blocks.append("<details><summary>展开全部 IFC 产品类及数量</summary>" + table(["IFC 类", "数量"], sorted(model["product_entities"].items())) + "</details>")
        blocks.append("<p class='source'>来源：" + html.escape(model["path"]) + "<br>SHA-256：" + model["sha256_before"] + "<br>读取后哈希一致；全部普通 Pset 及类型挂载另见 CSV。</p></section>")
    common = profiles["single_building_common_psets"]
    common_names = "、".join(p["pset"] for p in common)
    return """<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>报规成功案例 · HIFC 构件与属性集清单</title>
<style>body{margin:0;background:#f3f6f8;color:#183344;font:15px/1.7 'Microsoft YaHei UI',sans-serif}main{max-width:1440px;margin:auto;padding:34px}h1{font-size:30px;margin:0 0 12px}h2{font-size:22px}h3{font-size:17px}section,.intro{background:white;border:1px solid #d8e1e7;border-radius:10px;padding:24px;margin:20px 0}table{border-collapse:collapse;width:100%;font-size:13px;table-layout:auto}th,td{border-bottom:1px solid #e1e7ec;padding:10px;text-align:left;vertical-align:top;overflow-wrap:anywhere}th{background:#eaf1f5}td:first-child{min-width:185px}details{margin:18px 0}summary{cursor:pointer;font-weight:600;color:#216d89}.source{font-size:12px;color:#687d8b;overflow-wrap:anywhere}.tag{color:#94600e;font-weight:600}a{color:#216d89}.note{border-left:4px solid #c89335;padding-left:14px}input{padding:9px 12px;width:300px;max-width:90%;border:1px solid #bcccd6;border-radius:6px}tr.hidden{display:none}@media(max-width:800px){main{padding:14px}section{padding:12px;overflow:auto}table{min-width:820px}}</style>
<main><h1>报规成功案例 · HIFC 构件与属性集清单</h1><p class="tag">全部为用户确认已通过的报规阶段 IFC；当前以此建立报规案例参考清单。</p><div class="intro">
<p>报规阶段下，总图与单体分别建立参考范围。所有计数均从真实挂载关系读取，同时检查实例直接属性集和类型继承属性集；同一构件可同时带有业务属性集和通用类型属性集，不能按属性集条数重复计算构件。</p>
<p>两个单体共同观察到：""" + html.escape(common_names) + """。厂房另有类型窗属性集，后勤楼未出现，保留为案例差异。</p>
<p class="note">数量仅是该案例的观察值，不是新项目应照抄的数量。未出现在这三份文件中的类目保持未知；墙、门、屋顶等普通 IFC 构件不因自带通用 Pset 就被认定为 HIFC 特殊必需项。0/false 是实际值；单位和业务口径需分别核验。</p>
<p><a href="Pset挂载全清单.csv">全部 Pset 挂载 CSV</a> · <a href="HIFC字段与原值.csv">HIFC 字段原值 CSV</a> · <a href="属性集汇总.csv">属性集汇总 CSV</a> · <a href="inventory.json">完整证据 JSON</a> · <a href="case-profiles.json">后续插件参考清单 JSON</a></p>
<input id="search" placeholder="在表格中搜索属性集、构件名或 Tag"></div>""" + "".join(blocks) + """<p class="source">读取程序仅生成 V1 目录中的报告，未修改源 IFC。平台通过状态来自用户确认，本次不重新宣称平台验收。</p></main>
<script>document.getElementById('search').addEventListener('input',e=>{const s=e.target.value.trim().toLowerCase();document.querySelectorAll('tbody tr').forEach(r=>r.classList.toggle('hidden',!!s&&!r.textContent.toLowerCase().includes(s)));});</script></html>"""


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input-dir", required=True, type=Path)
    parser.add_argument("--site-file", required=True)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    paths = sorted(args.input_dir.glob("*.ifc"))
    if len(paths) != 3 or args.site_file not in {p.name for p in paths}:
        parser.error("本轮要求明确指定总图，并读取同目录三份 IFC。")
    contract_path = ROOT / "reference/gonggui/contract.json"
    contract = json.loads(contract_path.read_text(encoding="utf-8-sig"))
    known = {e["property_set"] for e in contract["entries"] if not e["exporter_native_group"]}
    args.output.mkdir(parents=True, exist_ok=True)
    models, audit_failures, excluded_predefined = [], [], []
    for path in paths:
        before = hashlib.sha256(path.read_bytes()).hexdigest()
        model = ifcopenshell.open(str(path))
        inventory = inventory_model(model, known)
        summary = summarize(inventory, contract["entries"])
        owner_sets = defaultdict(set)
        for binding in inventory["bindings"]:
            owner_sets[binding["owner_id"]].add(binding["pset_name"])
        for owner_id, names in owner_sets.items():
            independent = set()
            # get_psets also includes predefined door/window property definitions,
            # which are not IfcPropertySet entities with HasProperties.
            for name, values in ifcopenshell.util.element.get_psets(model[owner_id], psets_only=True).items():
                definition = model[values["id"]]
                if definition.is_a("IfcPropertySet"):
                    independent.add(name)
                else:
                    excluded_predefined.append({"file": path.name, "owner": owner_id, "definition_id": definition.id(), "entity": definition.is_a(), "name": name})
            if independent != names:
                audit_failures.append({"file": path.name, "owner": owner_id, "inventory": sorted(names), "independent": sorted(independent)})
        after = hashlib.sha256(path.read_bytes()).hexdigest()
        if before != after:
            audit_failures.append({"file": path.name, "error": "SOURCE_CHANGED"})
        hifc_owners = {r["owner_id"] for r in inventory["bindings"] if r["reference_hifc"] and not r["owner_is_type"]}
        models.append({"file": path.name, "path": str(path.resolve()), "scope": "总图" if path.name == args.site_file else "单体建筑",
            "workflow_stage": "报规", "workflow_stage_basis": "USER_CONFIRMED_ALL_THREE_FILES",
            "schema": model.schema, "sha256_before": before, "sha256_after": after, "source_unchanged": before == after,
            "success_status_basis": "USER_CONFIRMED_PASSED_CASE", "originating_system": model.header.file_name.originating_system,
            "product_count": len(model.by_type("IfcProduct")), "product_entities": dict(Counter(e.is_a() for e in model.by_type("IfcProduct"))),
            "hifc_occurrence_count": len(hifc_owners), "owners_crosschecked": len(owner_sets), "summary": summary, "inventory": inventory})
    observed = lambda m: {r["pset"] for r in m["summary"] if r["reference_hifc"] and r["effective_occurrences"] > 0}
    single = [m for m in models if m["scope"] == "单体建筑"]
    common = set.intersection(*(observed(m) for m in single))
    profiles = {"schema": "hifc.success-case-reference/1.0", "created_local": datetime.now().astimezone().isoformat(),
        "workflow_stage": "报规", "workflow_stage_basis": "USER_CONFIRMED_ALL_THREE_FILES",
        "basis": "User-confirmed successful IFC case; observed presence and fields, not official mandatory rules",
        "count_policy": "OBSERVED_COUNTS_ONLY_DO_NOT_COPY_TO_NEW_PROJECTS", "unobserved_policy": "UNKNOWN_NOT_NOT_APPLICABLE",
        "field_requirement_policy": "OBSERVED_PROPERTY_PRESENCE_NOT_MANDATORY_NONEMPTY_RULE",
        "reference_contract_sha256": hashlib.sha256(contract_path.read_bytes()).hexdigest(),
        "models": [{"file": m["file"], "scope": m["scope"], "workflow_stage": m["workflow_stage"], "path": m["path"], "sha256": m["sha256_before"],
                    "items": [r for r in m["summary"] if r["pset"] in observed(m)]} for m in models],
        "single_building_common_psets": [{"pset": name, "observed_properties_common": sorted(set.intersection(*(
            set(next(r for r in m["summary"] if r["pset"] == name)["observed_properties"]) for m in single)))} for name in sorted(common)],
        "single_building_case_additions": [{"file": m["file"], "psets": sorted(observed(m) - common)} for m in single]}
    result = {"schema": "hifc.success-case-inventory/1.0", "workflow_stage": "报规", "read_only": True, "all_source_hashes_unchanged": all(m["source_unchanged"] for m in models),
              "ownership_scope": "IfcPropertySet with HasProperties; quantities and predefined door/window definitions recorded separately",
              "excluded_predefined_definitions": excluded_predefined,
              "independent_owner_pset_crosscheck_passed": not audit_failures, "audit_failures": audit_failures, "models": models}
    (args.output / "inventory.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    (args.output / "case-profiles.json").write_text(json.dumps(profiles, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    summary_csv, owner_csv, field_csv = [], [], []
    for m in models:
        ps_by_id = {p["id"]: p for p in m["inventory"]["psets"]}
        for r in m["summary"]:
            summary_csv.append({"阶段": m["workflow_stage"], "文件": m["file"], "属性集": r["pset"], "参考HIFC集": r["reference_hifc"], "属性集实例数": r["pset_instances"],
                "直接覆盖数": r["direct_occurrences"], "类型定义数": r["type_definitions"], "继承覆盖数": r["inherited_occurrences"],
                "去重载体数": r["effective_occurrences"], "实测字段": " | ".join(r["observed_properties"]), "配置有但未观察字段": " | ".join(r["not_observed_reference_fields"])})
        for b in m["inventory"]["bindings"]:
            owner_csv.append({"阶段": m["workflow_stage"], "文件": m["file"], "STEP_ID": b["owner_id"], "GlobalId": b["owner_global_id"], "IFC类": b["owner_entity"], "构件名": b["owner_name"],
                "Revit_Tag": b["owner_tag"], "楼栋": b["building"], "楼层": b["level"], "属性集": b["pset_name"], "Pset_STEP_ID": b["pset_id"], "来源": b["origin"],
                "类型来源_ID": b["type_owner_id"], "参考HIFC集": b["reference_hifc"], "有Representation": b["has_representation"], "字段名": " | ".join(ps_by_id[b["pset_id"]]["property_names"])})
            if not b["reference_hifc"]:
                continue
            for p in ps_by_id[b["pset_id"]]["properties"]:
                field_csv.append({"阶段": m["workflow_stage"], "文件": m["file"], "STEP_ID": b["owner_id"], "IFC类": b["owner_entity"], "Revit_Tag": b["owner_tag"], "属性集": b["pset_name"],
                    "来源": b["origin"], "字段": p["name"], "IFC值类型": p["nominal_type"], "原值_JSON": json.dumps(p["value"], ensure_ascii=False), "显式单位": p["explicit_unit"]})
    for name, rows in [("属性集汇总.csv", summary_csv), ("Pset挂载全清单.csv", owner_csv), ("HIFC字段与原值.csv", field_csv)]:
        write_csv(args.output / name, rows, list(rows[0]))
    (args.output / "成功案例_HIFC构件与属性集清单.html").write_text(render_report(models, profiles), encoding="utf-8")
    print(json.dumps({"all_source_hashes_unchanged": result["all_source_hashes_unchanged"], "independent_crosscheck_passed": not audit_failures,
        "owner_binding_rows": len(owner_csv), "hifc_property_rows": len(field_csv), "models": [{"file": m["file"], "hifc_carriers": m["hifc_occurrence_count"],
        "business_psets": len(observed(m))} for m in models]}, ensure_ascii=False))
    return 1 if audit_failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
