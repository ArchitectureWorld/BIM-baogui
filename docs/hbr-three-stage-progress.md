# HBR 三阶段开发总进度

> 最后更新：2026-08-04 02:35（UTC+08:00；Task 7 完成，Task 8 启动）
> 计划基线：[`2026-08-02-hbr-three-stage-implementation.md`](superpowers/plans/2026-08-02-hbr-three-stage-implementation.md)
> 当前分支：`fix/official-hifc-hardening-v090`

## 一屏看懂

| 你最关心的项目 | 当前结果 |
|---|---|
| 整体正式完成度 | **58%：7/12 个 Task 已通过实现、验证、双审查和提交** |
| 含当前开发中的执行进度 | **约 58%：Task 7 已验收，Task 8 正进入首个 RED 测试** |
| 当前正在做 | **Task 8：IFC4 STEP 实体插入与缺失 Pset/属性转译** |
| 当前运行环节 | **TDD 准备**：锁定 `AddEntity`、IFC GUID、Pset/关系创建与回读合同 |
| 当前阻塞 | **无**；Task 8 可直接继续，不需要外部插件或用户点击 |
| 现在是否需要你操作 | **不需要**；Task 11 部署时如相关程序未关闭，可能需要配合关闭；Task 12 明确需要实机点击验收 |
| 下一节点 | `IfcStepDocument.AddEntity` RED → 最小可变 STEP 文档实现 → enrichment RED/GREEN |

状态图例：✅ 已完整验收；🟡 开发中；🔵 自动化验证中；🟣 待审查；⏳ 等待前置任务/实机；⬜ 未开始；❌ 未通过/阻塞。

## 总览

**已验收完成度：58%**（7/12 个 Task 已完成“实现、验证、审查、提交”全部闸门）

`███████████▋░░░░░░░░ 58%`

**当前执行进度：约 58%**（Task 7 已正式完成；Task 8 刚进入 TDD 准备）

`███████████▌░░░░░░░░ 约 58%`

| 工作包 | 包含任务 | 状态 | 完成度 |
|---|---:|---|---:|
| 统一规则与运行时基础 | Task 1–3 | ✅ 完成 | 100% |
| Stage02 属性准备 | Task 4–6 | ✅ 完成 | 100% |
| Stage03 检测、导出与转译 | Task 7–10 | 🟡 Task 7 完成，Task 8 启动 | 25% |
| CI、文档与单 GHA 部署 | Task 11 | ⬜ 未开始 | 0% |
| Revit 2020 实机闭环 | Task 12 | ⏳ 待实机 | 0% |

## 12 项总计划与实时状态

| Task | 交付目标 | 状态 | 进度 | 当前证据 / 下一验收门槛 | 需要用户操作 |
|---:|---|---|---:|---|---|
| 1 | 建立 359 条单一规则源与结构、语义合同 | ✅ 完成 | 100% | 单一 JSON 规则源和合同测试已落地 | 否 |
| 2 | 确定性 `.hbrpack` 编译与构建集成 | ✅ 完成 | 100% | 生产 GHA 只携带唯一规则包；禁止多份规则源混载 | 否 |
| 3 | 单一运行时规则数据库与跨阶段身份传播 | ✅ 完成 | 100% | `packageId + version + hash` 已作为跨阶段身份 | 否 |
| 4 | Stage02 纯匹配、预览和一次性确认合同 | ✅ 完成 | 100% | 已提交 `6b2f335`；旧预览、错文档和旧值变化会被阻断 | 否 |
| 5 | Revit 选择、可见共享参数、原子写入与失败报告 | ✅ 完成 | 100% | 已提交 `69dd48c`；独立全量验证和双审查均通过 | 否 |
| 6 | 新公开 Stage02 Grasshopper 组件与 UI | ✅ 完成 | 100% | 已提交 `cd7d03e`；Core 531/531、Python 373/373、Release 0/0，规格与质量双审均 0/0/0 | 否 |
| 7 | Stage03 字段状态、Strict/Force 门禁、路径与报告 | ✅ 完成 | 100% | 已提交 `437092d`；主代理全量验证与双审查均通过 | 否 |
| 8 | IFC4 STEP 实体插入与缺失 Pset/属性转译 | 🟡 TDD 准备 | 1% | 下一步先证明 `AddEntity` 与缺失 Pset 创建合同为 RED | 否 |
| 9 | Revit 2020 全模型扫描与 Autodesk 标准 IFC4 导出 | ⬜ 未开始 | 0% | 待 Stage03 领域合同完成 | 否 |
| 10 | Stage03 协调器、新公开组件和 legacy 隐藏 | ⬜ 未开始 | 0% | 完成 RAW IFC、HIFC-MVD IFC、字段 JSON 三件套工作流 | 否 |
| 11 | 文档、CI、全量自动化与单 GHA 无备份部署 | ⬜ 未开始 | 0% | 插件目录须恰有 1 个 GHA、0 个 `.bak/.backup`，源与部署 SHA-256 一致 | 否 |
| 12 | Revit 2020 真实项目全流程验收 | ⏳ 待实机 | 0% | 使用指定 RVT 完成 Stage01→Stage02→Stage03，并核验两份 IFC 与字段报告 | **是，届时按提示点击** |

## 当前执行看板

| 项目 | 实时状态 | 当前证据 / 剩余动作 |
|---|---|---|
| 当前任务 | 🟡 Task 8 TDD 准备 | IFC4 STEP 新实体、缺失 Pset/属性/关系创建及精确回读 |
| Task 7 Git 提交 | ✅ 完成 | `437092d feat: add Stage03 validation gate and reports`；12 个功能/测试文件 |
| Task 7 领域与报告实现 | ✅ 完成 | 字段状态、Strict/Force 门禁、三件套路径、原子字段/失败报告均已实现 |
| 首轮质量返修 | ✅ 9/9 | Important 5/5；Minor 4/4；均有真实 RED 与 GREEN 证据 |
| 规格复审返修 | ✅ 5/5 | 生产 GHA 公共 resolver 已通过真实 net48 AppDomain 测试 |
| 质量复审返修 | ✅ 4/4 | Important 1/1；Minor 3/3；全部具备真实 RED/GREEN 证据 |
| Atomic 定向测试 | ✅ 通过 | 主代理 fresh 15/15 |
| Task 7 合并定向测试 | ✅ 通过 | 主代理 fresh 151/151 |
| .NET Core 全量测试 | ✅ 通过 | 主代理 fresh 682/682 |
| Python 全量测试 | ✅ 通过 | 主代理 fresh 375/375 |
| Release 构建 | ✅ 通过 | 主代理 fresh 0 warning / 0 error |
| 仓库静态审计 | ✅ 通过 | 12 文件范围、BOM、EOF、备份、绝对路径、busy-wait、残留进程均通过；DLL/GHA SHA-256 相同 |
| 规格审查 | ✅ Ready | Critical / Important / Minor = **0 / 0 / 0**；终态指纹不变 |
| 代码质量审查 | ✅ Ready | Critical / Important / Minor = **0 / 0 / 0**；终态指纹不变 |
| 下一执行 | 🟡 Task 8 | 先写 `IfcStepDocument.AddEntity` 和缺失 Pset 创建的真实 RED 测试 |
| 用户操作 | ✅ 当前不需要 | Task 11 如程序未关闭可能需配合；Task 12 再按明确提示完成实机点击 |

## Task 5 完成证据

Task 5 已完成以下后端能力：

- 当前选择、显式选择和 `ProjectInformation` 入口；
- Revit UI 可见、可编辑的共享参数绑定与写入；
- 文档身份、规则包身份、旧值快照和一次性确认校验；
- 写入失败回滚，以及与活动 GHA 同目录的原子 JSON 失败报告；
- 不在隐藏存储中保存业务属性值。

本轮已按 RED→GREEN 修复的审查问题：

1. caller / Idling 直接观察到事务终态时，也必须登记给延迟清理协调器，避免极端异常路径遗留事务组；
2. `Start` 明确返回非活动状态时，应安全释放已构造的 wrapper；若 `Start` 抛异常且状态未知，仍须保持 fail-closed。

失败报告诊断问题已全部按 RED→GREEN 修复：首次实际失败独立锁定根因阶段，后续事务清理另记清理阶段；`AggregateException` 并列子异常保持同级；未知状态、Assimilate/Dispose 首次失败，以及 rejected-start 后 Dispose 二次失败均有回归覆盖。

Task 5 的完成门槛：修复上述问题 → 定向测试 → .NET 全量测试 → Python 全量测试 → Release 构建 → 规格审查与代码审查均无 Critical/Important → 提交。

### Task 5 实时验收闸门

| 闸门 | 当前状态 | 说明 |
|---|---|---|
| 功能实现 | ✅ 完成 | 已修复终态登记、明确非活动 wrapper 清理和失败状态报告 |
| Task 5 定向测试 | ✅ 通过 | Stage02 52/52；Stage02FailureReportWriterTests 9/9 |
| .NET 全量测试 | ✅ 通过 | 主代理独立复跑 Core 500/500 |
| Python 全量测试 | ✅ 通过 | 主代理独立复跑 348/348，退出码 0 |
| Release 构建 | ✅ 通过 | 主代理独立复跑 0 warning / 0 error |
| 静态与仓库检查 | ✅ 通过 | hash、diff-check、BOM、EOF、backup、sensitive additions 均通过；生产新增 busy-wait 为 0 |
| 规格审查 | ✅ Ready | Critical / Important / Minor = 0 / 0 / 0 |
| 代码质量审查 | ✅ Ready | Critical / Important / Minor = 0 / 0 / 0 |
| Git 提交 | ✅ 完成 | `69dd48c feat: write visible HBR parameters from Stage02` |

## Task 7 完成证据与当前焦点：Task 8

Task 7 已完成并提交：未知字段状态全部 fail-closed；Strict/Force、阻断身份、活动生产 GHA 身份、统一 runId、敏感值拒绝、异常图边界、短临时文件名、全模型报告峰值内存和 CreateNew 碰撞并发竞态均已通过真实 RED→GREEN。Force 仍只放行业务缺陷，不能绕过文档身份、Revit 版本、输出冲突、IFC 导出/解析或报告写入失败。

当前焦点切换到 Task 8：扩展 IFC4 STEP 文档为可安全插入新实体，并按确定性 IFC GUID 创建或更新 `IfcPropertySingleValue`、`IfcPropertySet` 与 `IfcRelDefinesByProperties`，最后对精确 owner/Pset/property/type/value 做回读验证；任何 owner 冲突或缺失都必须显式失败，不能错误转挂到 `IfcProject`。

## 进度口径

- 只有“实现、自动化验证、审查、提交”全部通过，Task 才标为 100%。
- 测试数字只记录当前候选代码的新鲜结果；代码再次变化后，旧结果仅作为历史基线，不作为完成证明。
- Task 11 只有完成无备份的单 GHA 部署后才算完成。
- Task 12 只有在 Revit 2020 指定 RVT 上完成真实闭环后才算完成；此前不会宣称整个开发完成。
- 每次 Task 状态变化、全量验证、提交、部署或实机验收后，更新本页的日期、证据和百分比。
