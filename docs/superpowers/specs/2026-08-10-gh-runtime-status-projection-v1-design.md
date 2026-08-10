# GH 运行状态统一投影 v1 设计

**状态：** 已批准，待书面复核

**批准日期：** 2026-08-10

**目标分支：** `feat/gh-plugin-hbr-planning-v1.0.0`

**规则基线：** `hbr-planning-mapping-v1.0.0`

## 1. 目标

让 Grasshopper 用户在 Stage02 和 Stage03 中直接看到每条规则由唯一规则库计算出的运行支持状态及原因，同时保持现有写入和导出门禁语义不变。

本增量解决的是状态不可见和多处重复推导问题，不修改冻结映射，不实现缺失的 Owner strategy，也不重新分类 359 条需求等级。

## 2. 当前问题

唯一运行时规则入口 `HbrRuleDatabase.Current` 已能通过 `GetEffectiveRuntimeStatus()` 为每条属性计算最终状态。冻结基线当前结果为：

```text
57  NOT_IMPLEMENTED
302 UNCLASSIFIED_REQUIREMENT
```

但生产路径尚未统一消费该结果：

- Stage02 字段明细没有 runtime status，也不显示原因；
- Stage02 当前允许 `UNCLASSIFIED` 字段进入预览和写入流程；
- Stage03 能产生 `RULE_NOT_IMPLEMENTED` 和 `UNCLASSIFIED_REQUIREMENT` 扫描状态，但通过独立策略重复推导；
- Stage03 的字段 `status` 同时承载规则能力和模型扫描结果，用户无法明确区分两者；
- Stage02 和 Stage03 卡片没有统一的运行支持状态计数。

## 3. 方案选择

采用“先统一投影，不改变门禁”的方案。

本轮不会把 runtime status 直接转成新的 Stage02 blocker。因为当前 359 条全部处于 `NOT_IMPLEMENTED` 或 `UNCLASSIFIED_REQUIREMENT`，立即改变写入门禁会导致 Stage02 全量不可写，属于独立的业务语义变更，应在状态可见后另行设计和验收。

## 4. 范围

### 4.1 纳入本轮

- 建立集中式、强类型的 runtime status decision；
- Stage02 预览字段携带并输出 runtime status、稳定原因码和可读原因；
- Stage02 卡片显示当前预览的状态数量和首个原因；
- Stage03 字段结果独立携带 runtime status，不再让用户从扫描 `status` 反推规则能力；
- Stage03 字段 Data Tree、fields JSON 和卡片展示同一 runtime status；
- 以真实冻结规则验证优先级：Owner 未实现优先于需求未分类；
- 保持确定性 JSON、稳定排序和现有 GH 端口顺序。

### 4.2 不纳入本轮

- 不修改 `specs/hbr-rules/v1/source/hbr_rule_source.v1.json`；
- 不修改或移动 `hbr-planning-mapping-v1.0.0` Tag；
- 不新增、删除或重排 GH 输入输出端口；
- 不改变 Stage02 是否允许安装或写入参数；
- 不改变 Stage03 Strict/Force、业务阻断和技术致命门禁；
- 不实现 `CANONICAL_SPATIAL_ZONE_RECORD`；
- 不实现 `USER_SELECTED_EXPORTABLE_GENERIC_MODEL`；
- 不扩展 Stage01 字段 UI；Stage01 投影作为后续独立增量；
- 不把 fixture 样例值写入规则库或生产默认值。

## 5. 核心模型

新增不可变的 runtime decision，至少包含：

```text
Status       SUPPORTED | NOT_IMPLEMENTED |
             UNCLASSIFIED_REQUIREMENT | OFFICIAL_EVIDENCE_ONLY
ReasonCode   稳定机器码
Reason       可读诊断，不包含样例值或敏感值
```

推荐稳定原因码：

```text
OWNER_STRATEGY_NOT_IMPLEMENTED
REQUIREMENT_LEVEL_UNCLASSIFIED
OFFICIAL_EVIDENCE_ONLY
SUPPORTED
```

`HbrRuleDatabase` 是唯一决策入口。现有 `GetEffectiveRuntimeStatus()` 保留兼容，由新的 decision API 返回值投影 `Status`，避免形成第二套状态优先级。

优先级继续由冻结规则包定义并由数据库校验：

```text
NOT_IMPLEMENTED
> UNCLASSIFIED_REQUIREMENT
> OFFICIAL_EVIDENCE_ONLY
> SUPPORTED
```

任何未知状态、未知 Owner strategy、未知 requirement level 或缺失原因都 fail-closed，不生成可继续消费的 decision。

## 6. Stage02 投影

`Stage02WriteOperation` 增加只读运行支持元数据：

```text
RuntimeStatus
RuntimeBlockCode
RuntimeBlockReason
```

`Stage02PreviewCompiler` 必须使用其已持有的 `HbrRuleDatabase` 对真实 `HbrRuleProperty` 求值，并把 decision 固化进预览快照。formatter 和 UI 只消费该快照，不再根据 `RequirementLevel` 或 Owner strategy 自行猜测状态。

现有“字段明细”Data Tree 的每个稳定 JSON 记录追加：

```json
{
  "runtimeStatus": "NOT_IMPLEMENTED",
  "runtimeBlockCode": "OWNER_STRATEGY_NOT_IMPLEMENTED",
  "runtimeBlockReason": "当前 IFC owner strategy 尚未实现。"
}
```

以上只是结构示例，不是新的映射或业务样例值。原有字段和键顺序保持稳定，新键固定插入在 `applicability` 后、写入动作前。

Stage02 卡片在预览存在时显示当前预览内的确定性计数：

```text
运行支持｜未实现 N｜需求待定 N
```

如存在非 `SUPPORTED` 字段，再显示稳定排序后的首个原因。现有 `Blockers` 集合不注入 runtime decision，本轮不改变写入行为。

## 7. Stage03 投影

`Stage03FieldResult` 增加与 Stage02 同源的：

```text
RuntimeStatus
RuntimeBlockCode
RuntimeBlockReason
```

现有 `Status`、`CarrierStatus`、`ParameterStatus`、`RevitStatus`、`RawIfcStatus` 和 `FinalIfcStatus` 保留，继续表达模型扫描、参数读取和 IFC 对账结果。

Stage03 字段 Data Tree 与 fields JSON 同时输出 runtime decision 和现有扫描状态，使以下两类问题可区分：

```text
规则当前是否可运行
模型本次扫描是否通过
```

现有 `RULE_NOT_IMPLEMENTED`、`UNCLASSIFIED_REQUIREMENT` 业务阻断码以及 Strict/Force 行为保持不变。回归测试必须证明 runtime decision 与现有门禁结果一致，但本轮不重写门禁策略。

Stage03 卡片增加 runtime status 数量摘要；原有扫描通过数、阻断数、技术错误和首条阻断继续显示。

## 8. 数据流

```text
HBR_RulePack.hbrpack
  -> HbrRuleDatabase.Current
  -> GetRuntimeStatusDecision(property)
  -> Stage02WriteOperation / Stage03FieldResult
  -> 现有 Data Tree、卡片与 fields JSON
```

formatter、UI 和 report writer 不得读取历史 JSON、CSV 或 `specs/hifc-mapping/v1`，也不得复制状态优先级。

## 9. 兼容与错误处理

- GH 组件 GUID 和输入输出端口数量不变，既有连线不受影响；
- 现有 JSON 字段不删除、不改名；新增字段使用固定键名和顺序；
- runtime decision 缺失时，预览或扫描 fail-closed，不输出伪造的 `SUPPORTED`；
- runtime 原因不得包含 Revit 业务值、fixture 样例值或异常堆栈；
- Stage02 预览失效、上升沿和事务语义保持不变；
- Stage03 runId、路径、原子发布和报告证据语义保持不变。

## 10. 测试设计

实现前先建立以下 RED：

1. `HbrRuntimeStatusProjectionTests`
   - 真实双重不支持属性必须按优先级得到 `NOT_IMPLEMENTED`；
   - 普通未分类属性必须得到 `UNCLASSIFIED_REQUIREMENT`；
   - decision 提供稳定原因码和原因；
   - decision 不自动创建 Stage02 写入 blocker。
2. `Stage02PreparationInputPolicyTests`
   - 字段 JSON 包含三个 runtime 字段；
   - 键顺序、转义和重复格式化字节稳定。
3. `Stage02` 组件合同测试
   - 组件及卡片通过集中式 decision 投影状态；
   - 禁止从 requirement 或 Owner strategy 在 UI 层自行推导。
4. `Stage03FieldDetailFormatterTests` 和报告测试
   - runtime status 与扫描 status 同时存在；
   - Data Tree 与 fields JSON 值一致；
   - 既有 Strict/Force 结果不变。

定向测试通过后，执行：

- Stage02/Stage03 相关 .NET 测试；
- Core 全量测试；
- Python 合同与发布测试；
- Release 构建，要求 0 warning / 0 error；
- `git diff --check`、冻结源和 Tag 不变检查。

## 11. 完成标准

只有以下条件全部满足，本增量才算完成：

1. Stage02 每条字段可见唯一 runtime status、原因码和原因；
2. Stage02 卡片显示当前预览的状态计数；
3. Stage03 将规则运行能力与模型扫描结果分开输出；
4. Data Tree、卡片和 fields JSON 使用同一 decision；
5. Stage02 写入门禁和 Stage03 Strict/Force 行为无变化；
6. GH 端口和组件 GUID 无变化；
7. 冻结映射源、manifest 和 Tag 无变化；
8. RED、GREEN、全量回归、Release 构建和静态检查均有可复核结果；
9. 功能实现与测试独立提交，提交历史可审计。

## 12. 后续顺序

本增量完成后，按独立设计依次评估：

1. Stage02 是否应对 `NOT_IMPLEMENTED` 提前阻断；
2. Stage01 字段行和初始化卡片的 runtime status 投影；
3. `CANONICAL_SPATIAL_ZONE_RECORD` Owner strategy；
4. `USER_SELECTED_EXPORTABLE_GENERIC_MODEL` Owner strategy；
5. 需求等级分类完成后的 Strict/Force 正式语义；
6. 真实 RVT -> 官方插件 -> IFC -> IFCFlux/checker 闭环。
