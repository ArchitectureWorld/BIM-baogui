# 官方插件兼容写入设计

## 1. 唯一产品目标

```text
GH 输入或计算
→ 按指南与官方软件要求写入 Revit
→ 官方 H-IFC 插件导出
→ 官方检查软件正确识别
```

最终 IFC 只由官方插件生成。本产品不开发 IFC 导出器，不修改 IFC 文件，也不使用导出后补属性作为正式产品路径。

**禁止将 POST_EXPORT_ENRICH 作为当前产品路径。** 旧基线中的 IFC 后处理只保留为历史研究，不进入 GHA 的运行链路、修复链路或完成定义。

**不以 Revit 参数回读作为最终兼容性结论。** 参数写入和回读只证明 Revit 内部一致；只有官方插件导出并经检查软件识别，才能把字段标记为兼容通过。

## 2. 证据优先级

每条写入规则必须区分：

1. 指南/报规要求：决定必填、条件必填、建议或不适用；
2. 官方检查软件规则：决定 IFC 实体、PropertySet、属性名、数据类型、单位和值；
3. 官方辅助设计/导出插件证据：决定 Revit 对象、源参数名称、作用域和读取路径；
4. 我们的实现决策：只能补充尚未确认部分，必须显式标记为未验证。

当前确认：166 条属性、16 个 PropertySet、9 类 IFC 实体，以及 4 条官方显式 Revit→IFC 对象映射。

### 2.1 官方源参数名称

已提取规则中 166 条属性的 `sourceParameterOverride` 均为空。当前有效解释为：

```text
sourceParameterOverride 有值 → 官方插件读取该覆盖名
sourceParameterOverride 为空 → 官方插件按 IFC 属性原名读取 Revit 参数
```

因此，单独创建 `HIFC.<PropertySet>.<Property>` 只能解决内部唯一标识，不能保证官方插件找到值。

## 3. 20260731test02 实测证据

用户提交的 `20260731test02.rvt` 与 `20260731test02.ifc` 证明：

- RVT 内嵌 Stage 01 版本仍为 `0.2.0`；
- RVT 中没有任何 `HIFC.*` 参数名或候选 GUID；
- IFC 中没有任何 H-IFC 项目级 PropertySet；
- IfcProject/IfcSite/IfcBuilding 仍为 `Project`、`Site`、`Building`；
- 所有 10 个导出属性集只挂在一个 IfcWall 上；
- Site 经纬度、高程、IfcMapConversion 和 IfcProjectedCRS 均缺失。

诊断记录：`docs/reviews/2026-08-01-test02-rvt-ifc-root-cause.md`。

## 4. v0.8.0 双投影模型

每条允许写入的标准属性同时产生两种 Revit 投影：

```text
CANONICAL_INTERNAL
HIFC.<PropertySet>.<IFCProperty>
用途：唯一标识、跨 PropertySet 防冲突、调试与回读

OFFICIAL_EXACT_SOURCE_NAME
sourceParameterOverride ?? IFCProperty
用途：满足官方插件的名称读取契约
```

### 4.1 精确源参数处理

- Revit 已存在唯一的精确同名参数时，直接复用；
- 不存在时，创建确定性 GUID 的精确同名共享参数；
- 写入后同时回读内部参数和官方源参数；
- 任一写入、类型、绑定或回读失败时整体回滚；
- 同一目标对象上若多个标准属性共享同一官方源名称，返回 `OFFICIAL_SOURCE_NAME_AMBIGUOUS`，不得静默覆盖。

当前已知 IfcProject 同名冲突：

- `地籍信息属性集.建筑物编码` / `登记信息属性集.建筑物编码`；
- `区划信息属性集.备注` / `申报信息属性集.备注`。

## 5. 当前允许写入的对象范围

| IFC 实体 | 官方显式对象映射 | 当前写入策略 |
|---|---|---|
| IfcProject | ProjectInformation | 允许双投影，必须完成官方导出验证 |
| IfcBuilding | ProjectInformation | 允许双投影，必须完成官方导出验证 |
| IfcBuildingStorey | Level | 必须提供明确 Level 目标 |
| IfcSpace | Room | 必须提供明确 Room 目标 |
| IfcOrganization | 未确认 | 阻断标准兼容写入，保留在工作流存储 |
| IfcSite | 未确认 | 阻断标准兼容写入，等待官方协议 |
| IfcSpatialZone | 未确认 | 阻断标准兼容写入，等待官方协议 |
| IfcDoor | 未提取到显式映射 | 阻断正式兼容声明，等待实测 |
| IfcDuctSegment | 未提取到显式映射 | 阻断正式兼容声明，等待实测 |

未确认对象不得默认写到 ProjectInformation。

## 6. Stage 01 文件初始化

### 6.1 数据分类

- `IfcProject` 标准字段：通过字段注册表与 166 条规则数据驱动写入；
- `IfcOrganization` 标准字段：完整保存在初始化载荷中，官方组织协议确认前不伪装成 IfcProject 参数；
- HBR 工作流字段：FileGuid、模型文件类型、版本、哈希等只进入 Extensible Storage/HBR_FileContext；
- 规划控制目标：目标值保存在 HBR 上下文，模型计算实际值才能写入实际指标属性。

### 6.2 旧版自动升级

当 RVT 已存在旧版 Stage 01 初始化记录且版本不同于当前 schema：

```text
读取旧载荷
→ 保留已有业务值
→ 自动更新 workflowVersion
→ 执行 v0.8.0 双投影
→ 更新初始化存储
```

版本升级不要求用户启用“允许重新初始化”；同版本覆盖仍需显式允许。

### 6.3 坐标语义

```text
X = 南北坐标 = North/South
Y = 东西坐标 = East/West
```

读取、写入、参数投影、回读和测试必须保持一致，输入单位为米。

### 6.4 提交流程

```text
Validate Stage01
→ 判断同版本覆盖或旧版迁移
→ 写 Revit 项目配置
→ 写 HBR 初始化存储
→ 解析非空 IfcProject 标准字段
→ 安装/复用内部唯一参数
→ 安装/复用官方精确源参数
→ 双写数值
→ Regenerate
→ 原生配置 + 双参数回读
→ Commit/Assimilate 或整体 Rollback
```

`Stage01Storage` 只负责存储，不允许隐式安装参数或写业务属性。

## 7. 参数与单位

运行时读取：IFC 实体、PropertySet、IFC 属性、源参数覆盖名、内部参数 GUID/名称、Revit 类别、实例/类型作用域、数据类型与写入策略。

支持 Text、Integer、YesNo、Length、Area、Volume、Angle 和无量纲 Number。Angle 输入按度，Length/Area/Volume 分别按 m/m²/m³ 输入并转换为 Revit 内部单位。

## 8. 兼容状态

- `OFFICIAL_EXTRACTED`：官方文件中明确存在；
- `IMPLEMENTATION_DECISION_UNVERIFIED`：我们的 GUID、承载或补充决策；
- `REVIT_WRITE_VERIFIED`：内部参数和官方精确源参数均写入、回读通过；
- `OFFICIAL_EXPORT_VERIFIED`：官方插件导出后 IFC 值正确；
- `CHECKER_VERIFIED`：官方检查软件正确识别。

GHA 不得把 `REVIT_WRITE_VERIFIED` 显示成“官方兼容通过”。

## 9. 最终验收闭环

```text
测试输入值
= GH 规范化值
= Revit 内部唯一参数值
= Revit 官方精确源参数值
= 官方插件导出的 IFC 值
= 检查软件识别值
```

验收资产包括 Golden RVT、官方插件直接生成的 Golden IFC、导出 manifest 和检查报告。任何参数名、对象宿主、单位、插件版本或规则包变化都必须重新跑闭环。

## 10. 当前完成定义

进入稳定版必须同时满足：

1. Stage 01 所有非空 IfcProject 字段都有映射或明确例外；
2. 旧版初始化能够自动迁移；
3. 内部唯一参数和官方精确源参数双写、双回读；
4. X/Y 坐标语义正确；
5. 非项目属性不会默认写入 ProjectInformation；
6. 未确认实体明确阻断；
7. GHA 编译、契约测试和核心测试通过；
8. 使用 v0.8.0 创建/升级 Golden RVT；
9. 官方插件生成 Golden IFC；
10. 官方检查软件对目标字段识别通过。
