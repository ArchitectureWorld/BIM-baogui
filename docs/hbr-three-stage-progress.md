# HBR 三阶段开发总进度

> 最后更新：2026-08-04 13:34（UTC+08:00；Task 8 已通过全部七道闸门并提交，正式启动 Task 9）
> 计划基线：[`2026-08-02-hbr-three-stage-implementation.md`](superpowers/plans/2026-08-02-hbr-three-stage-implementation.md)
> 当前分支：`fix/official-hifc-hardening-v090`
> 更新规则：任务启动、测试结果变化、审查结论、Git 提交、部署或实机验收后，必须同步更新本页。

## 一屏看懂

| 你最关心的项目 | 当前结果 |
|---|---|
| 整体正式完成度 | **67%：8/12 个 Task 已通过实现、验证、双审查和提交** |
| 当前开发进度 | **Task 8 已完成 7/7 闸门并提交 `d1792ee`；Task 9 已启动，尚未计入完成度** |
| 当前正在做 | **Task 9：Revit 2020 全模型扫描与 Autodesk 标准 IFC4 导出** |
| 当前运行环节 | **Task 9 合同与 RED 测试准备**：锁定 Revit API 线程、可见参数读取、IFC4 导出事务与输出验证 |
| 当前阻塞 | **无外部阻塞**；Task 9 自动化实现阶段不需要启动 Revit，实机验证后移至 Task 12 |
| 现在是否需要你操作 | **不需要**；Task 11 部署时如相关程序未关闭，可能需要配合关闭；Task 12 明确需要实机点击验收 |
| 下一节点 | Task 9 合同 RED → Revit scanner/export 最小 GREEN → 全量验证 → 规格与质量双审查 → 提交 Task 9 |

状态图例：✅ 已完整验收；🟡 开发中；🟠 有待修复风险；🔵 自动化验证中；🟣 待审查；⏳ 等待前置任务/实机；⬜ 未开始；❌ 未通过/外部阻塞。

## 总览

**已验收完成度：67%**（8/12 个 Task 已完成“实现、验证、审查、提交”全部闸门）

`█████████████▍░░░░░░ 67%`

**当前执行位置：Task 8 已完成 7/7 闸门；Task 9 已启动**（当前处于合同与 RED 测试准备阶段）

`Task 8：███████ 7/7　　Task 9：░░░░░ 0%`

| 工作包 | 包含任务 | 状态 | 完成度 |
|---|---:|---|---:|
| 统一规则与运行时基础 | Task 1–3 | ✅ 完成 | 100% |
| Stage02 属性准备 | Task 4–6 | ✅ 完成 | 100% |
| Stage03 检测、导出与转译 | Task 7–10 | 🟡 Task 7–8 完成，Task 9 开发中 | 正式完成 2/4 |
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
| 8 | IFC4 STEP 实体插入与缺失 Pset/属性转译 | ✅ 完成 | 100% | 已提交 `d1792ee`；终局双审 0/0/0，主代理 fresh Core 963/963、Python 375/375、Release 0/0 | 否 |
| 9 | Revit 2020 全模型扫描与 Autodesk 标准 IFC4 导出 | 🟡 已启动 | 0% | 正在准备合同 RED；读取 Revit 可见参数并生成/验证 RAW IFC4 | 否 |
| 10 | Stage03 协调器、新公开组件和 legacy 隐藏 | ⏳ 等待 Task 9 | 0% | 串联 RAW IFC、HIFC-MVD IFC、字段 JSON 三件套工作流 | 否 |
| 11 | 文档、CI、全量自动化与单 GHA 无备份部署 | ⏳ 等待 Task 9–10 | 0% | 插件目录须恰有 1 个 GHA、0 个 `.bak/.backup`，源与部署 SHA-256 一致 | 否 |
| 12 | Revit 2020 真实项目全流程验收 | ⏳ 等待 Task 11 | 0% | 使用指定 RVT 完成 Stage01→Stage02→Stage03，并核验两份 IFC 与字段报告 | **是，届时按提示点击** |

## 当前执行看板

| 项目 | 实时状态 | 当前证据 / 剩余动作 |
|---|---|---|
| 当前任务 | 🟡 Task 9 合同与 RED 测试准备 | Revit 2020 全模型扫描、可见参数读取、Autodesk 标准 IFC4 导出与输出验证 |
| Task 8 首轮候选（历史） | ✅ 当时形成 | 10 个获批文件；当时尚未提交，因此未计入正式完成度 |
| 上一冻结候选自动化基线 | ✅ 当时通过 | Task 8 165/165、STEP/MVD 47/47、Core 847/847、Python 375/375、Release 0/0；本轮代码变化后必须全部 fresh 重跑 |
| Task 8 跨 owner 风险 | ✅ 已修复 | token 相同或不同均创建独立 property/Pset/relation；foreign 图保持不变；两组真实 RED→GREEN |
| Task 8 首轮规格审查 | ❌ Not Ready：0/5/0 | 5 个 Important 均有代码证据和只读探针；候选冻结期间指纹未变化 |
| Task 8 规格返修 | ✅ 5/5 GREEN | 确定性、foreign 归属、Inspector fail-closed、section 状态机和全图 PSV arity 均有真实 RED/GREEN |
| Task 8 规格复审 | ✅ Ready：0/0/0 | 原 5 个 Important 全部关闭；完整规格巡检无新增 finding；候选指纹不变 |
| Task 8 首轮质量审查 | ❌ Not Ready：0/2/3 | 2 个 Important、3 个 Minor 均有代码路径和触发场景 |
| Task 8 首轮质量返修 | ✅ 5/5 GREEN | 索引性能、集中结构 invariant、旧句柄、comment lexer、Unicode X4 均有真实 RED/GREEN |
| Task 8 终局自审 | ❌ Not Ready：1/4/0 | 参数绕过序列化、尾随 comment trivia、X2 surrogate、GlobalId 索引 scope、deleted candidate commit |
| Task 8 上一轮终局返修 | ✅ 5/5 GREEN | 参数只读与注入、comment trivia、X2 surrogate、批内 GlobalId scope、deleted commit 均有真实 RED/GREEN |
| Task 8 最新正式质量复审 | ❌ Not Ready：0/2/1 | Important：完整 IfcRoot GlobalId 冲突、独立 Parse 的 `ReplaceWith` 序列化；Minor：真实枚举与 owner→Pset 性能护栏 |
| Task 8 两个 Important 返修 | ✅ GREEN | GlobalId 定向 8/8、`ReplaceWith` 定向 5/5；均已先观察真实 RED，再完成生产修复 |
| Task 8 三项质量返修 | ✅ 实现线程 GREEN | GlobalId 8/8、`ReplaceWith` 5/5、真实枚举与直接索引 3/3；另补既有 unowned Pset 多字段复用回归 |
| Task 8 返修回归 | ✅ 实现线程通过 | Task8 172/172、STEP/MVD 47/47、Core 853/853、Python 375/375、Release 0 warning / 0 error |
| Task 8 上一冻结候选 | ❌ 未通过 | binary `b75db60b77d8958534071b593a17acebeebba49b`；patch-id `bcc86db0debbd366c80f895dfd648425b19ba262` |
| Task 8 独立质量复审 | ❌ Not Ready：0/3/1 | GlobalId catalog 419/419 正确；新发现 owner 合法类型、source 结构、DATA raw statement 和 schema provenance 四项问题 |
| Task 8 四项质量返修 | ✅ 实现线程 GREEN | owner 18/18、ReplaceWith 4/4、strict DATA 4/4、provenance/drift 3/3；均先观察真实 RED |
| Task 8 返修后全量 | ✅ 实现线程通过 | Task8 174/174、STEP/MVD 47/47、Core 872/872、Python 375/375、Release 0 warning / 0 error |
| Task 8 第二轮受审候选（历史） | ❌ 未通过 | binary `885abb197f5e2442088ebcd3c9da987c61e16d7a`；patch-id `8166bb10523fbe3613f80f3a30ac73f614c28241` |
| Task 8 第二轮质量复审 | ❌ Not Ready：0/5/1 | 非法 foreign owner、PropertySetDefinitionSelect、TypeObject Pset、HasProperties 类型、STEP REAL 和错误优先级 |
| Task 8 六项质量返修 | ✅ 实现线程 GREEN | concrete owner；完整 PropertySetDefinitionSelect；TypeObject 所有权；HasProperties 类型/非空/去重；STEP REAL 规范化；SINGLE 错误优先级统一 |
| Task 8 返修后自动化 | ✅ 实现线程通过 | 定向 45/45、Mutation + Enricher 211/211、STEP/MVD 47/47、Core 909/909、Python 375/375、Release 0 warning / 0 error |
| Task 8 第三轮受审候选 | ❌ 未通过 | binary `6b8e9cece543db97cc68ba3f5517d1b2a661acc4`；patch-id `741c26c646b3a383486f6ff577d958e7519cff65`；审查期间指纹未漂移 |
| Task 8 第三轮质量复审 | ❌ Not Ready：0/2/1 | Important：Inspector 未完整验证图，且缺少 IFC4 schema/交换结构入口门槛；Minor：UUIDv5 测试缺 RFC 独立向量 |
| Task 8 第三轮质量返修 | ✅ 新增 9/9 GREEN | 图校验 5 个与 schema/结构 3 个用例先真实 RED；RFC UUIDv5 独立向量直接 GREEN；共享校验门槛已实现 |
| Task 8 第三轮返修回归 | ✅ 实现线程通过 | Task8 236/236、Mutation + Enricher 219/219、STEP/MVD 111/111、Core 918/918、Python 375/375、Release 0 warning / 0 error |
| Task 8 第四轮受审候选 | ❌ 未通过 | binary `7ae2a9c8a11ae09308107023094a27bb21aa1043`；patch-id `cf0b8e7fee538e9c550cb4fd2921251d4a22cee4`；审查期间指纹未漂移 |
| Task 8 第四轮质量复审 | ❌ Not Ready：0/3/1 | Important：value 合同/优先级、mixed DefinitionSet、surrogate 异常；Minor：standalone Inspector 359 字段产生 1795 次实体全枚举 |
| Task 8 第四轮质量返修 | ✅ 新增 15/15 GREEN | A 5/5、B 1/1、C 8/8 真实行为 RED；D 缺 `InspectMany` 编译 RED；共享合同、目标化 mixed set、surrogate 映射和批量入口已实现 |
| Task 8 第四轮返修回归 | ✅ 实现线程通过 | Task8 251/251、Mutation + Enricher 234/234、STEP/MVD 111/111、Core 933/933、Python 375/375、Release 0 warning / 0 error |
| Task 8 第五轮受审候选 | ❌ 未通过 | binary `9ff5f43d2d76961060cfe71ec214c874d606742b`；patch-id `cdd3063f692e0881e504860a1e148a0964207bda`；审查期间指纹未漂移 |
| Task 8 第五轮质量复审 | ❌ Not Ready：0/1/1 | Important：空批绕过文档门槛且无法表达顶层失败；Minor：批量字段结果缺 PropertyIdentity |
| Task 8 第五轮质量返修 | ✅ 定向 10/10 GREEN | 批量顶层 Success/Error/Fields 与 PropertyIdentity 已实现；合法/非法空批、全局/局部失败、null/duplicate 和 359 字段性能均覆盖 |
| Task 8 第五轮返修回归 | ✅ 实现线程通过 | 新增 9/9、Task8 260/260、Mutation + Enricher 243/243、STEP/MVD 111/111、Core 942/942、Python 375/375、Release 0 warning / 0 error |
| Task 8 第六轮受审候选 | ❌ 未达严格门槛 | binary `e4d7ffadf7604cc6208c33eea5ab45307eed6f7a`；patch-id `a07ef07834f88155fa5ead623a6528432ac5d5eb`；审查期间指纹未漂移 |
| Task 8 第六轮质量复审 | ❌ Ready with follow-ups：0/0/2 | Minor：batch/field DTO 可变且 Fields 可下转型数组；Apply 后置回读两条构造路径缺 PropertyIdentity |
| Task 8 第六轮质量返修 | ✅ 新增 21/21 GREEN | DTO 唯一构造/get-only、防御复制、null/矛盾状态拒绝及 Apply 后置回读 12 条路径 identity 传播均已实现 |
| Task 8 第六轮返修回归 | ✅ 实现线程通过 | 新增 21/21、Task8 281/281、Mutation + Enricher 264/264、STEP/MVD 111/111、Core 963/963、Python 375/375、Release 0 warning / 0 error |
| Task 8 终局受审候选 | ✅ 10 文件、只读受审 | binary `465980585050e961c1c512e696593fea65fd9337`；patch-id `6e5c24a389e713bc14c747ee3549ea8ed2a51d44`；唯一 unstaged 文件为本看板 |
| Task 8 终局质量复审 | ✅ Ready：0/0/0 | fresh 独立 TEMP 64 项通过；两项 Minor 全部关闭；终局指纹无漂移 |
| Task 8 主代理全量复验 | ✅ 通过 | Task8 281/281、Mutation + Enricher 264/264、STEP/MVD 111/111、Core 963/963、Python 375/375、Release 0/0；全部 fresh |
| Task 8 静态与产物审计 | ✅ 通过 | 10 文件范围、diff、BOM、EOF、backup、绝对路径、敏感信息、busy-wait、残留进程全通过；DLL/GHA SHA-256 相同 |
| Task 8 Git 提交 | ✅ 完成 | `d1792ee feat: create missing HBR properties in IFC4`；恰好 10 个功能/测试文件，看板未混入 |
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
| Task 7 规格审查 | ✅ Ready | Critical / Important / Minor = **0 / 0 / 0**；终态指纹不变 |
| Task 7 代码质量审查 | ✅ Ready | Critical / Important / Minor = **0 / 0 / 0**；终态指纹不变 |
| 下一执行 | 🟡 Task 9 | 先写 Revit API 线程、参数 GUID、IFC4 事务与输出验证合同 RED，再实现 scanner/export 最小 GREEN |
| 用户操作 | ✅ 当前不需要 | Task 11 如程序未关闭可能需配合；Task 12 再按明确提示完成实机点击 |

### Task 8 七道验收闸门

| 闸门 | 状态 | 完成标准 |
|---:|---|---|
| 1. 合同与边界梳理 | ✅ 完成 | 明确事务性、幂等、精确 owner/Pset/property 图与七类 typed value |
| 2. 首轮实现与定向测试 | ✅ 完成 | 候选代码形成；主代理 fresh 定向测试 96/96 |
| 3. 已知风险修复 | ✅ 完成 | foreign Pset 在 token 相同和不同两种情况下均隔离创建；原关系不变 |
| 4. 规格审查 | ✅ 完成 | 首轮 0 / 5 / 0；五项 GREEN 后复审 0 / 0 / 0 Ready |
| 5. 代码质量审查 | ✅ 完成 | 终局独立 fresh 复审 Critical / Important / Minor = 0 / 0 / 0 Ready |
| 6. 独立全量复验 | ✅ 完成 | 主代理 fresh 定向、旧回归、Core、Python、Release 和仓库静态审计全部通过 |
| 7. Git 提交与看板更新 | ✅ 完成 | `d1792ee feat: create missing HBR properties in IFC4`；正式完成度更新为 8/12 |

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

## Task 7–8 完成证据与当前焦点：Task 9

Task 7 已完成并提交：未知字段状态全部 fail-closed；Strict/Force、阻断身份、活动生产 GHA 身份、统一 runId、敏感值拒绝、异常图边界、短临时文件名、全模型报告峰值内存和 CreateNew 碰撞并发竞态均已通过真实 RED→GREEN。Force 仍只放行业务缺陷，不能绕过文档身份、Revit 版本、输出冲突、IFC 导出/解析或报告写入失败。

Task 8 已完成并提交：终局规格与质量复审均为 0/0/0；主代理 fresh 复跑 Task8 281/281、Mutation + Enricher 264/264、STEP/MVD 111/111、Core 963/963、Python 375/375、Release 0 warning / 0 error，静态审计与 DLL/GHA 哈希一致性全部通过。代码提交为 `d1792ee feat: create missing HBR properties in IFC4`。

当前焦点转为 Task 9：在 Revit host context 中扫描 ProjectInformation、Level、Room、Area 及规则类别元素，按固定共享参数 GUID 读取可见值并转换为 canonical 外部单位；随后使用 Autodesk Revit 2020 标准 `IFCExportOptions` 明确导出 IFC4，验证事务状态、目标不存在、输出文件存在且非空。当前先写合同 RED，不部署 GHA、不启动 Revit，实机点击与真实 RVT 验收仍保留到 Task 12。

## 进度口径

- 只有“实现、自动化验证、审查、提交”全部通过，Task 才标为 100%。
- 测试数字只记录当前候选代码的新鲜结果；代码再次变化后，旧结果仅作为历史基线，不作为完成证明。
- Task 11 只有完成无备份的单 GHA 部署后才算完成。
- Task 12 只有在 Revit 2020 指定 RVT 上完成真实闭环后才算完成；此前不会宣称整个开发完成。
- 每次 Task 状态变化、全量验证、提交、部署或实机验收后，更新本页的日期、证据和百分比。
