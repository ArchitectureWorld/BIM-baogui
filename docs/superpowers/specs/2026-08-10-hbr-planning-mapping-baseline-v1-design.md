# HBR 报规映射规则基线 v1 与 GH 后续开发设计

**状态：** 已批准，待书面复核

**批准日期：** 2026-08-10

**目标规则包：** `HBR-WUHAN-PLANNING / 1.0.0`

**目标运行环境：** Revit 2020 + Rhino 8 + Rhino.Inside.Revit + Grasshopper 8

**插件交付形态：** 单一 `BIMBaoGui.Stage01.gha`

## 1. 目标

先冻结一份可追溯、可重建、可由 GH 插件唯一消费的报规映射规则基线，再以该基线继续完善 Stage01、Stage02、Stage03。

本设计明确区分：

- **规则稳定：** 属性身份、数据类型、单位、Owner 策略、阶段归属和当前支持状态具有确定版本与哈希；
- **运行时完整：** GH 插件已经实现全部 Owner 策略、业务门禁和真实软件闭环。

规则基线可以先于运行时完整性冻结。未实现能力必须显式标记并 fail-closed，不得被隐藏、猜测或自动降级。

## 2. 冻结范围

### 2.1 完整数据合同

基线冻结完整 359 条映射，而不是另建只含官方 166 条的运行时规则集：

- 356 条 MVD 字段；
- 3 条 H-IFC 扩展字段；
- 166 条官方提取证据作为 359 条中的证据子集；
- 52 个 PropertySet；
- 14 类 IFC Owner；
- 固定 propertyId、canonicalKey、Revit 参数 GUID、数据类型、单位、载体角色和阶段归属。

官方 166 条继续承担外部证据和兼容性对账作用，不再成为第二份可编辑业务规则源。

### 2.2 当前未完成能力

冻结基线不宣称以下能力已经实现：

- `CANONICAL_SPATIAL_ZONE_RECORD`：32 条；
- `USER_SELECTED_EXPORTABLE_GENERIC_MODEL`：25 条；
- 359 条字段的必填、条件必填、建议和可选分类；
- 完整 Strict/Force 业务门禁；
- RAW/Candidate/Final 三层审计证据；
- Stage03 扫描与导出的同一 Revit 模型快照；
- 完整 Revit → 三件套 → IFCFlux 生产验收。

这些状态必须保存在规则或兼容状态中，并由 GH 明确展示为未分类或未实现。

## 3. 唯一规则链

### 3.1 唯一可编辑业务源

Git 中只允许编辑：

```text
specs/hbr-rules/v1/source/hbr_rule_source.v1.json
```

它是 359 条映射、官方证据标记、Revit 投影、Owner 策略、模型配置和阶段归属的唯一业务真源。

以下文件不是第二份业务真源：

```text
specs/hbr-rules/v1/schemas/hbr_rule_source.schema.json
specs/hbr-rules/v1/compatibility/hbr_rule_compatibility_baseline.v1.json
specs/hbr-rules/v1/manifest.sha256.json
```

- Schema 只定义结构合同；
- compatibility baseline 只阻止无解释的兼容性漂移；
- manifest 只记录路径、大小和 SHA-256。

### 3.2 历史映射目录

```text
specs/hifc-mapping/v1
```

只保留为官方提取证据、迁移输入和历史审计资料。生产 GH 不得直接读取其中的 `wuhan_planning_rules.v1.json`、bindings、shared parameters 或 compatibility status。

如仍需保留派生文件，它们必须由唯一规则源生成并通过 manifest 校验，禁止人工修改。

## 4. 坐标 identity 冻结

### 4.1 原始证据与最终输出分离

Excel/MVD 原始证据保持：

```text
source.rawProperty = 基点坐标 X
source.rawProperty = 基点坐标 Y
```

最终 IFC identity 固定为：

```text
IfcProject|Pset_申报信息属性集|基点坐标X
IfcProject|Pset_申报信息属性集|基点坐标Y
```

所有 canonicalKey、`ifc.property`、stage fieldKey、空间映射、fixture、validator 和运行时 enrichment 必须使用无空格输出名称。

带空格名称只允许作为原始证据或输入迁移 alias，最终 IFC 禁止双写。

### 4.2 坐标语义与数值合同

```text
X = Northing = North/South = 南北坐标
Y = Easting  = East/West   = 东西坐标
```

v1 单位合同固定为：

```text
IFC declared type    = IfcReal
allowed runtime type = IfcReal | IfcLengthMeasure
canonical unit       = m
Revit storage        = Double
Revit parameter type = Length
```

单位参数在 IFC STEP 中是否显式写出，必须由 fixture 和真实软件验收分别记录，不得从项目全局单位或 `$` 自动推导为兼容通过。

## 5. 文件、名称与发布位置

### 5.1 仓库文件

```text
specs/hbr-rules/v1/source/hbr_rule_source.v1.json
specs/hbr-rules/v1/schemas/hbr_rule_source.schema.json
specs/hbr-rules/v1/compatibility/hbr_rule_compatibility_baseline.v1.json
specs/hbr-rules/v1/manifest.sha256.json
tools/build_hbr_rulepack.py
tools/hifc/generate_hifc_mapping_smoke.py
tools/hifc/validate_hifc_mapping_smoke.py
tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.ifc
tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.manifest.json
```

fixture 延续当前名称，内容更新为经 IFCFlux 验证的无空格 X/Y 版本。旧带空格版本不再作为提交的正向 fixture；带空格负例由测试在临时目录生成。

### 5.2 构建产物

```text
src/BIMBaoGui.Stage01/obj/Release/net48/HBR_RulePack.hbrpack
src/BIMBaoGui.Stage01/bin/Release/net48/BIMBaoGui.Stage01.gha
```

`.hbrpack` 的嵌入逻辑名固定为：

```text
BIMBaoGui.Stage01.Resources.HBR_RulePack.hbrpack
```

`obj`、`bin` 中的构建产物不提交到 Git。

### 5.3 发布归档

```text
artifacts/HBR-WUHAN-PLANNING-v1.0.0-baseline.zip
```

ZIP 至少包含：

- `HBR_RulePack.hbrpack`；
- 规则 manifest；
- 全映射 fixture 及其 manifest；
- validator 报告；
- IFCFlux 验收说明与证据哈希。

本地 `artifacts/` 保持 gitignored。CI 将该 ZIP 纳入现有验证 artifact，不创建第二条互相竞争的上传链。

### 5.4 版本标识

```text
packageId      = HBR-WUHAN-PLANNING
packageVersion = 1.0.0
Git tag        = hbr-planning-mapping-v1.0.0
```

GH 插件继续使用自身版本，例如 `0.9.0-rc.N`。规则包版本和 GHA 版本独立演进，但 GHA 必须报告其嵌入的 packageId、packageVersion 和 SHA-256。

## 6. 确定性生成与 manifest

生成链固定为：

```text
hbr_rule_source.v1.json
  → schema + semantic + compatibility validation
  → HBR_RulePack.hbrpack
  → H-IFC full-mapping fixture
  → fixture manifest
  → rules manifest
```

`generate_hifc_mapping_smoke.py` 必须恢复为标准库可运行的确定性生成器，并与生产端共享同一个 EffectiveIfcIdentity 规则，而不是复制 X/Y 特例。

`specs/hbr-rules/v1/manifest.sha256.json` 只记录冻结交付所需文件的：

- 相对路径；
- 字节大小；
- SHA-256；
- packageId 和 packageVersion；
- 生成器版本或生成器文件 SHA-256。

CI 必须在临时目录重生成 pack、fixture 和 manifest，并与提交内容逐字节比较。

## 7. GH 插件依赖边界

### 7.1 唯一运行时入口

GH 业务代码只允许通过：

```text
HbrRuleDatabase.Current
```

读取嵌入的 `HBR_RulePack.hbrpack`。组件不得直接解析旧映射 JSON、CSV、bindings 或 shared parameters，也不得在 `SolveInstance()` 中硬编码映射名称、类型、单位或 Owner 选择。

规则包加载必须 fail-closed：资源缺失、schema 错误、packageId/version/hash 不一致时，Stage01/02/03 均不得继续业务写入或导出。

### 7.2 可以继续完善的模块

映射基线冻结后，可以并行推进：

- Stage01 输入、初始化、FileContext 和规则身份传播；
- Stage02 选择、预览、共享参数安装、实例/类型写入、回读和事务回滚；
- GH 卡片布局、状态、图标、Data Tree、上升沿执行和预览失效；
- 规则包加载、诊断、版本/hash 展示；
- Stage03 runId、路径不覆盖、状态机、失败报告和字段 JSON；
- STEP 解析、RAW 保真检查和原子文件发布基础设施。

### 7.3 必须保持候选状态的模块

以下内容可以开发和测试，但在完整验收前不能标记为稳定：

- `IfcSpatialZone` 的 Revit Area/GenericModel → `IfcSpatialZone` 生成或匹配；
- `IfcActor → IfcOrganization` 的组织 Owner 解析；
- 必填和 cardinality；
- Strict/Force 业务语义；
- RAW/Candidate/Final 证据分层；
- 扫描与导出模型快照一致性；
- Stage03 三件套和 IFCFlux 全流程验收。

## 8. 运行时支持状态

每条规则或 Owner 策略必须能导出明确状态：

```text
SUPPORTED
NOT_IMPLEMENTED
UNCLASSIFIED_REQUIREMENT
OFFICIAL_EVIDENCE_ONLY
```

对应行为：

- `SUPPORTED`：可以进入运行时扫描、写入或转译；
- `NOT_IMPLEMENTED`：显示具体诊断并阻断依赖该能力的操作；
- `UNCLASSIFIED_REQUIREMENT`：不得被 Strict 当成已满足，报告为需求等级待定；
- `OFFICIAL_EVIDENCE_ONLY`：只用于对账，不自动转化为 Revit 写入策略。

不得把未实现 Owner 静默改挂到 ProjectInformation，也不得因字段未分类就视为可选并宣称完整通过。

## 9. 错误处理与审计

规则冻结和 GH 运行必须遵循 fail-closed：

- identity drift：编译失败，不产生 pack；
- manifest 漂移：CI 失败；
- fixture 生成不确定：CI 失败；
- 规则包资源或 hash 不一致：GH 阻断；
- Owner 策略未实现：字段级 `RuleNotImplemented`；
- 模型在扫描与导出之间变化：本次 Stage03 运行失败；
- RAW/Candidate/Final 证据不完整：不得发布成功报告；
- IFCFlux 或 Revit 实机验收未执行：状态必须显示未验收，不计作通过。

## 10. 测试与验收

### 10.1 规则合同

- 359 条 propertyId、canonicalKey、IFC identity 唯一；
- 官方 166 条最终 identity 与 official originalIdentity 达到 166/166；
- X/Y 无空格输出，带空格仅为 raw/alias；
- 无缺失 Entity、Pset、Property、declared type；
- 运行时支持状态完整且枚举合法。

### 10.2 生成物

- pack 相同输入产生相同字节；
- fixture 相同输入产生相同字节；
- manifest 全部路径、大小和 SHA-256 匹配；
- 带空格 X/Y mutation 必须失败；
- fixture 保持 616 STEP、359 property、52 Pset、52 attachment 和 14 类 Owner。

### 10.3 GH 与运行时

- GHA 只包含并加载一个规则包；
- packageId/version/hash 在 FileContext、TaskPlan 和 Stage03 报告中一致；
- Stage01/02/03 不直接读取历史映射目录；
- 未实现策略稳定返回明确诊断；
- Release build 无警告；
- Python、.NET 和 Windows EOL 门禁通过。

### 10.4 外部软件

- IFCFlux 识别 `基点坐标X/Y` 且数值、轴向不交换；
- Revit 2020 + Rhino 8 + Rhino.Inside 加载新 GHA；
- Stage03 输出 RAW IFC、HIFC-MVD IFC 和字段报告三件套；
- 三件套的 runId、规则 hash、模型身份和文件 SHA-256 一致；
- 保存重开后结果可复现。

## 11. 冻结完成定义

映射基线 v1.0.0 只有在以下条件全部满足后才打 tag：

1. 唯一源中的 X/Y canonical identity 已统一；
2. 官方 166 identity 为 166/166；
3. 359 条均有明确运行时支持状态；
4. generator 已恢复并确定性重建 fixture；
5. 规则 manifest 全量匹配；
6. Python、.NET、Release、EOL、fixture 门禁通过；
7. IFCFlux B 验收证据归档；
8. 当前 WIP 被拆分为可审计提交；
9. 远端提交具有对应 CI check；
10. tag、ZIP 与 GHA 均记录同一规则包 SHA-256。

映射基线冻结后，GH 插件开发继续进行；Stage03 完整生产稳定版仍需等待未实现 Owner、门禁语义和真实三件套闭环完成。
