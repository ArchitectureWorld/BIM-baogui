# HBR 统一规则数据库与三阶段工作流设计

**状态：** 已批准，进入实施  
**批准日期：** 2026-08-02  
**目标运行环境：** Revit 2020 + Rhino 8 + Rhino.Inside.Revit + Grasshopper  
**交付形态：** 单一 `BIMBaoGui.Stage01.gha`

## 1. 本设计替代的旧路线

本设计是后续开发的唯一产品与架构依据，明确替代以下旧路线：

- 依赖官方 H-IFC 插件的未公开 Revit 参数映射；
- Stage03 手工输入 ElementId、属性和值后直接写入；
- Stage04 只能修改 IFC 中已存在的属性；
- “只允许官方插件导出、禁止标准 IFC 导出和后处理”的旧契约；
- Stage01、任务计划、Revit 参数、IFC 转译各自维护映射的多数据源结构。

官方插件提取结果继续作为证据保存，但不再决定 HBR 的 Revit 数据模型，也不再作为唯一导出路径。

## 2. 产品目标与边界

最终用户工作流固定为三阶段：

```text
01 项目初始化
  -> 02 构件匹配、属性预览与 Revit 可见参数写入
  -> 03 全模型检测、门禁、Autodesk IFC4 导出与 H-IFC/MVD 转译
```

### 2.1 Stage01 保持不变的行为

Stage01 继续负责并保留当前已经工作的内容：

- 项目、子项和 RVT 文件身份；
- 模型文件类型；
- 项目坐标、标高和真北方向；
- 项目条件选择；
- 规划控制目标；
- 文件上下文、文档指纹和初始化状态。

坐标语义必须始终为：

```text
X = NorthSouth = 南北坐标
Y = EastWest  = 东西坐标
```

规划控制目标和模型实际值继续分离，不能把容积率、建筑密度、绿地率阈值写成 IfcSite 的实际值。

Stage01 现有兼容 payload 暂时保留，避免破坏当前初始化效果；从本设计开始，它不再是 Stage02/Stage03 业务属性的唯一真源。Stage02 新增或编辑的业务值必须以 Revit 可见共享参数为真源。

### 2.2 Stage02 目标

Stage02 只作用于当前打开并已通过 Stage01 身份校验的 RVT：

1. 读取当前 Revit 选择，或由用户显式调用 Revit 模态选择；
2. 根据模型文件类型、项目条件、元素类别、族/类型/名称和可选角色提示匹配载体；
3. 生成只读预览，不立即写入；
4. 预览显示将安装或复用的固定 GUID 参数、旧值、建议值、值来源和所有阻断；
5. 用户确认同一份未失效的预览后，才在一个原子事务中安装参数、写入建议值并回读；
6. 参数直接出现在 Revit 的项目信息、实例属性或类型属性中，可查看、编辑和保存；
7. 不要求修改 RFA，也不要求先建设标准化族库。

没有可靠建议值的字段仍安装为可见参数并保持空白，等待建模人员直接在 Revit 中填写。系统不得把示例值当默认业务值。

### 2.3 Stage03 目标

Stage03 合并原“检测”和“导出/转译”职责：

1. 按当前文件上下文和条件扫描全模型载体；
2. 检测必建载体、角色、名称/类别匹配、必填参数、值类型和单位；
3. 生成字段级检测结果；
4. 通过独立业务门禁决定是否允许导出；
5. 使用 Autodesk Revit 2020 标准 `Document.Export(..., IFCExportOptions)` 导出 IFC4；
6. 从 Revit 可见 HBR 参数快照向副本 IFC 创建或更新 Pset/属性；
7. 输出原始 IFC、H-IFC/MVD IFC 和字段级 JSON 报告。

## 3. 已核实的数据基线

### 3.1 MVD 工作簿

`《MVD》规划报建.xlsx` 的有效范围为 `Sheet1!A1:I357`：

- 356 条字段；
- 50 个 PropertySet；
- 12 类 IFC 实体；
- 以 `IFC 实体 + PropertySet + 属性名` 规范化后仍为 356 个唯一字段；
- 工作簿没有“必填、条件必填、建议、可选”列；
- 个别空白样式单元格被旧提取过程误写成字符串 `14`；
- 有一条 `IFcText` 大小写错误，规范化时按 `IfcText` 处理，但原始证据值必须保留。

因此，MVD 工作簿定义字段合同，不单独定义门禁必填性。

### 3.2 官方插件证据

现有官方提取包有 166 条规则：

- 163 条可在 356 条 MVD 字段中找到；
- 3 条仅存在于官方插件提取结果，作为扩展证据保存：
  - `IfcDoor / 门信息属性集 / 开启方向`；
  - `IfcDuctSegment / 风管段信息属性集 / 隔热层厚度`；
  - `IfcSpace / 建筑空间信息属性集 / 空间形成方式`；
- MVD 另有 193 条不在官方 166 条中；
- 官方只明确公开了 4 条 Revit 对象映射；
- 166 条当前全部为 `UNCLASSIFIED`，不能据此宣称必填。

### 3.3 Stage01 注册表

现有 Stage01 注册表有 102 条：

- IfcProject：77；
- IfcOrganization：25；
- 全部来自 MVD 工作簿；
- 其中 89 条也位于官方 166 条内；
- 另外 13 条为 MVD-only：项目控制指标 10 条、`Pset_Manifest` 3 条。

### 3.4 数据结论

必须同时保留三种不同含义，禁止再混为一个数字：

| 数据层 | 数量 | 含义 |
|---|---:|---|
| MVD 字段合同 | 356 | 要支持和追踪的标准字段全集 |
| 官方插件证据子集 | 166 | 可用于交叉核对的插件提取证据 |
| Stage01 初始化子集 | 102 | 项目初始化阶段当前处理的字段 |

官方示例 ZIP 中的两份 IFC 均由 Autodesk Revit 2025 IFC 导出器生成；厂房样例 35 个属性中只有 27 个命中 MVD、10 个命中官方 166，总平样例 14 个属性中只有 2 个命中两套合同。样例还使用 `IfcLengthMeasure`、`IfcAreaMeasure` 等运行时 typed value，而工作簿常声明 `IfcReal`。因此样例只用于验证实体 owner、名称别名和允许的运行时 IFC 类型，不能作为字段全集、GUID 或必填性的权威源。

## 4. 统一规则数据库

### 4.1 单一可编辑源

Git 中只保留一个可编辑业务规则源：

```text
specs/hbr-rules/v1/source/hbr_rule_source.v1.json
```

它同时包含：

- 356 条 MVD 标准字段；
- 3 条官方插件扩展字段；
- 官方 166 条证据标记；
- 固定 Revit 参数 GUID 与旧参数别名；
- Revit 载体角色；
- 三种模型文件类型的激活配置；
- 项目条件；
- 必建载体和字段必填性；
- 建议值来源；
- IFC 所有者解析和转译策略；
- Stage01/02/03 阶段归属。

旧的 `stage01_file_initialization_registry_v0.1.json`、`wuhan_planning_rules.v1.json`、bindings、任务硬编码和兼容状态文件只作为迁移输入与历史证据，不再被多个运行时目录交叉 join。

### 4.2 规则源顶层结构

```json
{
  "schemaVersion": "1.0.0",
  "packageId": "HBR-WUHAN-PLANNING",
  "packageVersion": "1.0.0",
  "guidNamespace": "<uuid>",
  "evidenceSources": [],
  "properties": [],
  "carrierRoles": [],
  "modelProfiles": [],
  "conditions": [],
  "tasks": [],
  "legacyAliases": []
}
```

### 4.3 属性规则合同

每条属性至少包含：

```text
propertyId
canonicalKey
contractKind = MVD | HIFC_EXTENSION
mvd.sourceRow / entity / pset / property / ifcDataType / unit
officialPlugin.evidenceStatus / originalIdentity
revit.parameterGuid / parameterName / legacyNames
revit.bindingScope / storageType / parameterType
revit.visible = true
revit.userModifiable = true
carrierRoleIds[]
stageOwnership[]
requirement.level
requirement.conditionId
suggestion.kind / aliases
ifc.ownerStrategy / writeStrategy
```

可见参数命名固定为：

```text
HBR｜属性集名称｜属性名
```

已经发布并可能写入 RVT 的 166 个 canonical GUID 必须冻结，不能因重命名而改变。既有 `HIFC.属性集.属性名` 作为 legacy name 按同一 GUID 回读；当前模型已有旧名时不强制删除或制造重复参数。新增 MVD 字段使用 UUIDv5 生成后写入源文件并冻结。运行时不得临时生成业务参数 GUID。

外部官方 GUID、官方参数别名 GUID和 HBR canonical GUID 必须使用不同字段表达；任何派生 GUID 都不能冒充官方证据。

### 4.4 载体角色

载体角色解决“同一 Revit 类别可能表达不同业务对象”的问题。角色至少包含：

```text
roleId
displayName
modelFileTypes[]
ifcEntity
revitCategories[]
allowedElementKinds
nameAliases[]
familyAliases[]
typeAliases[]
cardinality.min / max
selectionPolicy
ifcOwnerStrategy
```

首版承载原则：

| HBR/IFC 角色 | Revit 可见载体 |
|---|---|
| Project、Building、单一 Site | ProjectInformation |
| Storey | Level |
| Space | Room |
| SpatialZone | Area，必要时由转译创建 IfcSpatialZone |
| Wall、Slab、Roof、Window、StairFlight | 对应 Revit 实例/类型 |
| Organization | 用户选择的可导出 Generic Model 载体；多组织不得压平到同一 ProjectInformation |
| 其他规划对象 | 用户选择的 Area、Floor 或 Generic Model，并通过角色元数据区分 |

角色、规则来源、预览哈希和写入审计可以进入隐藏元数据；业务值不能只存在于隐藏存储。

### 4.5 必填性与条件

必填性不能从 Excel 空白、示例值或官方插件配置推断。只有规则源中经过开发确认的实施配置可以使用：

```text
REQUIRED
CONDITIONAL
OPTIONAL
NOT_APPLICABLE
UNCLASSIFIED
```

`UNCLASSIFIED` 必须在报告中显式出现。严格模式下，当前激活配置中尚未分类或尚未制定载体的规则属于阻断，不能显示“全部通过”。测试强制模式可以继续导出，但报告必须保留这些缺口。

### 4.6 构建产物

构建工具执行：

```text
hbr_rule_source.v1.json
  -> JSON Schema 校验
  -> GUID/identity/引用/必填性/覆盖率校验
  -> 确定性排序和 canonical JSON
  -> SHA-256
  -> hbr_rules.v1.hbrpack
  -> hbr_shared_parameters.v1.txt
  -> hbr_rule_manifest.v1.json
```

`.hbrpack` 使用固定头 `HBRP + formatVersion + payloadLength + payloadSha256 + canonical UTF-8 JSON payload`，构建到 `obj` 后作为只读 EmbeddedResource 编入 GHA，不作为第二份可编辑源提交。运行时只加载这一份规则包，并先验证 payload 长度和 SHA-256，再反序列化。不使用 SQLite，原因是 Revit 2020/net48 的 SQLite native DLL 会破坏“单一 GHA、无额外运行时 DLL”的交付要求。

运行时所有目录都来自同一个只读单例：

```text
HBRRuleDatabase
  ├─ PropertiesById
  ├─ PropertiesByIfcIdentity
  ├─ PropertiesByParameterGuid
  ├─ CarrierRolesById
  ├─ ProfilesByModelFileType
  ├─ TasksById
  └─ RulePackHash
```

Stage01 的 `HBR_FileContext`、Stage02 预览/写入结果、Stage03 检测/导出报告都必须携带同一个 `packageId + packageVersion + rulePackHash`。规则哈希变化会使旧预览和旧检测结果立即失效。

## 5. Stage02 详细设计

### 5.1 输入与动作

规范 Stage02 组件接收：

- Stage01 文件上下文；
- 可选 ElementId 列表；为空时读取当前 Revit 选择；
- 可选角色提示；
- “生成预览”边沿；
- “确认写入”边沿。

Revit 模态选择只能由明确 UI 动作通过 Revit host context 调用，不能在 `SolveInstance()` 或 dynamic update 中直接 `PickObjects`。

### 5.2 预览身份

预览绑定以下全部状态：

```text
FileGuid
RevitDocumentFingerprint
Stage01 FileContextHash
RulePackHash
Element.UniqueId 集合
元素旧值快照
角色选择
previewNonce
previewHash
```

确认写入前必须重新核对全部状态。切换 RVT、改选元素、改值、规则更新、Stage01 重跑或预览被消费后，均不得继续确认。

整数 `ElementId` 只能作为同一活动文档内的 UI 辅助信息，不能作为排队请求的唯一身份。实际请求必须保存 `Element.UniqueId + DocumentFingerprint`。

### 5.3 匹配优先级

```text
显式角色提示
  -> 已保存的角色元数据
  -> Revit 类别
  -> 族名/类型名/元素名称别名
  -> 唯一候选自动匹配
  -> 多候选时阻断并要求用户选择角色
```

名称不匹配、类别不匹配和角色歧义必须分别报告，不能都显示为“缺少构件”。

### 5.4 建议值优先级

```text
同 GUID 现有 HBR 参数值
  -> legacy HIFC 参数值
  -> 规则声明的 Revit 参数别名
  -> 规则声明的确定性模型计算
  -> Stage01 项目信息投影
  -> 空白
```

建议值必须携带来源和置信状态；官方示例值永远不能自动写入。

### 5.5 预览字段

每个写入项至少显示：

- 文档标题和指纹；
- ElementId、UniqueId、元素名称和类别；
- 载体角色；
- 实例或类型作用域；
- propertyId、固定参数 GUID、可见参数名；
- 旧值、建议值、值来源；
- 参数将新装、复用、合并类别或仅写值；
- 必填性、适用条件和阻断状态。

参数解绑、旧绑定删除或其他迁移动作不能静默发生；如确有需要，必须单列为迁移动作并经确认。

### 5.6 写入事务

```text
重新验证预览身份
  -> TransactionGroup
  -> 安装或校正固定 GUID 参数定义与绑定
  -> 保留并合并既有绑定类别
  -> 写入非空建议值
  -> Regenerate
  -> 按 GUID 回读
  -> 写入最小审计元数据
  -> Assimilate
```

任何一步失败必须整体回滚。共享参数临时文件只能位于系统临时目录，并在 `finally` 中恢复 `SharedParametersFilename` 和删除临时文件。

## 6. Stage03 详细设计

### 6.1 扫描与字段状态

Stage03 以规则数据库为驱动扫描整个当前 RVT，字段状态至少包括：

```text
PASS
NOT_APPLICABLE
MISSING_CARRIER
CARRIER_CATEGORY_MISMATCH
CARRIER_NAME_MISMATCH
AMBIGUOUS_CARRIER
MISSING_PARAMETER
EMPTY_REQUIRED_VALUE
INVALID_VALUE
RULE_NOT_IMPLEMENTED
UNCLASSIFIED_REQUIREMENT
IFC_OWNER_NOT_FOUND
IFC_VALUE_MISMATCH
```

报告必须同时区分载体级、参数级、Revit 值级、原始 IFC 级和转译 IFC 级状态。

### 6.2 业务门禁

门禁模式独立于防误触发的 `false -> true` 执行边沿：

- `Strict`：任何激活的阻断项存在时不导出；
- `Force`：允许带业务缺陷导出，必须提供非空放行原因，并在报告中记录 `forced=true`、放行人输入和全部阻断项。

公开 Stage03 Grasshopper 组件以布尔输入 `全部通过才导出` 暴露该模式，默认值必须为 `true`：

- `true` = `Strict`，卡片显示“严格门禁｜全部通过后导出”；
- `false` = `Force`，卡片显示“测试放行｜缺陷仍写入报告”，且必须连接非空 `强制原因`；
- 该模式输入不能代替独立的 `执行` 输入；只有 `执行` 的 `false -> true` 边沿才能开始一次运行；
- 模式或强制原因改变后，既有检测/导出结果立即标为过期，用户必须重新触发执行。

Force 不能绕过技术致命错误：

- Revit 版本不符；
- 活动 RVT 身份不符；
- 文档不可用；
- 输出路径已存在；
- IFC 导出失败或空文件；
- IFC4 解析失败；
- 最终报告写入失败。

### 6.3 输出命名与不可覆盖

同一次运行使用同一个 `runId`：

```text
<rvt-stem>-<runId>-RAW.ifc
<rvt-stem>-<runId>-HIFC-MVD.ifc
<rvt-stem>-<runId>-fields.json
```

任一正式目标已存在就拒绝运行，不覆盖原 RVT、原始 IFC、转译 IFC 或已有报告，不创建 `.bak`、`.backup` 或插件备份。

业务严格门禁失败时仍生成字段报告，但不生成两个 IFC。转译失败时保留已经成功导出的 RAW IFC，作为诊断证据。

异常报告统一写到活动 GHA 同目录：

```text
BIMBaoGui.Stage02.failure-*.json
BIMBaoGui.Stage03.failure-*.json
```

异常报告使用唯一时间戳和原子移动；不得在插件目录生成 GHA 副本。

### 6.4 Autodesk IFC4 导出

Revit API 线程中执行：

1. 再次验证文档身份；
2. 生成纯 DTO 的 Revit 字段快照和元素 IFC GlobalId 映射；
3. 确认 RAW 路径不存在；
4. 打开独立导出 Transaction；
5. 显式设置 `IFCExportOptions.FileVersion = IFCVersion.IFC4`；
6. 调用 `Document.Export`；
7. 检查返回值、文件存在且长度大于零；
8. 验证导出事务采用回滚还是提交的副作用，并将实际策略写入报告。

扫描和 `Document.Export` 禁止放入 `Task.Run`。只有脱离 Revit API 的 IFC STEP 转译与 JSON 序列化可以在后台线程运行。

### 6.5 IFC 转译

转译以 RAW IFC 副本为输入，保留几何和非目标数据：

1. 解析 IFC4 STEP；
2. 用 Revit 快照中的 IFC GlobalId 或唯一空间实体解析 owner；
3. 规范化已有 Pset/属性名和值类型；
4. 对缺失属性创建 `IfcPropertySingleValue`；
5. 对缺失 Pset 创建 `IfcPropertySet`；
6. 创建或复用 `IfcRelDefinesByProperties`；
7. 回读并逐字段验证实体、Pset、属性名、类型和值；
8. 原子输出 H-IFC/MVD IFC。

转译器不能再以“找到至少一个可规范化字段”为成功。每个激活字段都必须得到明确状态。

对于无法合法附着属性集或尚无已验证 owner 策略的实体，必须报告 `RULE_NOT_IMPLEMENTED` 或 `IFC_OWNER_NOT_FOUND`，不得把属性偷偷挂到 IfcProject。Organization 的 `IfcActor -> IfcOrganization` 包装策略、SpatialZone 创建和关系必须有独立 IFC4 fixture 与检查器验收后才能从“实施中”升级为“通过”。

## 7. 字段级报告

字段报告是一次运行的最终清单，至少包含：

```text
schemaVersion
runId
startedUtc / completedUtc
pluginVersion
revitVersion
document title/path/fingerprint/fileGuid
fileContextHash
rulePackage id/version/hash
gate mode/forced/reason/decision
output raw/final/report paths and SHA-256
summary counts by status/entity/pset/requirement
carriers[]
fields[]
diagnostics[]
```

每个 `fields[]` 项至少包含：

```text
propertyId
contractKind
requirement/applicability
carrier role/ElementId/UniqueId
parameter GUID/name/scope
revit raw/normalized value and source
revit validation status
raw IFC owner/pset/property/type/value/status
final IFC owner/pset/property/type/value/status
messages[]
```

数组按稳定键排序，便于 Git diff、自动测试和用户把报告直接交给开发人员分析。

## 8. Grasshopper 产品形态

最终公开组件只显示三项：

```text
湖北BIM报规｜01 项目初始化
湖北BIM报规｜02 构件与属性准备
湖北BIM报规｜03 检测、导出与 H-IFC 转译
```

旧 Stage03 手工双写组件和独立 Stage04 组件保留为隐藏 legacy wrapper，防止既有 GH 文件立即损坏；它们不再出现在新组件菜单，也不作为正式验收路径。

新组件必须显示真实端口。自定义卡片不能遮挡输入/输出端口，并且任何按钮都有清晰状态：等待、预览、阻断、确认中、导出中、成功、失败。

卡片只承担可扫读摘要，359 条字段的完整信息必须通过输出端口提供给 Grasshopper Panel / Data Tree，不能在组件卡片中截断后作为唯一结果：

- Stage02 卡片依次显示 RVT/规则身份、选择与匹配数量、预览状态、待安装/待写入/阻断数量、首条阻断；输出强类型预览、按元素和角色稳定分支的字段明细、全部阻断、写入状态与规则哈希；
- Stage03 卡片依次显示 `Strict` 或 `Force`、检测状态、字段通过/阻断数量、运行状态以及三件套路径；输出允许导出、字段通过、全部阻断、RAW IFC、HIFC-MVD IFC、fields JSON、规则哈希与状态；
- 正常、等待、测试放行、业务阻断和技术失败使用可区分的中性/绿色/橙色/红色状态，但颜色不能作为唯一信息，必须同时显示文字和计数。

## 9. 兼容与迁移

- 单一 GHA 文件名保持不变；
- Stage01 现有初始化行为和坐标效果保持；
- 旧 canonical GUID 不变；
- 旧参数名作为 legacy alias 回读，不自动删除；
- 旧 DataStorage payload 保留兼容读取，但 Stage02 新业务值以 Revit 参数为准；
- 旧官方插件证据继续保留 `evidenceStatus`，不再作为强制运行路径；
- 旧文档和测试中“禁止标准 IFC/后处理”的断言必须被新设计替换。

## 10. 自动化验收

### 10.1 规则数据库

- MVD 标准字段准确为 356，identity 唯一；
- 官方扩展字段准确为 3；
- 官方证据覆盖为 166，其中 163 MVD + 3 extension；
- Stage01 初始化子集准确为 102；
- 已发布 GUID 重算和冻结检查通过；
- 参数 GUID、propertyId、角色、任务和 profile 引用无重复或悬空；
- 运行时包输出确定，重复构建字节和 SHA-256 相同；
- GHA 中只嵌入并加载一个 runtime rule pack。

### 10.2 Stage02

- 无预览不能确认；
- 文档、选择、旧值、上下文或规则哈希变化使预览失效；
- 重复确认不能再次消费同一 nonce；
- ProjectInformation、Level、Room、实例和类型参数均可见可编辑；
- 绑定类别采用并集，不破坏既有类别；
- 任何失败整体回滚；
- 隐藏元数据不包含 Stage02 业务属性值。

### 10.3 Stage03

- Strict 正确阻断业务缺陷；
- Force 必须有原因且不能绕过技术错误；
- 输出三件套同 runId 且不覆盖；
- RAW 是 Revit 标准 IFC4；
- final IFC 可创建缺失 Pset/属性并逐字段回读；
- 转译失败保留 RAW；
- 失败 JSON 与 GHA 同目录；
- 无 `.bak/.backup` 和 GHA 备份。

## 11. 实机完成定义

在以下闭环完成前，不得宣称整个目标完成：

1. 自动测试、Release 构建和规则覆盖率全部通过；
2. 活动插件目录只有一个 GHA；
3. 在 `20260731test02.rvt` 中完成 Stage01；
4. Stage02 对项目信息和至少一个实例载体生成预览、确认并在 Revit 属性面板回读；
5. Stage03 生成 RAW IFC、H-IFC/MVD IFC 和 fields JSON；
6. X、Y、高程等当前关键字段在 final IFC 中逐字段匹配；
7. Strict 和 Force 两种门禁各完成一次验证；
8. 关闭并重新打开 RVT 后，HBR 参数仍直接可见和可编辑；
9. 记录仍未实施或尚未取得检查器证据的字段，不将部分覆盖描述为全部通过。
