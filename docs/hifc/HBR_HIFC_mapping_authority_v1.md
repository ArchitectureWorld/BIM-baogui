# HBR–H-IFC 映射唯一依据 v1

> 冻结日期：2026-08-07
> 适用分支：`fix/official-hifc-hardening-v090`
> 机器唯一规则源：`specs/hbr-rules/v1/source/hbr_rule_source.v1.json`

## 1. 唯一映射主键

任何 H-IFC 属性都必须由以下三元组唯一定位：

```text
IFC Entity + PropertySet + Property
```

数据类型、Canonical Unit、Owner 定位规则是强约束。最终 IFC 不按“参数名字相似”猜测，也不在错误对象上寻找同名字段。

标准挂接链固定为：

```text
IFC Owner
  ← IfcRelDefinesByProperties.RelatedObjects
IfcRelDefinesByProperties
  → IfcPropertySet
      → IfcPropertySingleValue
          → declared IFC typed value
```

## 2. 当前规则规模

规则包 `HBR-WUHAN-PLANNING / 1.0.0` 当前包含：

- 359 条唯一属性路径；
- 356 条 MVD 字段；
- 3 条已验证 H-IFC 扩展字段；
- 52 个 PropertySet；
- 14 类最终属性 Owner。

任何人读表、生成表、安装 Revit 参数、扫描模型或写 IFC，都必须从机器唯一规则源生成，不允许维护第二份可编辑映射表。

## 3. Owner 结构

| 业务角色 | IFC Owner | PropertySet 挂接对象 |
|---|---|---|
| 项目 | `IfcProject` | `IfcProject` |
| 场地 | `IfcSite` | `IfcSite` |
| 建筑 | `IfcBuilding` | `IfcBuilding` |
| 楼层 | `IfcBuildingStorey` | 同实体 |
| 空间 | `IfcSpace` | 同实体 |
| 建筑区域 | `IfcSpatialZone` | 同实体 |
| 墙 | `IfcWall` | 同实体 |
| 板 | `IfcSlab` | 同实体 |
| 屋顶 | `IfcRoof` | 同实体 |
| 窗 | `IfcWindow` | 同实体 |
| 楼梯段 | `IfcStairFlight` | 同实体 |
| 门 | `IfcDoor` | 同实体 |
| 风管段 | `IfcDuctSegment` | 同实体 |
| 组织 | `IfcActor` | Pset 挂 `IfcActor`；`IfcActor.TheActor` 指向 `IfcOrganization` |

## 4. 坐标冻结规则

最终 Canonical 名称固定为：

```text
IfcProject
└─ Pset_申报信息属性集
   ├─ 基点坐标X : IfcReal
   ├─ 基点坐标Y : IfcReal
   └─ 基点高程   : IfcReal
```

业务语义固定为：

```text
X = Northing = 南北坐标
Y = Easting  = 东西坐标
```

`基点坐标 X / 基点坐标 Y` 等带空格旧名称只能作为输入迁移别名，不允许输出到最终 IFC。

## 5. 全映射结构验证文件

`tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.ifc` 是 IFCFlux 人工验收字节锚点；source-derived generator 另负责确定性结构回归。两者共享同一 identity/类型/Owner/数量合同，不要求样例值与 GUID 逐字节相同。

试件包含：

- IFC4 / ReferenceView_V1.2 文件头；
- Project → Site → Building → Storey → Space 的空间层级；
- 真实 `IfcSpatialZone`；
- `IfcOrganization + IfcActor` 包装关系；
- 359 个 `IfcPropertySingleValue`；
- 52 个 `IfcPropertySet`；
- 52 个 `IfcRelDefinesByProperties`；
- 14 类 Owner；
- 9 个可见拉伸几何对象。

试件为了集中核查路径，会把同一 Owner 类别下互斥或有条件的 Pset 汇总到一个样例对象上；这不代表真实项目应无条件填写全部字段。真实项目仍必须经过模型类型、条件和适用性规则筛选。

## 6. 验收边界

该字节锚点已由用户确认 IFCFlux 人工运行通过；当次未记录 IFCFlux 版本，也没有独立机器导出报告或截图。试件中的 `IfcSpatialZone` 和 `IfcOrganization` owner 尚未由 GH 完整实现；本证据只证明目标 IFC 结构，不证明 Stage03 生产完整支持。
