# 官方插件兼容写入深度 Review

## 1. Review 目标

本轮 Review 不再以“GHA 能创建参数”或“Revit 内能回读”为完成标准，唯一目标是：

```text
指南与检查规则要求
→ GH 得到正确业务值
→ 写入官方插件能够读取的 Revit 数据位置
→ 官方插件导出 IFC
→ 检查软件正确识别
```

因此，本轮同时检查：

- 指南与 Stage 01 字段范围；
- 官方 H-IFC 属性、PropertySet、IFC 实体和数据类型；
- 官方显式 Revit→IFC 对象映射；
- 当前 GHA 的字段覆盖、目标对象、参数定义、单位转换和错误状态；
- 旧架构中的自研导出、IFC 后处理与当前“官方插件导出”目标是否冲突；
- 测试是否真正覆盖官方软件闭环。

## 2. 证据分层

### 2.1 已确认的官方证据

当前规则包确认：

- 166 条规划报建属性；
- 16 个 PropertySet；
- 9 类 IFC 实体；
- 全部 PropertySet 为实例作用域；
- 属性名、PropertySet 名、IFC 数据类型和示例值；
- 4 条官方显式 Revit→IFC 对象映射：
  - ProjectInformation → IfcProject；
  - ProjectInformation → IfcBuilding；
  - Level → IfcBuildingStorey；
  - Room → IfcSpace。

### 2.2 尚未成为官方协议证据的内容

以下内容来自我们的实现基线，不能直接称为官方兼容协议：

- `HIFC.<PropertySet>.<Property>` 参数名；
- UUIDv5 参数 GUID；
- IfcSite、IfcOrganization、IfcSpatialZone 等对象的 Revit 承载决策；
- Door、Duct 的自定义属性读取路径；
- `POST_EXPORT_ENRICH` 和 `POST_EXPORT_CREATE_OR_ENRICH`；
- 仅根据 Revit 参数回读推断官方插件一定能够识别。

这些实现可以作为实验候选，但只有经过官方插件导出和检查软件识别后才能升级为验证结论。

## 3. 发现的主要冲突

| 编号 | 冲突 | 原实现 | 风险 | 修复 |
|---|---|---|---|---|
| C01 | 产品导出路径冲突 | 原基线依赖原生 IFC + 后处理 | 与“只使用官方插件导出”相反 | 当前路径固定为 `OFFICIAL_HIFC_PLUGIN_ONLY`，禁止产品代码依赖后处理 |
| C02 | 官方证据与实现假设混合 | 类名和状态把自生成参数称为“官方兼容” | 容易产生虚假完成结论 | 新增兼容证据状态目录，区分官方提取、实现决策、Revit 回读、官方导出和检查通过 |
| C03 | Stage 01 覆盖不足 | 102 个标准初始化字段中仅硬编码投影 10 个 | 大量表单值只存在内部 JSON，官方插件无法读取 | 改为根据 Stage 01 字段键和规则包动态解析全部非空 IfcProject 字段 |
| C04 | 组织数据丢失 | `organizations` 数组没有进入参数写入 | 多个 IfcOrganization 无法被官方导出 | 保留组织数据，但在官方组织写入协议确认前明确阻断，不再写到 ProjectInformation 冒充组织实体 |
| C05 | 坐标轴语义错误 | X→EastWest，Y→NorthSouth | 官方输出坐标互换 | 固定 X=NorthSouth、Y=EastWest，读取、写入、回读和测试全部同步修复 |
| C06 | 存储层副作用 | `Stage01Storage.Write` 隐式安装并写参数 | 存储职责不纯，诊断丢失，事务边界难审计 | 存储只保存载荷；Stage01 应用服务显式调用标准投影并汇总消息 |
| C07 | 通用写入错误宿主 | ElementId 为空时所有属性默认写 ProjectInformation | 楼层、房间和其他实体属性可能写到错误对象 | 按每条映射分别解析对象；仅 IfcProject/IfcBuilding 可默认 ProjectInformation |
| C08 | 未确认对象被当作可写 | Site/Organization/SpatialZone/Door/Duct 使用实现承载 | 可能写入后无法被官方插件识别 | 当前正式路径直接阻断并返回 `BLOCK_PENDING_OFFICIAL_PLUGIN_CONTRACT` |
| C09 | 状态文案过度承诺 | “官方 H-IFC 属性写入通过” | Revit 回读被误解为官方导出已通过 | 状态改为“Revit 写入与回读通过｜待官方导出验收” |
| C10 | 测试门槛不足 | 只检查源码包含事务、回读等关键词 | 不能验证数据被官方软件识别 | 增加 Golden RVT → 官方插件导出 → Golden IFC → 检查软件报告的最终门槛 |

## 4. Stage 01 字段审计

### 4.1 正确处理范围

Stage 01 包含三类不同数据：

1. **IfcProject 标准字段**：项目、报建联系人、地籍、登记、区划、申报等项目级数据；
2. **IfcOrganization 标准字段**：每个参建单位独立记录，不能压平到单一项目参数；
3. **HBR 工作流字段**：文件 GUID、子项身份、模型文件类型、版本、哈希、激活规则等，只用于流程控制。

修复后的规则：

- 所有非空 `IfcProject|Pset_*|属性` 通过规则目录解析为候选参数；
- 所有 HBR 字段只进入 HBR_FileContext / Extensible Storage；
- 规划目标值不写入模型实际指标字段；
- 组织数组继续完整保存，但没有官方对象协议前不得伪写为 IfcProject；
- 找不到规则映射的标准字段必须失败，或进入明确的登记例外，禁止静默跳过。

### 4.2 坐标语义

固定定义：

```text
基点坐标 X = 南北坐标 = NorthSouth
基点坐标 Y = 东西坐标 = EastWest
```

建议 Golden RVT 使用明显不相等的哨兵值，例如：

```text
X = 3123456.789 m
Y = 456789.123 m
高程 = 37.654 m
```

这样可以直接识别 X/Y 交换、单位未转换和小数精度问题。

### 4.3 规划目标与实际值

以下内容不得混写：

```text
规划目标：容积率 ≤ 2.00
模型实际：容积率 1.83
```

Stage 01 保存前者；后续模型计算组件写后者。检查结果由目标和实际比较得出。不得为了填满官方参数而把目标上限写成模型实际值。

## 5. 对象与宿主审计

### 5.1 当前允许进入候选写入的实体

- IfcProject → ProjectInformation；
- IfcBuilding → ProjectInformation；
- IfcBuildingStorey → 明确 Level；
- IfcSpace → 明确 Room。

这四类有官方显式对象映射证据，但参数名称/GUID仍需官方导出实测。

### 5.2 当前阻断的实体

- IfcOrganization；
- IfcSite；
- IfcSpatialZone；
- IfcDoor；
- IfcDuctSegment。

阻断不表示标准不需要这些对象，而是表示当前提取证据不足以证明官方插件从我们选择的位置读取自定义属性。继续写入并显示“通过”会制造错误模型，因此必须先补官方插件黑箱实验或逆向证据。

## 6. 参数、数据类型和单位

写入层必须按实际 Revit 参数类型处理：

- Text / Label / Date / DateTime → String；
- Integer → Integer；
- Boolean → YesNo 0/1；
- Length → m 转 Revit 内部长度；
- Area → m² 转内部面积；
- Volume → m³ 转内部体积；
- Angle → ° 转内部角度；
- Number / Real 无单位 → 数值原值。

回读只验证写入一致性，不替代官方插件导出验证。

## 7. 测试 Review

### 7.1 已补的自动化测试

新增失败测试后确认了以下问题确实存在：

- X/Y 坐标反向；
- Stage01Storage 存在参数写入副作用；
- Stage 01 只有 10 字段硬编码；
- Organization 未处理；
- 非项目属性错误默认到 ProjectInformation；
- 缺少兼容证据状态；
- 开发计划未强制官方软件闭环。

这些测试将作为后续回归门槛。

### 7.2 自动化测试不能替代的环节

GitHub Actions 可以验证：

- 规则与注册表结构；
- 字段映射覆盖；
- 代码目标选择；
- 单位转换分支；
- GHA 编译；
- 核心单元测试。

但它不能证明官方插件真实读取结果。最终仍必须在 Revit 2020 环境完成：

```text
Golden RVT
→ 官方插件导出
→ Golden IFC
→ 检查软件
```

## 8. Golden 样本要求

### 8.1 Golden RVT

至少包含：

- 不相等的 X/Y 坐标和非整数高程；
- Project 与 Building 的不同哨兵字符串；
- 两个 Level，分别带不同楼层属性；
- 两个 Room，分别带不同空间属性；
- 文本、整数、布尔、长度、面积、角度和无量纲数值；
- 一个明确的组织记录，用于确认官方插件组织协议是否缺失或可用。

### 8.2 Golden IFC

必须由官方 H-IFC 插件从 Golden RVT 直接生成，不允许经过任何后处理。记录：

- 官方插件版本；
- 导出配置；
- GHA 版本；
- 规则包版本；
- RVT 文件哈希；
- IFC 文件哈希。

### 8.3 检查软件报告

逐字段记录：

- IFC 实体是否正确；
- PropertySet 名是否正确；
- 属性名是否正确；
- 数据类型是否正确；
- 单位是否正确；
- 数值是否正确；
- 是否被检查软件识别；
- 失败属于写入、导出、对象映射还是规则判断问题。

## 9. 当前结论

当前分支经过本轮修复后，可以达到：

- 按规则包生成 Revit 候选写入；
- 防止明显错误宿主和坐标语义；
- 区分 Revit 写入完成与官方兼容完成；
- 为官方插件实机验证准备可审计的写入链。

但在 Golden RVT、官方插件导出的 Golden IFC 和检查软件报告完成之前，以下结论仍然不能声称：

- 自生成 GUID 一定是官方插件的读取键；
- `HIFC.<Pset>.<Property>` 一定是官方插件唯一识别名称；
- Organization/Site/SpatialZone/Door/Duct 的当前承载方式可被官方插件识别；
- 166 条属性已经全部实现正式闭环。

后续开发的最高优先级不是继续增加界面，而是逐实体完成官方软件闭环，并把验证结果回写到 `official_plugin_compatibility_status.v1.json`。
