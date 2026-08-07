# HBR–H-IFC 全映射结构验证样例 v1.0

本目录用于在继续开发前，冻结并人工检查 **H-IFC属性到底挂在哪里、用什么名称、以什么类型写入**。

## 文件

- `HBR_HIFC_全映射结构验证_v1.0.ifc`：由唯一规则源确定性生成的 IFC4 / ReferenceView_V1.2 综合验证模型。
- `HBR_HIFC_全映射结构验证_v1.0.manifest.json`：源规则指纹、IFC指纹和数量统计。
- `generate_hifc_mapping_smoke.py`：仅依赖Python标准库，直接读取仓库唯一规则源生成IFC。
- `validate_hifc_mapping_smoke.py`：精确回读Owner、Pset、Property、类型、坐标和空间关系。

## 本样例验证的标准挂接结构

```text
IFC Owner
└─ IfcRelDefinesByProperties
   └─ IfcPropertySet
      └─ IfcPropertySingleValue
         └─ typed NominalValue
```

也就是：

```text
对象是谁
→ 挂哪一张属性集
→ 属性集里是哪一个精确字段
→ 字段是什么IFC类型和值
```

## 完整性

本样例包含：

- 359条Canonical属性；
- 52个PropertySet；
- 52个`IfcRelDefinesByProperties`；
- 14类实际属性挂接Owner；
- `IfcProject → IfcSite → IfcBuilding → IfcBuildingStorey → IfcSpace`显式层级；
- 真实`IfcSpatialZone`；
- `IfcOrganization + IfcActor`组织包装；
- 墙、板、屋顶、窗、楼梯段、门、风管段等简单可视几何。

主要实体编号：

```text
#25  IfcProject
#26  IfcSite
#27  IfcBuilding
#28  IfcBuildingStorey
#41  IfcSpace
#54  IfcSpatialZone
#146 IfcOrganization（业务组织）
#147 IfcActor（组织Pset实际挂接Owner）
```

重点Pset编号：

```text
#317 Pset_申报信息属性集        → #25 IfcProject
#223 Pset_建筑技术信息属性集    → #27 IfcBuilding
#244 Pset_建筑楼层信息属性集    → #28 IfcBuildingStorey
#584 Pset_建筑区域信息属性集    → #54 IfcSpatialZone
#179 Pset_组织通用属性集        → #147 IfcActor → #146 IfcOrganization
```

## 强约束

1. 最终字段名只使用 `specs/hbr-rules/v1/source/hbr_rule_source.v1.json` 的Canonical名称。
2. 唯一字段身份为 `(实际挂接Owner实体, PropertySet, Property)`。
3. `基点坐标 X`为南北坐标，`基点坐标 Y`为东西坐标；最终IFC不双写无空格别名。
4. `Pset_组织通用属性集`挂到`IfcActor`，其`TheActor`指向`IfcOrganization`；不得把资源级`IfcOrganization`非法直接塞入`IfcRelDefinesByProperties.RelatedObjects`。
5. `Pset_建筑区域信息属性集`和`Pset_停车场信息属性集`挂到真实`IfcSpatialZone`。
6. 样例中的Boolean验证值统一使用`.T.`，用于避免IFCFlux 0.1.0部分`.F.`值被误判为空；这不是业务默认值策略。
7. 这是**结构全映射试件**，不是一个真实项目的业务合规申报模型。很多互斥或有条件Pset被集中放到同一个示例Owner，仅用于检查映射路径。

## 仓库内生成与验证

```powershell
python tools/hifc/generate_hifc_mapping_smoke.py `
  --mapping specs/hbr-rules/v1/source/hbr_rule_source.v1.json `
  --source-commit 1372497e20322d3f90a01360f3bd9d4829bdedf9 `
  --output tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.ifc `
  --manifest tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.manifest.json

python tools/hifc/validate_hifc_mapping_smoke.py `
  --mapping specs/hbr-rules/v1/source/hbr_rule_source.v1.json `
  --ifc tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.ifc
```

当前文件已通过确定性结构验证和二次生成一致性验证。只有在目标Windows环境中被IFCFlux/H-IFC软件实际打开、识别并产生检查结果后，才能标记为“目标软件验收通过”。
