# GH 插件开发基线归档（2026-08-11）

本文件冻结“当前开发到哪里、依据是什么、哪些尚未通过实机验收”。它记录的是开发快照，不创建第二份映射库，也不替代规则源、验收清单或 Git 历史。

机器可读证据见 [2026-08-11-validation-evidence.json](2026-08-11-validation-evidence.json)。

## 1. 快照身份

| 项目 | 冻结值 |
|---|---|
| 仓库分支 | feat/gh-plugin-hbr-planning-v1.0.0 |
| 开发代码提交 | b6eef878ccf53a17569ac7c5766076a5a0fd852e |
| 远端跟踪提交 | b6eef878ccf53a17569ac7c5766076a5a0fd852e |
| 映射基线 Tag | hbr-planning-mapping-v1.0.0 |
| Tag 提交 | 0c5d2c1100c9c80c4306354bab553debe8f191ca |
| 规则包 | HBR-WUHAN-PLANNING / 1.0.0 |
| GHA 版本 | 0.9.0.0 |
| GHA 大小 | 1,569,280 bytes |
| GHA SHA-256 | dcff8939bc70ce335a4603e83de46ba4577571841985055cc57b98c68451217e |

捕获快照时，本地分支与远端无领先或落后，工作树干净。部署目录中只有一个活动 GHA，且没有 bak、backup 或 pending 副本。

代码提交与归档记录是两个不同身份：上表的 b6eef87 是被归档的实现快照；本文件所在的后续 Git 提交只负责记录该快照。

## 2. 当前开发依据及优先级

发生描述冲突时，按下表从上到下判定。

| 优先级 | 依据 | 负责回答 |
|---:|---|---|
| 1 | [三阶段规则数据库设计](../superpowers/specs/2026-08-02-hbr-three-stage-rule-database-design.md) | 当前产品边界、三阶段职责、标准 IFC4 加确定性后处理的运行路径 |
| 2 | [机器唯一规则源](../../specs/hbr-rules/v1/source/hbr_rule_source.v1.json) | 359 条属性的唯一可编辑业务定义 |
| 3 | [规则 Schema](../../specs/hbr-rules/v1/schemas/hbr_rule_source.schema.json)、[兼容基线](../../specs/hbr-rules/v1/compatibility/hbr_rule_compatibility_baseline.v1.json)、[哈希清单](../../specs/hbr-rules/v1/manifest.sha256.json) | 结构、兼容边界、可重建性和漂移门禁 |
| 4 | [HBR–H-IFC 映射唯一依据](../hifc/HBR_HIFC_mapping_authority_v1.md) | IFC Entity、PropertySet、Property、类型、单位和 Owner 的人类可读说明 |
| 5 | [映射基线设计](../superpowers/specs/2026-08-10-hbr-planning-mapping-baseline-v1-design.md) | 映射冻结、唯一规则链、规则稳定与运行完整的区别 |
| 6 | [GH 运行状态投影设计](../superpowers/specs/2026-08-10-gh-runtime-status-projection-v1-design.md) | Stage02、Stage03 如何展示统一 runtime decision，且不改变既有门禁 |
| 7 | [v0.9.0 用户手册](../revit2020-v090-user-manual.md) 与 [实机验收清单](../revit2020-v090-acceptance-checklist.md) | 用户操作和最终完成定义 |

### 历史资料边界

[specs/hifc-mapping/v1](../../specs/hifc-mapping/v1/README.md) 只保存官方工具提取证据、迁移输入和历史研究。该目录中的“官方插件唯一导出”兼容性状态不是当前三阶段产品的运行依据，生产 GH 也不得直接读取该目录中的旧规则、bindings 或 shared parameters。

[HBR–H-IFC 映射唯一依据](../hifc/HBR_HIFC_mapping_authority_v1.md) 是 Tag 冻结的人类可读映射说明；其页首“适用分支”保留的是形成该文档时的历史元数据，不代表当前开发分支。当前机器权威仍是唯一规则源及 Tag hbr-planning-mapping-v1.0.0。

当前 Stage03 产品路径以三阶段设计和仓库 README 为准：

    Revit 当前模型
      -> Autodesk 标准 IFC4 RAW
      -> 确定性 H-IFC 后处理
      -> HIFC-MVD 候选与字段证据

这条产品路径不等于“官方 H-IFC 插件导出并由官方检查器通过”。

## 3. 唯一规则链

唯一可编辑业务源固定为：

    specs/hbr-rules/v1/source/hbr_rule_source.v1.json

当前冻结规模：

- 359 条属性；
- 52 个 PropertySet；
- 14 个载体角色；
- 3 个模型 profile；
- 14 个项目条件；
- 28 个任务；
- 166 条官方提取 identity 对账；
- runtime 投影为 57 条 NOT_IMPLEMENTED、302 条 UNCLASSIFIED_REQUIREMENT。

生成与消费链固定为：

    hbr_rule_source.v1.json
      -> schema、语义和兼容校验
      -> HBR_RulePack.hbrpack
      -> 嵌入 BIMBaoGui.Stage01.gha
      -> HbrRuleDatabase.Current
      -> Stage01 / Stage02 / Stage03

从 Tag hbr-planning-mapping-v1.0.0 到开发提交 b6eef87，冻结规则目录没有差异。后续 Stage02 迭代继续消费这一个规则源；除非先形成新的映射变更决定、版本和迁移方案，否则不得直接改动冻结规则。

坐标当前 identity 为：

| 含义 | 最终 IFC identity | 原始证据字段 |
|---|---|---|
| X，Northing，南北坐标 | IfcProject / Pset_申报信息属性集 / 基点坐标X | 基点坐标 X |
| Y，Easting，东西坐标 | IfcProject / Pset_申报信息属性集 / 基点坐标Y | 基点坐标 Y |

带空格名称只用于原始证据或旧输入迁移，最终 IFC 不双写旧名称。

## 4. 已经开发了什么

### Stage01：文件初始化

已开发：

- 初始化项目身份、模型类型、坐标 X/Y、高程、真北、项目条件和规划目标；
- 在 Revit 事务组内写入、回读并在失败时整体回滚；
- 输出携带文档指纹、规则包身份和 payload hash 的强类型 HBR_FileContext；
- 生成原子、脱敏并包含事务状态的失败报告；
- 旧坐标键迁移：基点坐标 X/Y 迁移到当前基点坐标X/Y；
- 当前 payload 的 canonical 内容和 SHA-256 完整性校验；
- 旧 10 条件 payload 按唯一规则包补齐到当前 14 条件，保留旧值、只补缺键并保持幂等。

关键增量提交：

| 提交 | 内容 |
|---|---|
| 69b7f12 | 迁移旧坐标字段键 |
| 5e3fff7 | 校验当前已存 payload 的 canonical 内容和 hash |
| b6eef87 | 将旧 10 条件 payload 升级为当前 14 条件 |

实机状态：用户已确认当前 Stage01 暂无问题，且最新运行已经进入 Stage03。这里记录为“指定 RVT 烟测可用”，不替代验收清单中的保存重开、逐字段和哈希留证。

### Stage02：构件与属性准备

已开发：

- 当前选择、ElementId、Revit 交互点选和 ProjectInformation 四种明确选择方式；
- 依据 FileContext、模型条件、载体角色、类别和名称证据进行 fail-closed 匹配；
- 生成按 UniqueId 与 propertyId 稳定排序的预览；
- 预览固化参数 GUID、旧值、建议值、建议来源、适用性、绑定动作、阻断及 runtime decision；
- 文档指纹、规则包、角色、旧值、preview hash 和 nonce 的确认前复核；
- 旧预览、错文档、值漂移或重复确认时拒绝写入；
- 在 Revit 事务中安装或复用 UI 可见共享参数、合并类别绑定、写入非空建议值并按 GUID 回读；
- 技术失败回滚并写入原子、脱敏、可关联当前输入身份的 Stage02 报告；
- Stage02 Data Tree 和卡片展示 runtimeStatus、runtimeBlockCode、runtimeBlockReason；
- runtime decision 统一由 HbrRuleDatabase 计算，不由 UI 或 formatter 重复推导。

关键增量提交：

| 提交 | 内容 |
|---|---|
| eecc78e | 集中 runtime status decision |
| fb4ac1a | Stage02 预览、Data Tree 和 UI 展示 runtime 状态 |
| b0f3f52 | 补齐 Stage02 runtime canonical、篡改和合同测试 |

当前边界：

- runtime 状态“已可见”，但按批准设计暂不转成新的 Stage02 写入 blocker；
- 57 条 NOT_IMPLEMENTED 与 302 条 UNCLASSIFIED_REQUIREMENT 不等于 359 条均已生产支持；
- 当前规则源的 359 条业务属性均为 INSTANCE；TYPE 分支有策略和自动测试，但没有当前规则字段触发；
- 用户本次跳过 Stage02 直接执行 Stage03，因此指定 RVT 的 Stage02 实机闭环仍未完成。

Stage02 必须继续验证：ProjectInformation、至少一个实例载体、属性面板 GUID 回读、保存关闭重开后的持久性、切换 RVT 后旧预览失效，以及真实失败报告与当前输入身份一致。

### Stage03：检测、导出与 H-IFC 转译

已开发：

- 规则驱动的全模型载体、参数、值、Owner 和 runtime 状态扫描；
- Strict 业务门禁与 Force 原因合同；技术致命错误不可绕过；
- Autodesk Revit 标准 IFC4 RAW 导出；
- 后台创建或更新 Pset 与属性、复读验证、RAW 不改写和 final 原子发布；
- runId 贯穿 RAW、HIFC-MVD、fields JSON 和失败报告；
- 禁止静默覆盖已有目标，失败时保留已有证据；
- Stage03 扫描状态与规则 runtime 状态分开输出到 Data Tree、fields JSON 和卡片。

关键增量提交：

| 提交 | 内容 |
|---|---|
| 8d8d0b7 | 分离 Stage03 扫描状态与 runtime 支持状态 |
| 4cd39c1 | 明确 runtime 原因码与 Strict/Force gate blocker 码互不耦合 |

最新实机 run：

    run-20260811024412878-44501703f06f4acb9823e42173156867

该 run 的准确结论是：

- RAW IFC 已生成，42,155 bytes，导出日志为 Operation terminated successfully；
- HIFC-MVD 候选文件已生成，43,045 bytes；
- 随后在 TRANSLATE-IFC 阶段以 INVALID_IFC 和 System.IO.InvalidDataException 结束；
- 本 run 没有 fields JSON。

因此只能归档为“文件已导出，完整转译验收未通过”。HIFC-MVD 文件存在不等于工作流成功，也不等于 IFCFlux 或官方检查器闭环通过。

## 5. 自动化、构建与部署证据

开发提交 b6eef87 在本次归档提交前重新执行的完整验证记录：

| 验证项 | 结果 |
|---|---:|
| Python | 569 passed |
| .NET Core 测试 | 1286 passed / 0 failed / 0 skipped |
| Release 构建 | 0 warning / 0 error |
| 最近修复复审 | Critical / Important / Minor = 0 / 0 / 0 |

部署证据：

- 仓库候选、Release 输出、artifact manifest 和活动部署 GHA 的 SHA-256 一致；
- 活动 GHA SHA-256 为 dcff8939bc70ce335a4603e83de46ba4577571841985055cc57b98c68451217e；
- 活动目录只有一个 BIMBaoGui.Stage01.gha，没有备份或 pending 插件。

自动化通过证明代码合同、规则一致性和构建产物可复核，不代替 Revit 2020、Grasshopper、IFCFlux 或检查器实机验收。

## 6. 下一阶段唯一重点：Stage02

下一轮不先改映射规则，先完成指定 RVT 的 Stage02 证据闭环。

### 第一组：固定测试身份

1. 关闭并保存 RVT 后记录 RVT、GH、活动 GHA 的 SHA-256，并单独记录代码提交 ID。
2. 使用当前 Stage01 重新生成 HBR_FileContext。
3. 固定一个 ProjectInformation 对象和至少一个实例载体作为哨兵。

### 第二组：预览与确认

1. 触发一次预览上升沿，记录匹配角色、字段数、blocker、runtime 三字段和 preview hash。
2. 核对建议值来源；fixture 样例值不得成为生产默认值。
3. 触发一次确认上升沿，验证写入只消费一次。
4. 在 Revit 属性面板按固定 GUID 回读参数名称、值和可编辑性。

### 第三组：持久性与失效

1. 保存、关闭并重开 RVT，复核哨兵参数仍存在且值一致。
2. 修改源值或切换到另一 RVT，验证旧预览明确失效且未进入写入队列。
3. 制造一个受控技术失败，核对失败报告与当前文档、规则和请求身份一致。

### Stage02 完成门槛

只有上述证据全部落入 [v0.9.0 实机验收清单](../revit2020-v090-acceptance-checklist.md)，才能把 Stage02 从“代码与自动化完成”提升为“指定 RVT 实机验收完成”。若实机暴露缺陷，再以最小代码改动、回归测试、重建部署和同一 RVT 复测闭环。

## 7. 明确不作出的结论

- 不宣称 359 条字段均已生产支持；
- 不宣称 Stage02 已完成指定 RVT 实机验收；
- 不把 fixture 样例值当成正式规则或生产默认值；
- 不把 HIFC-MVD 文件存在等同于 Stage03 全链路成功；
- 不把标准 IFC4 加后处理等同于官方插件加官方检查器兼容；
- 不移动 hbr-planning-mapping-v1.0.0 Tag，不创建第二份可编辑映射表。
