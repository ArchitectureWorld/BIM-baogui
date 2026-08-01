# 官方插件兼容写入设计

## 1. 唯一产品目标

本产品只解决一件事：

```text
GH 输入或计算
→ 按指南与官方软件要求写入 Revit
→ 官方 H-IFC 插件导出
→ 官方检查软件正确识别
```

最终 IFC 继续由官方插件生成。本产品不开发 IFC 导出器，不修改 IFC 文件，也不使用导出后补属性作为正式产品路径。

**不以 Revit 参数回读作为最终兼容性结论。** 参数写入和回读只证明 Revit 内部写入一致；只有官方插件导出并经检查软件识别，才能把字段标记为兼容通过。

**禁止将 POST_EXPORT_ENRICH 作为当前产品路径。** 旧基线中的 IFC 后处理、Organization/SpatialZone 导出后创建等方案保留为历史研究，不进入当前 GHA 的完成定义。

## 2. 证据优先级

每条写入规则必须区分以下证据：

1. **指南/报规要求**：决定字段是否必填、条件必填、建议或不适用。
2. **官方检查软件规则**：决定 IFC 实体、PropertySet、属性名、数据类型、单位和值判定。
3. **官方辅助设计/导出插件证据**：决定 Revit 对象、参数名称、参数作用域、存储方式和导出读取路径。
4. **我们的实现决策**：只能用于尚未确认部分，必须标记为未验证，不能冒充官方协议。

当前已经确认：166 条属性、16 个 PropertySet、9 类 IFC 实体，以及 4 条官方显式 Revit→IFC 对象映射。当前自行生成的共享参数名与 UUIDv5 GUID 属于实现决策，必须经过官方插件导出实测后才能标记为兼容。

## 3. 当前允许写入的对象范围

| IFC 实体 | 官方显式对象映射 | 当前写入策略 |
|---|---|---|
| IfcProject | ProjectInformation | 允许写入，但必须完成官方导出验证 |
| IfcBuilding | ProjectInformation | 允许写入，但必须完成官方导出验证 |
| IfcBuildingStorey | Level | 必须提供明确 Level 目标 |
| IfcSpace | Room | 必须提供明确 Room 目标 |
| IfcOrganization | 未确认 | 阻断标准兼容写入，保留在工作流存储 |
| IfcSite | 未确认 | 阻断标准兼容写入，等待官方协议 |
| IfcSpatialZone | 未确认 | 阻断标准兼容写入，等待官方协议 |
| IfcDoor | 未提取到显式映射 | 阻断正式兼容声明，等待实测 |
| IfcDuctSegment | 未提取到显式映射 | 阻断正式兼容声明，等待实测 |

未确认对象不得默认写到 ProjectInformation，也不得因为 Revit 原生 IFC 通常能够导出某类构件，就直接宣称官方 H-IFC 插件能够识别其自定义属性。

## 4. Stage 01 文件初始化

### 4.1 数据分类

Stage 01 数据分为：

- `IfcProject` 标准字段：按照字段注册表与属性映射数据驱动写入；禁止只维护 10 个硬编码字段。
- `IfcOrganization` 标准字段：完整保存在初始化载荷中；在官方插件组织数据写入协议确认前，不伪装成 ProjectInformation 参数。
- HBR 工作流字段：FileGuid、模型文件类型、工作流版本、哈希等只进入 Extensible Storage/HBR_FileContext，不冒充标准 IFC 属性。
- 规划控制目标：目标值保存在 HBR 上下文；模型计算实际值才能写入对应实际指标属性，禁止把“≤2.0”写成实际容积率。

### 4.2 坐标语义

标准坐标定义固定为：

```text
X = 南北坐标 = North/South
Y = 东西坐标 = East/West
```

读取、写入、共享参数投影、回读和测试必须使用同一语义。输入单位为米。

### 4.3 提交流程

```text
Validate Stage01
→ 写 Revit 项目配置
→ 写 HBR 初始化存储
→ 根据字段注册表解析 IfcProject 标准字段
→ 安装/校正候选共享参数
→ 写入参数
→ Regenerate
→ 原生配置回读 + GUID 参数回读
→ Commit/Assimilate 或整体 Rollback
```

`Stage01Storage` 只负责存储，不允许隐式安装参数或写业务属性。标准属性投影由初始化应用服务显式调用，并把诊断返回界面。

## 5. 通用属性写入组件

通用组件接收属性与目标对象，但必须按映射逐条解析目标：

- IfcProject/IfcBuilding：未提供 ElementId 时可以使用 ProjectInformation。
- IfcBuildingStorey：必须提供 Level ElementId，并校验类别。
- IfcSpace：必须提供 Room ElementId，并校验类别。
- 其他未确认实体：直接返回阻断诊断。

一个请求包含不同实体属性时，服务按属性分别解析目标，不允许先解析一次目标列表再把全部属性写到全部元素。

## 6. 参数与单位

运行时必须读取映射数据中的：

- IFC 实体；
- PropertySet；
- IFC 属性；
- 参数 GUID 与名称；
- Revit 类别；
- 实例/类型作用域；
- 参数数据类型；
- 证据状态与写入策略。

支持 Text、Integer、YesNo、Length、Area、Volume、Angle 和无量纲 Number。Angle 输入按度，写入 Revit 内部角度单位；Length/Area/Volume 分别按 m/m²/m³ 输入。

## 7. 兼容状态

每个实体和字段必须区分：

- `OFFICIAL_EXTRACTED`：官方文件中明确存在；
- `IMPLEMENTATION_DECISION_UNVERIFIED`：我们的承载、命名或 GUID 决策；
- `REVIT_WRITE_VERIFIED`：Revit 写入和回读通过；
- `OFFICIAL_EXPORT_VERIFIED`：官方插件导出后 IFC 值正确；
- `CHECKER_VERIFIED`：官方检查软件正确识别。

GHA 不得把 `REVIT_WRITE_VERIFIED` 显示成“官方兼容通过”。

## 8. 最终验收闭环

每个正式支持字段必须具备：

```text
测试输入值
= GH 规范化值
= Revit 回读值
= 官方插件导出的 IFC 值
= 检查软件识别值
```

验收资产：

- **Golden RVT**：包含明确测试值和对象身份；
- **Golden IFC**：仅由官方插件从 Golden RVT 导出；
- 导出清单：记录官方插件版本、规则包版本、GHA 版本、字段与对象映射；
- 检查报告：逐字段记录通过、缺失、类型错误、单位错误和对象错误。

任何参数名、GUID、对象宿主、单位或官方插件版本变化，都必须重新跑闭环。

## 9. 当前完成定义

当前分支只有在以下条件满足后才能进入稳定版：

1. Stage 01 所有非空 IfcProject 字段都有映射或明确例外；
2. X/Y 坐标语义修复并有回归测试；
3. 非项目属性不会默认写入 ProjectInformation；
4. 未确认实体被明确阻断，不再伪装为兼容；
5. 当前 GHA 编译、契约测试和核心测试通过；
6. 至少建立一个 Golden RVT；
7. 使用官方插件生成 Golden IFC；
8. 官方检查软件对目标字段识别通过。
