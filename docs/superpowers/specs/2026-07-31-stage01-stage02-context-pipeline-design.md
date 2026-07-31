# Stage 01 → Stage 02 数据驱动报规工作流设计

- 状态：已确认设计，待用户复核书面规格
- 基线：Revit 2020 + Rhino 8 + Rhino.Inside.Revit
- 产品形态：编译型 Grasshopper `.gha` 组件
- 适用范围：单文件报规工作流的文件初始化、任务分流与骨架任务编译

## 1. 已冻结的核心决策

1. `01 文件初始化` 是后续所有组件的数据源，不再只是独立表单。
2. `02 模型任务与骨架分流` 必须通过 Grasshopper 数据线接收 `HBR_FileContext`，显式显示上下游依赖。
3. 当前模型类型固定为三类：
   - 总平模型
   - 单体建筑—地上
   - 单体建筑—地下
4. 第一阶段记录项目事实、规划控制目标和文件身份；第二阶段据此编译本文件应执行的建模对象、属性和检查。
5. 上游数据变化后，`FileContextHash` 改变，第二阶段及下游结果自动进入待重新编译或待复检状态。
6. Revit Extensible Storage 用于持久化和恢复；Grasshopper 连线仍是主要依赖表达方式，不允许第二阶段静默使用隐藏配置。

## 2. 规划控制指标的输入规则

### 2.1 总平模型必填项

以下项目级规划控制目标在总平模型中必须填写：

| 指标 | 必填状态 | 业务类型 | IFC/MVD 输出 |
|---|---|---|---|
| 建筑密度 | 必填 | 结构化约束 | 标准文本 |
| 容积率 | 必填 | 结构化约束 | 标准文本 |
| 绿地率 | 必填 | 结构化约束 | 标准文本 |

单体地上、单体地下文件不重复人工填写上述项目总目标，而是从其所属项目上下文继承。

### 2.2 结构化目标值

上述指标不得作为任意文本录入。内部统一使用：

```text
PlanningTargetValue
├─ MetricCode
├─ Operator: ≤ / ≥ / = / 区间
├─ Value1
├─ Value2（仅区间使用）
├─ Unit: % / 无量纲 / 个
├─ Source: 项目初始化
└─ DisplayText
```

示例：

```text
建筑密度 ≤ 30%
容积率 ≤ 2.00
绿地率 ≥ 35%
```

写入 MVD 时序列化为稳定文本：`≤30%`、`≤2.00`、`≥35%`。模型计算值使用独立字段保存，只参与对比，不覆盖目标值。

### 2.3 条件必填项

- 建筑限高：存在规划限高要求时必填；单体地上可继承项目目标。
- 海拔限高：项目适用时必填。
- 机动车位：勾选停车相关条件时必填。
- 非机动车位：勾选非机动车相关条件时必填。
- 非机动车类型、折算系数：启用相应折算规则时必填。

界面必须同步显示 `必填 / 条件必填 / 选填 / 系统`，并在提交前按当前模型类型和项目条件动态校验。

## 3. `HBR_FileContext`

第一阶段输出编译型自定义 Grasshopper 数据对象，而不是普通 JSON 字符串。

```text
HBR_FileContext
├─ SchemaVersion
├─ WorkflowVersion
├─ FileGuid
├─ RevitDocumentFingerprint
├─ ProjectNumber / ProjectName
├─ SubitemCode / SubitemName
├─ ModelFileType
├─ ModelScope
├─ SpatialReference
├─ PlanningTargets
├─ ProjectConditions
├─ ActivatedRuleIds
├─ NotApplicableRuleIds
├─ InitializationPassed
├─ RulePackVersion
└─ FileContextHash
```

技术实现：

- `HBRFileContext`：不可变业务对象。
- `HBRFileContextGoo : GH_Goo<HBRFileContext>`：Grasshopper 数据封装。
- `HBRFileContextParam : GH_PersistentParam<HBRFileContextGoo>`：专用参数和端口。
- 支持 GH 文件序列化、复制、查看摘要和 JSON 调试输出。
- `FileContextHash` 使用确定性规范化载荷计算。

### 3.1 第一阶段组件输出

建议输出顺序：

1. `文件上下文`：`HBR_FileContext`
2. `初始化通过`：Boolean
3. `状态`：Text
4. `消息`：Text List
5. `上下文JSON`：Text，仅用于调试和外部检查

只有写入 Revit 并回读一致后，`InitializationPassed` 才为 `true`。

## 4. `02 模型任务与骨架分流`

### 4.1 输入

- `文件上下文`：必接 `HBR_FileContext`

阻断条件：

- 未连接第一阶段输出；
- `InitializationPassed = false`；
- 上下文对应的 Revit 文档指纹与当前活动文档不一致；
- 上下文版本或规则包版本不兼容；
- 上下文哈希无效。

### 4.2 输出

```text
HBR_TaskPlan
├─ FileContextHash
├─ ModelFileType
├─ RequiredObjects
├─ ConditionalObjects
├─ NotApplicableObjects
├─ AttributeRequirements
├─ BuildSequence
├─ Dependencies
├─ GeometryChecks
├─ PropertyChecks
├─ TargetComparisons
├─ SkeletonTasks
└─ TaskPlanHash
```

同时输出：

- `任务计划`：`HBR_TaskPlan`
- `骨架路径`：总平 / 地上 / 地下
- `激活任务`：文本摘要
- `阻断信息`：文本列表

### 4.3 分流逻辑

#### 总平模型

默认激活：

- 规划总用地
- 规划净用地
- 建筑轮廓或建筑占地表达
- 总平空间基准骨架

按条件激活：

- 其他分类用地
- 道路红线、道路中心线、区内道路
- 消防道路、消防登高或操作场地
- 绿地
- 室外停车场或车位
- 人防区域
- 室外构筑物与设施

#### 单体建筑—地上

根据规则包激活：

- 建筑主体与楼层骨架
- 屋顶、阳台、雨篷等条件对象
- 高度、层数、面积和项目目标继承检查

#### 单体建筑—地下

根据规则包激活：

- 地下建筑与地下楼层骨架
- 地下停车、人防、地下空间等条件对象
- 地下范围、层数、面积及关系检查

## 5. 数据流与失效机制

```text
01 文件初始化
    │ HBR_FileContext
    ▼
02 模型任务与骨架分流
    │ HBR_TaskPlan
    ├─────────────┬─────────────┐
    ▼             ▼             ▼
总平建模组件   地上建模组件   地下建模组件
```

失效规则：

- 模型类型、模型范围、坐标基准、规划目标或项目条件变化，必须生成新的 `FileContextHash`。
- 第二阶段检测到哈希变化后重新编译 `TaskPlan`。
- 已完成但受影响的任务进入 `待复检`，不得继续显示为永久通过。
- `TaskPlan` 必须记录其来源 `FileContextHash`，禁止跨文件复用错误的任务计划。

## 6. Revit 持久化与恢复

- 第一阶段继续把完整初始化载荷、哈希和版本写入 Revit Extensible Storage。
- Revit 重新打开后，第一阶段组件读取存储结果并重新输出同一类型的 `HBR_FileContext`。
- 第二阶段第一版不自动绕过连线读取 Revit，以保持依赖关系可见。
- 后续可增加独立的 `读取文件上下文` 组件，但它仍输出同一种 `HBR_FileContext`。

## 7. 错误处理与用户反馈

### 第一阶段

- 目录显示未完成数量。
- 输入框展示类型、单位和示例。
- 格式错误时保留编辑状态并解释正确写法。
- 规划控制指标缺失时直接定位到“规划控制指标”。

### 第二阶段

- 未连接上下文时显示：`请连接 01 文件初始化的“文件上下文”输出。`
- 文档不匹配时显示当前文件名和上下文文件名。
- 初始化未通过时显示首个阻断目录与字段。
- 任务编译成功后明确显示模型类型、激活任务数和条件任务数。

## 8. 测试要求

### 单元测试

- 三类模型的必填矩阵。
- 建筑密度、容积率、绿地率的结构化输入与序列化。
- 运算符、数值范围和单位校验。
- `HBR_FileContext` 确定性哈希与 GH 序列化。
- 三类模型的任务编译结果。
- 条件勾选对任务增删的影响。
- 上下文哈希变化导致任务计划失效。

### 集成测试

- Stage 01 写入、回读、重新打开后恢复并输出上下文。
- Stage 01 与 Stage 02 真实 GH 连线。
- Revit 文档指纹不匹配时阻断。
- 修改项目条件后 Stage 02 自动更新任务计划。

### 实机验收

- 总平模型完成初始化后，第二阶段只产生总平任务。
- 地上模型只产生地上任务，继承项目规划目标。
- 地下模型只产生地下任务，并按停车、人防等条件激活对象。
- 任何上游变化都能明确触发重新编译或待复检。

## 9. 本轮不包含

- 具体总平、地上、地下几何建模算法。
- 既有正式模型的坐标修复和整体变换。
- 跨多个 Revit 文件的项目级汇总。
- H-IFC 导出和平台回读。

上述内容分别在 `HBR_TaskPlan` 稳定后进入后续实施阶段。