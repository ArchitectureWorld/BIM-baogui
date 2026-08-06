# HBR 三阶段开发总进度

> 最后更新：2026-08-06 13:38（UTC+08:00；Stage03 相对输出目录 finding 已 RED→GREEN，等待远端 CI artifact 与无备份部署）
> 计划基线：[`2026-08-02-hbr-three-stage-implementation.md`](superpowers/plans/2026-08-02-hbr-three-stage-implementation.md)
> 当前分支：`fix/official-hifc-hardening-v090`
> 更新规则：任务启动、测试结果变化、审查结论、Git 提交、部署或实机验收后，必须同步更新本页。
> 实时口径：本页与 Codex 计划面板同步；只有实现、验证、审查、提交全部通过的 Task 才计入正式完成度。

## 一屏看懂

| 你最关心的项目 | 当前结果 |
|---|---|
| 整体正式完成度 | **83%：10/12 个 Task 已通过实现、验证、双审查和提交** |
| 现在能否直接作为最终版使用 | **还不能**：尚未完成 Task 11 无备份部署和 Task 12 Revit 2020 指定 RVT 实机闭环 |
| 当前开发进度 | **Task 1–10 已完成；Task 11 已完成审查与提交，内部进度 4/5** |
| 当前正在做 | **Task 11：等待 GitHub Actions clean runner 产出并核验 v0.9.0 artifact** |
| 当前运行环节 | **修复后 Core/Python/Release/NuGet/静态全部通过；提交 `5152f0d` + `05b48ba` 已形成，尚未部署** |
| 当前阻塞 | **无代码阻塞**；远端 CI 与单 GHA 无备份部署仍是 Task 11 的必要闸门 |
| 现在是否需要你操作 | **不需要**；Task 11 部署时如相关程序未关闭，可能需要配合关闭；Task 12 明确需要实机点击验收 |
| 下一节点 | 推送 `05b48ba` → clean runner → artifact manifest 核验 → 单 GHA 无备份部署 |

## Grasshopper 最终使用路径

| 顺序 | GH 公开组件 | 用户在 GH 中能看到什么 | 当前开发状态 |
|---:|---|---|---|
| 1 | `01 项目初始化` | 项目身份、坐标 X/Y、高程、真北、文件上下文与规则包身份 | ✅ 自动化完成；⏳ Task 12 复核既有效果不回归 |
| 2 | `02 构件与属性准备` | 选择/匹配结果、完整字段明细、建议值、阻断原因、待安装/写入计数、写入状态和失败报告路径 | ✅ exactly-once 与异常终态自动化已闭环；⏳ 待 Task 12 实机复核 |
| 3 | `03 检测、导出与转译` | Strict/Force 模式、字段通过与阻断计数、RAW IFC、HIFC-MVD IFC、fields JSON、失败报告和总状态 | ✅ Strict/Force 开关、条件化验收与 X/Y/高程证据槽已闭环；⏳ 待部署和实机验收 |

### Stage03 测试开关

| `全部通过才导出` | 实际模式 | 导出规则 |
|---|---|---|
| `true`（默认） | Strict | 任一活动业务阻断存在时不导出，只输出字段检测证据 |
| `false` | Force | 必须填写非空“强制原因”；可绕过业务阻断以便测试并生成两份 IFC |

无论开关取值如何，错文档、Revit 版本不支持、输出冲突、IFC 导出/解析失败、报告写入失败等技术故障都不能被绕过。卡片只提供摘要，完整字段、阻断和产物路径同时通过 GH 输出端口提供。

状态图例：✅ 已完整验收；🟡 开发中；🟠 有待修复风险；🔵 自动化验证中；🟣 待审查；⏳ 等待前置任务/实机；⬜ 未开始；❌ 未通过/外部阻塞。

## 总览

**已验收完成度：83%**（10/12 个 Task 已完成“实现、验证、审查、提交”全部闸门）

`█████████████████░░░ 83%`

**当前执行位置：Task 10 已完成 7/7 闸门；Task 11 已通过前4道闸门，等待第5道无备份部署**

`Task 10：███████ 7/7　　Task 11：████░ 4/5（CI artifact / 部署待执行）`

| 工作包 | 包含任务 | 状态 | 完成度 |
|---|---:|---|---:|
| 统一规则与运行时基础 | Task 1–3 | ✅ 完成 | 100% |
| Stage02 属性准备 | Task 4–6 | ✅ 完成 | 100% |
| Stage03 检测、导出与转译 | Task 7–10 | ✅ 完成 | 100% |
| CI、文档与单 GHA 部署 | Task 11 | 🔵 等待 CI artifact / 部署 | 正式0%，内部4/5 |
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
| 9 | Revit 2020 全模型扫描与 Autodesk 标准 IFC4 导出 | ✅ 完成 | 100% | 已提交 `b56100a`；双审 0/0/0，主代理定向/全量/Release/静态/指纹全部通过 | 否 |
| 10 | Stage03 协调器、新公开组件和 legacy 隐藏 | ✅ 完成 | 100% | 已提交 `4890e07`；16文件manifest `243858f4…77e04`，规格/质量0/0/0，主代理全量通过 | 否 |
| 11 | 文档、CI、全量自动化与单 GHA 无备份部署 | 🔵 等待 CI artifact / 部署 | 正式0%，内部4/5 | 本地规格/质量0/0/0；Core1242/1242、Python516/516、严格Release0/0、NuGet0漏洞；已提交 `5152f0d` + `05b48ba`，待远端 artifact 与无备份部署 | 否 |
| 12 | Revit 2020 真实项目全流程验收 | ⏳ 等待 Task 11 | 0% | 使用指定 RVT 完成 Stage01→Stage02→Stage03，并核验两份 IFC 与字段报告 | **是，届时按提示点击** |

### Task 9 七道验收闸门

| 闸门 | 状态 | 最新证据 |
|---:|---|---|
| 1. 功能实现与 TDD 返修 | ✅ 完成 | 34 个功能/测试文件冻结；全部历史 finding 已按 RED→GREEN 关闭 |
| 2. 规格审查 | ✅ Ready | Critical / Important / Minor = 0 / 0 / 0 |
| 3. 代码质量审查 | ✅ Ready | Critical / Important / Minor = 0 / 0 / 0；起止 manifest 无漂移 |
| 4. 主代理定向复验 | ✅ 通过 | Task9 xUnit 126/126；Task9 Python 17/17 |
| 5. 主代理全量复验 | ✅ 通过 | Core 1089/1089；Python 392/392；Release 0 warning / 0 error |
| 6. 静态、产物与候选指纹 | ✅ 通过 | 34 文件、staged=0、diff-check clean；无 backup/绝对路径/敏感信息；DLL/GHA 与 manifest 精确命中 |
| 7. Git 提交与看板更新 | ✅ 完成 | `b56100a feat: scan Revit and export Autodesk IFC4`；恰好 34 个文件，看板保持独立提交 |

### Task 10 七道验收闸门

| 闸门 | 状态 | 完成标准 |
|---:|---|---|
| 1. 协调器与公开组件 TDD 实现 | ✅ 完成 | backend、真实 translator、生产 adapter、新组件/UI 与 legacy hidden 均已 GREEN |
| 2. 定向与回归测试 | ✅ fresh 全通过 | 最终定向78/78、Stage03/MVD348/348、Core1167/1167、Python401/401、component9/9、Release0/0 |
| 3. 候选冻结与静态审计 | ✅ 完成 | 16文件、2046-byte最终manifest `243858f4…77e04`；BOM0、禁止项0、DLL/GHA哈希一致 |
| 4. 规格审查 | ✅ Ready：0/0/0 | 首轮0/1/0；Force finding 真实 RED→GREEN 后独立复审确认精确关闭，冻结指纹无漂移 |
| 5. 代码质量审查 | ✅ Ready：0/0/0 | 首轮0/2/0；后台化与失败根因传播分别RED→GREEN后，独立复审确认全部关闭且冻结无漂移 |
| 6. 主代理独立全量复验 | ✅ 完成 | 定向78/78、Stage03/MVD348/348、Core1167/1167、Python401/401、component9/9、Release0/0、静态/manifest通过 |
| 7. Git 提交与看板更新 | ✅ 完成 | `4890e07 feat: complete Stage03 validation export workflow`；恰好16文件，提交manifest不变，看板独立提交 |

### Task 11 五道验收闸门

| 闸门 | 状态 | 完成标准 |
|---:|---|---|
| 1. 三阶段文档/CI合同 TDD | ✅ RED→GREEN完成 | 6 failed/9 passed真实RED→15/15 GREEN；README仅3组件，checklist为指定RVT实机表，CI合同已更新 |
| 2. 完整自动化与安全审计 | ✅ 第六版 fresh 通过 | Core连续4轮各1242/1242；Python515/515；严格Release 0 warning/0 error；NuGet 1项目/1框架/0漏洞；diff/BOM/CRLF/备份门禁通过 |
| 3. Task 11 整体验收审查 | ✅ 0/0/0 | 当前候选以 `90dada31…62079` 为精确 manifest；本地规格/质量复审与 fresh 全量验证均通过 |
| 4. Git 提交与看板更新 | ✅ 候选已提交 | 62 个获批文件提交为 `5152f0d`，相对路径修复追加为 `05b48ba`；本看板保持独立提交 |
| 5. 单 GHA 无备份部署 | ⬜ 待执行 | 相关宿主进程已关闭；插件目录恰好1个GHA、0个备份，源/部署SHA-256一致 |

### Task 11 当前 6 项返修清单

| ID | 来源 | 必须修复/补齐的内容 | 当前状态 | 关闭门槛 |
|---|---|---|---|---|
| Q1 | 代码质量 | Stage02 首次 completion 消费者异常进入独立、exactly-once 的技术失败终结路径，不得吞异常或二次执行业务 completion | ✅ RED→GREEN | 行为1/1、接线合同1/1、相关C#47/47；业务completion一次、技术终态/报告/刷新各一次，Pending清零且不重试业务回调 |
| Q2 | 代码质量 | RevitHost 增加 callback-start/executed gate；业务回调已开始后禁止 fallback 到 legacy 队列 | ✅ RED→GREEN | 完整RevitHost路由2失败/1通过→3/3；相关C#17/17、Python122/122，入口失败可fallback，callback开始后异常传播且legacy=0 |
| S1 | 规格 | `STRICT_CLEAN_EXPORT` 改为“权威分类完成后才适用”的条件场景；当前 v0.9.0 强制验收保留 Strict blocked 与三类 Force 场景 | ✅ RED→GREEN | 合同0/1→1/1、相关4/4；明确359/359均UNCLASSIFIED、不得伪造分类，Strict clean条件模板不阻塞当前诚实验收 |
| S2 | 规格 | Stage02 清单加入项目信息及实例/类型哨兵编辑、保存重开回读、切换 RVT 后旧预览失效且不能确认写入 | ✅ RED→GREEN | 合同0/1→1/1、相关5/5；锁定两个真实GUID及固定哨兵、保存重开截图/哈希、RVT身份切换、结果过期且写入队列未进入证据槽 |
| S3 | 规格 | Stage03 清单加入 X/Y/高程的 Revit GUID、显示值、单位与 final IFC 实体/Pset/属性/类型/值对照，以及源 RVT 前后 SHA-256 | ✅ RED→GREEN | 合同初次0/1→1/1，边界复核后二次0/1→相关7/7；两导出场景均锁定3个真实GUID与final IFC身份/类型/值，记录Stage03执行前后源RVT及RAW转译前后哈希 |
| S4 | 规格/CI | Release 构建启用 warnings-as-errors，并以发布合同测试防止回退 | ✅ 规格复审 Ready：0/0/0 | 唯一严格 build、NoWarn、默认失败传播、PowerShell 常规调用、YAML folded scalar 与 `dotnet --% build` 边界全部闭环；实际 workflow 无需修改 |
| S4 第三轮范围 | 规格/CI | 替代规格重审的 PowerShell/dotnet 调用边界 | ✅ 已 RED→GREEN，但后续复审发现新边界 | 三枚反引号续行、`&`、`dotnet.exe` 变异已关闭；其证据仅代表第三轮范围 |
| S4 第四轮范围 | 规格/CI | YAML `>` / `>-` folded scalar 与 PowerShell `dotnet --% build` 调用边界 | ✅ 独立复审 Ready：0/0/0 | 三枚精确变异3/3、上一轮PowerShell三案3/3、历史16类16/16均被独立拒绝；发布合同39/39、CI22/22、真实Release0警告/0错误 |

## 当前执行看板（最新状态）

| 项目 | 实时状态 | 当前证据 / 剩余动作 |
|---|---|---|
| 当前任务 | 🔵 Task 11 等待 CI artifact / 部署 | 正式完成度10/12（83%）；代码已修复并提交，Task 11 仍须完成远端 artifact 核验和无备份部署 |
| 根因与修复 | ✅ 行为 RED→GREEN | Stage03 预占目标原抛普通 `0x80131620` 并在首候选终止；现统一为可分类 `0x800700B7`，并以固定 GUID 验证“文件占用→目录占用→第三候选成功” |
| 旧第三版候选 | ❌ 已作废 | 60 文件 manifest `a42f48d5…ca0d` 与 GHA/DLL `2db105fc…9ea3c` 均不再代表当前代码，不得提交或部署 |
| 第四版候选 | ❌ 已作废 | 60文件 manifest `766f2191…b3fac` 与 GHA/DLL `4ce3a01c…6ec80` 不得提交或部署 |
| 代码质量终审 | ✅ Ready：0/0/0 | writer、NoWarn/job flow、MSBuild/import/analyzer config/paths全部RED→GREEN；当前候选无新增 finding |
| 规格终审 | ✅ 第五版 Ready：0/0/0 | folded/`--%`三案、反引号/`&`/`dotnet.exe`三案及历史16类变异全部独立拒绝；候选起止摘要一致 |
| 验证证据 | ✅ 修复后 fresh 通过 | Stage03合同15/15；Core1242/1242；Python516/516；发布合同97/97；Release0/0；NuGet0漏洞；62文件manifest=`e07499b1…c8475ea2`；GHA/DLL=`35f746a0…96a6c` |
| 下一动作 | 🔵 正在执行 | 推送 `05b48ba` → clean runner → 下载并核验 artifact manifest → 单 GHA 无备份部署 |
| 用户操作 | ⏳ 当前不需要 | 仅当部署时宿主仍打开需协助关闭；Task 12 再按提示进行 Revit 2020 实机点击 |

## 详细审计流水（历史记录，不代表当前阻塞）

下表保留每次 RED、返修和复审证据。历史 `❌ Not Ready` 已由其后的 `✅ GREEN/Ready` 项闭环；判断当前状态请以上方“最新状态”为准。

| 项目 | 历史状态 | 证据 |
|---|---|---|
| 质量 I1：Stage02 写失败报告身份 | ✅ RED→GREEN | identity 1/1、真实FS finalizer 1/1、集成1/1；回归C#13/13、Python30/30。current=`PUBLISHED_CURRENT`，stale=`DISCARDED_STALE`，ignored零文件 |
| 质量 I2：Stage02/03 验收证据关联 | ✅ RED→GREEN | Task12定向4/4；Stage02按7项原生身份/时间窗关联且无runId，Stage03仍绑定runId；三类坏变异与fatal N/A变异均被拒绝 |
| 质量 I3：preview含业务阻断的类型 | ✅ RED→GREEN | OperationResults 7/7；blockers只物化一次并优先判`BusinessBlocked`，保留preview、Success=false，且不生成技术失败报告 |
| 质量 M1：活动文档不可用分类 | ✅ RED→GREEN | Core seam1/1、生产接线1/1；false+null异常统一BusinessBlocked/no-report，真实异常统一TechnicalFailure/report |
| 质量 M2：null selection 受控失败 | ✅ RED→GREEN | 两条精确0/1→1/1；`SelectionNoResult`可报告、解引用前分类且清pending；合并定向C#22/22、Python108/108 |
| 原质量复审3I+2M返修 | ✅ 全部GREEN | I1–I3、M1–M2均具真实RED与定向GREEN；仍须连同GH 6I+3M一起冻结、复审和全量验证 |
| GH I1：技术Preview失败可辨识 | ✅ 0/1→1/1 | 新增“预览技术失败”总状态并映射Error；业务blocker仍为PreviewBlocked，卡片和状态端口不再混淆 |
| GH I2：写入请求入队失败报告 | ✅ RED→GREEN | Core1/1、Python1/1；throw保留原异常，false受控异常，报告含WRITE_ENQUEUE及request身份，current/stale/ignored分类与路径发布完整 |
| GH I3：Stage02字段证据完整 | ✅ 0/1→1/1 | 字段JSON现19个稳定键，并以documentFingerprint/documentTitle开头，Panel可直接绑定具体RVT |
| GH M1：Stage02卡片匹配角色 | ✅ 0/1→1/1 | MatchedRoles按ordinal去重排序并显示，GH重算不改变角色摘要顺序 |
| GH M2：Stage01公开菜单名含01 | ✅ GREEN 4/4 | 组件、README和Task12统一“湖北BIM报规｜01 文件初始化”，legacy GUID/隐藏状态不变 |
| GH I4：Stage03完整字段Data Tree | ✅ RED→GREEN 3/3 | 已含合同/适用性/参数身份、全分层状态、Revit三值、active/blocker与稳定messages，并保留IFC原证据 |
| GH I5：全部阻断保持纯语义 | ✅ RED→GREEN | C#12/12、Python1/1；只含业务阻断、typed fatal和blocking diagnostics，INFO/WARNING/普通messages排除，Force缺陷保留 |
| GH I6：Stage03端口通道 | ✅ 0/1→1/1 | 外框934px、内卡620×338仅X内缩；Bounds与真实端口按outer box布局，卡片不覆盖通道 |
| GH M3：Force呈现 | ✅ RED→GREEN | Core20/20、Python2/2；精确Strict/Force文案，Force为Warning、Force+缺陷橙色，仅无阻断Strict allow为Success绿色 |
| 本批质量与GH返修汇总 | 🟠 复审finding返修 | 自动验证基线全过，但只读复审发现pending分支仍把“请求已提交”普通消息放入全部阻断；修复并重跑前不冻结 |
| GH I5复审增量：pending污染 | ✅ 0/1→1/1 | 签名变化与健康pending均不写普通blockers；进度只在Status/footer；端口说明仅列三类真实阻断 |
| 复审Minor 1：分隔符无歧义 | ✅ RED→GREEN | 碰撞1/1、formatter13/13；字段与各类阻断均为确定性JSON object，35键对齐报告，分隔符逐值round-trip |
| 复审Minor 2：阻断策略边界 | ✅ RED→GREEN | Python1/1、C#22/22；独立policy文件，formatter/coordinator只调用；null/trim/case/8 fatal/反例全锁 |
| 二次复审唯一I：异常路径JSON | ✅ RED→GREEN | Python1/1、Core round-trip1/1；共享FormatComponentFailure复用统一formatter，三条异常路径均稳定JSON |
| 第三次全量回归与只读复审 | ✅ 复审0/0/0 | Stage03 Core340/340、GH14/14、Core1220/1220、Python439/439；继续Release/diff/text/hash/身份审计后冻结 |
| Task11最终候选冻结 | ✅ 已冻结 | 排除本看板后48文件、6011-byte Ordinal/LF manifest `1476f86c…6062`；HEAD `21549ed`、staged=0 |
| Task11正式规格复审 | ❌ Not Ready：0/4/1 | 4I：Stage03 fatal报告、Force空原因证据槽、NuGet空framework放行、Stage02 host callback丢完成回调；1M：Selection分类不一致。绝对RVT路径finding已撤销 |
| 规格I1：Stage03 typed fatal报告 | ✅ 0/1→1/1 | 原FailureReportCalls=0；现保留fields并生成同runId failure report，Force仍不导出，Strict纯业务路径不变 |
| 规格I2：Force空原因可审计运行 | ✅ RED→GREEN 2/2 | 删除重复TryBegin早拒绝；正式gate仍阻断，Scan/fields各1、同runId、Export/Translate=0，签名/边沿不变 |
| 规格I3：NuGet空framework fail-closed | ✅ 3失败/2通过→5/5 | 双报告：inventory校验项目/framework，vulnerable专查漏洞；真实YAML gate执行`NUGET_GATE_OK`，健康零package不误杀 |
| 规格I4：Stage02 post-enqueue callback failure | ✅ RED→GREEN | 三参数EnqueueAction+exactly-once；补writer白名单与冻结DocumentTitle；完整身份/Exception保留；Core70/70、Python88/88 |
| 规格M1：Selection宿主不可用分类 | ✅ RED→GREEN | 四个core走typed helper；null=>BusinessBlocked/no-report，真实异常=>TechnicalFailure/Same/report；Core70/70、Python88/88 |
| 规格4I+1M返修汇总 | 🔵 静态/manifest收尾 | 规格Core152/152、NuGet变异5/5、Stage02 Py88/88、Core1229/1229、Python444/444、Release0/0、实际gate`NUGET_GATE_OK` |
| 规格返修后新候选冻结 | ✅ 已冻结 | 排除看板49文件、6153-byte Ordinal/LF manifest `0d0f5a8b…e185`；GHA/DLL 1,546,752 bytes、SHA `1f1b9b4b…bff7`一致，staged=0 |
| 规格返修后正式复审 | ❌ Not Ready：0/1/1 | I：Force空原因fields路径/哈希仍允许N/A；M：扫描全Pass时GH全部阻断缺稳定Force原因JSON。其余原4I/1M关闭，manifest无漂移 |
| 复审新增I：Force空原因fields证据 | ✅ RED→GREEN | 清单强制独立同runId fields路径/SHA，不得N/A；定向1/1、Task12相关6/6 |
| 复审新增M：FORCE_REASON_REQUIRED | ✅ 4/4 GREEN | gate/formatter/clean workflow/fields snapshot全链保存typed blocker，普通Messages仍不混入AllBlockers |
| GH 三阶段可用性独立审查 | ❌ Not Ready：0/6/3 | 核心安全语义通过；需补技术失败辨识/报告、Stage02/03字段证据、纯阻断输出、端口通道、角色摘要、01菜单名与Force测试放行文案/状态色 |
| 部署前只读基线 | ✅ 可进入后续部署闸门 | 插件目录现有1个GHA、0个`.bak/.backup`；当前未发现Revit/Rhino/Grasshopper主进程。现有GHA仍是旧版，候选未提前覆盖 |
| Stage02 预览失败报告返修 | ✅ 首轮与定向回归 GREEN | 首轮C# 43/43、Python 27/27；定向回归C# 55/55、Python 79/79；继续纳入最终全量复验与独立复审 |
| Stage03 GH 展示独立审查 | ✅ Ready：0/0/0 | 5入8出、默认Strict、Force原因、上升沿、失效签名、卡片计数/路径与技术fatal均覆盖；组件合同9/9 |
| Task 11 文档/CI合同 | ✅ RED→GREEN完成 | 6 failed/9 passed→15/15；README/验收清单/workflow已按三阶段当前产品路径更新 |
| Task 11 完整自动化 | ✅ fresh全通过 | Task11+兼容28/28、rulepack159/159、Python413/413、Core1167/1167、Release0/0、workflow YAML/diff/static通过，NuGet 1项目/0已知漏洞 |
| Task 11 兼容合同清理 | ✅ 完成 | 第6文件确认legacy Stage04 GUID唯一且hidden；当前README/checklist不再公开Stage04，目标联跑19/19 |
| Task 11 候选冻结 | ✅ 完成 | 6 files、+266/-122、613-byte manifest `9f07a0e3d748b30b24ea9529135fd336fe2bb636f5af296697ce4d50ca3be566`；staged=0 |
| Task 11 首轮规格审查 | ❌ Not Ready：0/4/0 | CI 缺jsonschema、diff-check、vulnerable fail gate；GH Toggle 接线语义与 Task12 count/failure/owner 证据槽位不完整 |
| Task 11 规格返修 | 🔵 全量 GREEN，冻结中 | 5/19 RED → 目标25/25、rulepack159/159、Python410/410、Core1167/1167、Release0/0；YAML/diff/committed gate/实际 NuGet scan 全通过 |
| Task 11 二轮候选冻结 | ✅ 完成 | 6 files、+557/-122、staged=0；613-byte manifest `a6ed73ed1a82f60981fd70e27c562c7c5dd58668c16d668058500b5c06bacda9` |
| Task 11 第二轮规格复审 | ❌ Not Ready：0/3/0 | clean runner restore 在 Python 测试之后；空 projects 数组误放行；新分支首次多提交 push 只检查 HEAD^…HEAD |
| Task 11 第三轮返修 | 🔵 全量 GREEN，冻结中 | Task11+兼容28/28、rulepack159/159、Python413/413、Core1167/1167、NuGet1项目/0漏洞、Release0/0，YAML/diff/static全通过 |
| Task 11 第三轮候选冻结 | ✅ 完成 | 6 files、+646/-125、staged=0；613-byte manifest `90838b04cb2f355776b759331773443936dae05421f677858ca24de06a91e670` |
| Task 11 第三轮规格复审 | ✅ 完成 | 原独立规格审查员只读复审全部历史finding、追加paths及规格1–9；看板排除在候选外 |
| Task 11 第三轮规格复审结论 | ✅ Ready：0/0/0 | 原7项finding、追加paths与规格1–9全部关闭；起止manifest一致 |
| Task 11 质量与 GH 可用性审查 | 🟣 基线0/4/4，返修已GREEN | 4I+4M全部具备真实RED→GREEN；相关终审C#113/113、Python137/137、CR offender 0，等待完整复审 |
| Task 11 当前候选冻结 | ✅ 完成 | 排除本看板后27文件、3158-byte Ordinal/LF manifest `52f6cb5c…57f8`；HEAD `21549ed`、staged=0、diff-check clean |
| Task 11 完整规格复审 | ❌ Not Ready：0/3/1 | 3I：选择技术异常漏报、预览业务/技术误分流、五场景未分Stage02/03报告；1M：.gitattributes未自触发/自校验 |
| Task 11 规格finding返修 | ✅ RED→GREEN | I1 C#4/4+Py4/4；I2 C#3/3+Py2/2；I3 1/1；M1 3/3；相关回归C#27/27、Python102/102 |
| Task 11 新候选冻结 | ✅ 完成 | 排除本看板后36文件、4275-byte Ordinal/LF manifest `1529c916…8723`；CR offender 0、staged=0、diff-check clean |
| Task 11 第二轮规格复审 | ❌ Not Ready：0/1/0 | Selection/Preview与正文均通过；唯一I为fatal Stage03报告“禁止N/A”坏变异仍被合同放行 |
| Task 11 二轮finding返修 | ✅ RED→GREEN | fatal“允许N/A”纯内存坏变异0/1→1/1；Task12定向2/2、发布合同22/22；正文未改 |
| Task 11 第三轮候选冻结 | ✅ 完成 | 36文件、4275-byte Ordinal/LF manifest `e72511aa…c95c`；staged=0、diff-check clean |
| Task 11 第三轮规格复审 | ✅ Ready：0/0/0 | 规格1–9静态回归166/166；fatal path/SHA三种坏变异均拒绝；起止manifest一致 |
| Task 11 完整代码质量复审 | ❌ Not Ready：0/3/2 | 3I：写失败身份/迟到报告、Stage02不可能同Stage03 runId、preview blockers误Success；2M：宿主状态分类、null selection |
| Task 11 质量审查验证基线 | ✅ fresh通过但不足以发布 | Python426/426、Core1178/1178、Release0/0、NuGet0漏洞、YAML/diff/CR/BOM/EOF通过，manifest无漂移 |
| Task 11 第五版规格复审 | ❌ Not Ready：0/1/0 | Q1/Q2/S1–S3通过；唯一Important为S4只检查固定名称build step，放行continue-on-error、不同名二次弱build/NoWarn与always上传旧产物 |
| S4 防绕过复审增量 | ✅ RED→GREEN，待独立重审 | 10类流程/后续build坏变异在旧helper上0/1 RED；全workflow唯一生产build、严格step与Verify→Prepare→唯一Upload默认失败传播闭环后，主合同+全部变异3/3、发布合同35/35、相关CI6/6、Release0/0 |
| S4 PowerShell/YAML 边界返修 | ✅ 四轮RED→GREEN | 依次关闭改名弱build、continue/always、反引号、`&`、`dotnet.exe`、folded scalar与`--%`边界；最终发布合同39/39、CI22/22、Release0/0 |
| Task 11 第五版规格终审 | ✅ Ready：0/0/0 | 独立重放folded/`--%`3/3、PowerShell常规调用3/3、历史16/16；排除看板62文件候选起止SHA `547096ab…3269d2`一致，diff clean、staged0 |
| Task 10 backend TDD | ✅ 实现线程 GREEN | RED：缺 9 个生产类型；GREEN：协调器 18/18、相关回归 379/379、Release 0/0、diff-check clean |
| Task 10 backend 首轮规格审查 | ❌ Not Ready：0/5/2 | TEMP 5 个探针全部复现；协调器官方 18/18 和 Stage03 244/244 仍通过，但不足以证明对抗 seam 变异 |
| Task 10 backend 首轮返修 | ✅ 定向 GREEN | Coordinator 41/41、Stage03/MVD 311/311、Core 1130/1130、Release 0/0；manifest `29c204b4…122c84` |
| Task 10 backend 返修自审 | ❌ 当时 Not Ready：0/2/0 | writer 可改权威对象、RAW inspection/fatal diagnostics 不完整；随后均按真实 0/4 RED→4/4 GREEN 返修 |
| Task 10 backend 二次返修 | ✅ fresh 验证通过 | Coordinator45、Stage03/MVD315、Core1134、Release0/0；禁止项0；组件预期0/3 RED |
| Task 10 backend 当前冻结 | ✅ 稳定 | 3 文件 manifest `1ecc0e8eb54ff29360191ad64b5b6c49c593595efd7e686e04cae805b9365a5a`；起止重算一致、staged=0 |
| Task 10 backend 正式规格复审 | ❌ Not Ready：0/2/1 | 空洞Pass/final evidence、Strict writer 越权生成IFC、失败结果旧hash；起止manifest均为 `1ecc0e8e…9365a5a` |
| Task 10 backend 第三轮返修 | ✅ RED→GREEN 完成 | I1 8/8、I2 1/1、M1 2/2；聚合/全量/Release/静态均通过 |
| Task 10 backend 第三轮冻结 | ✅ 稳定 | 3文件manifest `52d1095937657cfbffba2927c60364b890d2575ee806517f691f3faccc2acf2e`；实现线程与主代理复算一致 |
| Task 10 backend 第三轮规格复审 | ❌ Not Ready：0/0/1 | 两项 Important 全部关闭；唯一 Minor 为 RAW/final 同时失效时第二个旧 hash 未被清空；起止 manifest 均为 `52d10959…acf2e` |
| Task 10 backend 第四轮返修 | ✅ RED→GREEN 完成 | 双文件同时失效测试0/1 RED→1/1 GREEN；两份产物分别验证并分别清空失效 hash，仍保留损坏产物 |
| Task 10 backend 第四轮验证 | ✅ fresh 通过 | Coordinator55/55、Stage03/MVD325/325、Core1144/1144、Release0/0；禁止项0；组件预期0/3 RED |
| Task 10 backend 第四轮冻结 | ✅ 稳定 | Coordinator `81b74554…d849`、Tests `97e14138…9144`、csproj `dba7663c…8363`；manifest `d16a5aad…7ce9` |
| Task 10 backend 第四轮 delta 复审 | ✅ Ready：0/0/0 | 正式审查与独立测试/hash审查一致；起止 manifest 均为 `d16a5aad…7ce9`，允许进入组件 GREEN |
| Task 10 GH component/生产接线合同 | 🔴 预期 0/9 RED | 9 个失败均指向缺失组件、adapter、translator、UI 或 legacy 未 hidden；测试本身无语法错误 |
| Task 10 translator xUnit | ✅ 5/5 GREEN | 缺失属性enrich、RAW不变、复读/原子发布、预占用、畸形UTF-8、失败temp证据与空批均通过 |
| Task 10 component state policy | ✅ 10/10 GREEN | Strict/Force、空原因、签名变化、原始原因变化与A→B→A迟到结果均已覆盖 |
| Task 10 field formatter/order | ✅ 3/3 GREEN | 乱序输入输出一致、失败字段不丢失；每行含总状态及RAW/final完整证据，阻断稳定分组 |
| Task 10 live DocumentPath/adapter | ✅ 2/2 GREEN | scanner回传document.PathName，adapter只经Revit phase scan/export seam并接通translator/report writers；Release0/0 |
| Task 10 GH component/UI | ✅ 合同9/9 GREEN | 精确5入8出、上升沿/失效token、稳定Data Tree、Strict/Force三路径卡片、legacy hidden；Release0/0 |
| Task 10 component全量验证 | ✅ fresh全通过 | 新增19/19、Coordinator55/55、Stage03/MVD344/344、Core1163/1163、Python401/401、Release0/0、component9/9、static0命中 |
| Task 10 component候选冻结 | ✅ 完成 | 排除看板后共16个功能/测试文件（4 tracked + 12 untracked）；manifest 2046 bytes，SHA-256 `d2d740849e7a54abff6f7e329f92c1c7d99d5b80bce838c5a91c94f58da76e30` |
| Task 10 整体规格审查 | ❌ Not Ready：0/1/0 | 冻结候选未漂移；其余 A–I 均符合，唯一 Important 为 Force 无法完成真实业务阻断场景 |
| Task 10 Force 业务阻断返修 | ✅ RED→GREEN 完成 | 真实0/1 RED→1/1 GREEN；五类定向5/5、Coordinator56/56、Stage03/MVD345/345、Core1164/1164、Python401/401、Release0/0 |
| Task 10 返修后冻结 | ✅ 完成 | 16 files、+6380/-2、2046-byte manifest `b6f42fe0016db1aaba8d3067fed3186411f2fe54bd6d32d16b4b04e5b52cb80f`；staged=0、静态禁止项0 |
| Task 10 整体规格复审 | ✅ Ready：0/0/0 | 独立探针确认 Force 成功且三件套存在；active Pass、技术 fatal、伪造 owner/状态/evidence 仍 fail-closed；manifest 无漂移 |
| Task 10 代码质量审查 | ❌ Not Ready：0/2/0 | 候选无漂移；Important 1为纯IFC转译同步占用UI，Important 2为失败根因/translator diagnostics不可逆丢失 |
| 质量返修 A：纯转译后台化 | ✅ RED→GREEN 完成 | 阻塞hook用例0/1→1/1；translator7/7、Revit host/thread23/23、component9/9；Task.Run仅纯translator 1处，Revit相关0 |
| 质量返修 B：失败根因传播 | ✅ RED→GREEN 完成 | Coordinator sentinel与真实UTF-8均0/1→1/1；diagnostic/类型/HResult/inner贯穿；逆向4/4、null DTO1/1 |
| Task 10 质量返修全量 | ✅ fresh 全通过 | 新增4/4、translator7/7、Coordinator58/58、Stage03/MVD348/348、Core1167/1167、Python401/401、Release0/0 |
| Task 10 质量返修冻结 | ✅ 完成 | 16 files、2046-byte manifest `243858f46d69ce33998b1504a398a777dbb3b05d7344d90842ed5b265f777e04`；staged=0、静态检查通过 |
| Task 10 代码质量复审 | ✅ Ready：0/0/0 | 核心4/4、Translator/Coordinator/host68/68、Python边界26/26；Task.Run与ConfigureAwait边界、异常链均确认 |
| Task 10 主代理复验 | ✅ fresh 全通过 | xUnit78/78、Stage03/MVD348/348、Core1167/1167、Python401/401、component9/9、Release0/0；manifest`243858f4…77e04` |
| Task 10 产物/静态审计 | ✅ 通过 | DLL/GHA 1,528,320 bytes且SHA均`62696a2c…6559c`；12/12 untracked clean、BOM0、删除/备份/旧MVD/绝对路径0 |
| Task 10 Git提交 | ✅ 完成 | `4890e07`；恰好16 files、+6600/-2；提交后manifest仍`243858f4…77e04`，仅看板保持未暂存 |
| Task 10 translator identity自审 | ✅ RED→GREEN | inactive重复identity：首轮5 pass/1 fail，最小修复后translator6/6；修复后全量正在顺序重跑 |
| Task 9 冻结候选 | ✅ 已冻结 | manifest SHA-256 `a7b21715…e8eb22`；实现、规格、质量均 0/0/0；真实 Revit 2020 运行仍留待 Task 12 |
| Important 1：条件激活身份 | ✅ RED→GREEN | 纯策略 3/3、scanner 集成合同 1/1 |
| Important 2：accepted + ambiguous 并存 | ✅ RED→GREEN | carrier suite 10/10、scanner 集成合同 1/1 |
| Important 3：未支持 owner strategy | ✅ RED→GREEN | field policy 8/8、scanner 集成合同 1/1；仅 `BY_EXPORT_GUID` 调用 ExportUtils |
| Important 4：UNKNOWN applicability | ✅ RED→GREEN | field policy 8/8、scanner 集成合同 1/1；UNKNOWN 不得成为 PASS |
| Important 5：saved-role 规则包身份 | ✅ RED→GREEN | 两条定向合同 2/2；完整 audit snapshot 只接受当前 package 三元组 |
| Task 9 首轮定向验证（历史） | ✅ 当时通过 | Task9 xUnit 102/102；Task9 Python 10/10；Important 5 定向 2/2 |
| Task 8 关键回归 | ✅ fresh 通过 | Enricher / Inspector / Mutation 281/281 |
| 首轮 Core 全量（历史） | ✅ 当时通过 | 1065/1065 |
| 首轮 Python 全量（历史） | ✅ 当时通过 | 385/385，73.97s，exit 0 |
| 首轮 Release 构建（历史） | ✅ 当时通过 | 0 warning / 0 error |
| 首轮静态检查（历史） | ✅ 当时通过 | `git diff --check` clean；Task9 无 `Task.Run`、`File.Delete`、新增 backup、硬编码绝对路径；纯 DTO/策略无 Revit 类型 |
| Task 9 首轮正式规格审查 | ❌ Not Ready：0/1/0 | Important：`projectConditions` 缺失全部/部分已知 key 或注入未知 key 时，可能随重算结果一起被接受 |
| Task 9 规格返修 | ✅ 两项 RED→GREEN | 缺少已知 condition 1/1；未知 `forged.condition` 1/1；合法键全集使用规则包全部 14 个 `ConditionId` |
| Task 9 返修后定向 | ✅ fresh 通过 | 两个新增各 1/1；activation 5/5；Task9 xUnit 104/104；Task9 Python 10/10 |
| Task 9 返修后全量 | ✅ fresh 通过 | Core 1067/1067；Python 385/385，102.92s，外部插件自动加载已禁用 |
| Task 9 返修后构建/冻结 | ✅ fresh 通过 | Release 0 warning / 0 error；diff/static clean；27 文件新候选已冻结 |
| Task 9 正式规格复审 | ✅ Ready：0/0/0 | 原 Important 完全关闭；Task9 xUnit 104/104、Python 10/10、Release 0/0；起止 manifest 一致 |
| Task 9 首轮代码质量审查 | ❌ With fixes：0/2/1 | Important：空白 String 误 PASS；host 排队后 callback 外层失败可永久悬挂。Minor：export+rollback 双异常丢诊断 |
| 质量返修 A：空白 String | ✅ RED→GREEN | canonical/text/field 32/32；converter 使用纯策略；required whitespace 落 EmptyRequired 且不生成 enrichment |
| 质量返修 B：host callback | ✅ RED→GREEN | pure gate 3/3；集成合同 2/2；callback-start timeout/error/enqueue failure 单次 fault，迟到 callback no-op；Release 0/0 |
| 质量返修 C：双异常 | ✅ RED→GREEN | pure policy 缺符号 CS0103 RED→15/15 GREEN；service 集成合同 1 RED→1/1 GREEN；双异常按 export、rollback wrapper 顺序聚合 |
| 三项返修后定向 | ✅ fresh 通过 | Task9 xUnit 114/114；Task9 Python 14/14；Task8 IFC 回归 281/281 |
| 三项返修后 Core | ✅ fresh 通过 | 1077/1077 |
| 三项返修后 Python | ✅ fresh 通过 | 389/389，74.16s，exit 0，未重复启动 |
| 三项返修后 Release | ✅ fresh 通过 | 0 warning / 0 error；DLL/GHA SHA-256 均为 `80cb5950…023beb` |
| 三项返修后静态审计 | ✅ 当前通过 | 32 文件、staged=0；Task.Run/File.Delete/backup/绝对路径与纯 DTO Revit 类型逃逸均 0 命中 |
| 新候选冻结 | ✅ 完成 | 32 files / +4809 -126；manifest `9ebdd6b2…aebd7d9`；patch-id `563dbd64…3db6ae7`；staged=0 |
| 返修后规格 delta 复审 | ✅ Ready：0/0/0 | Task9 114/114、Python 14/14、Release 0/0；三项修复均不破坏原规格，起止指纹一致 |
| 首轮返修后代码质量复审 | ❌ With fixes：0/1/1 | Important：Document precheck 令旧 overload callback 无法自行失败完成；Minor：Dispose 可覆盖 export/rollback 异常 |
| 质量返修 D：旧 overload | ✅ RED→GREEN | RevitHost 集成 2/2；pure invoker + gate 8/8；仅统一 UIApplication，Document 交回业务 callback |
| 质量返修 E：Dispose 异常 | ✅ RED→GREEN | pure policy 18/18；service 集成 2/2；显式 Transaction Dispose 后三参数 Combine，全部因果保留 |
| D/E 返修后定向 | ✅ fresh 通过 | 新增 xUnit 26/26；D/E Python 5/5；Task9 xUnit 122/122；Task9 Python 16/16；Task8 IFC 281/281 |
| D/E 返修后全量 | ✅ fresh 通过 | Core 1085/1085；Python 391/391，73.39s，exit 0 |
| D/E 最终冻结 | ✅ 完成 | Release 0/0；34 files / +5069 -127；manifest `ab993889…a051a7`；DLL/GHA `25098fbf…f0a1cb`；staged=0 |
| 第二次规格 delta 复审 | ❌ Not Ready：0/1/0 | `ReadStaticProperty<UIApplication>` 在 invoker seam 外；getter异常不能即时送 callbackFailure |
| 质量返修 F：resolver seam | ✅ RED→GREEN | pure resolver+gate 12/12；RevitHost Python 3/3；resolver getter异常纳入 exactly-once failure seam |
| F 返修后实现线程 fresh | ✅ 当时通过 | Task9 xUnit 126/126；Task9 Python 17/17；Task8 IFC 281/281；Core 1089/1089（实现线程环境） |
| F 返修后 Python | ✅ fresh 通过 | 392/392，72.86s，exit 0 |
| F 返修后 Release/冻结 | ✅ fresh 通过 | Release 0/0；34 files / +5184 -127；manifest `a7b21715d3fe0168998f1fea945372482291ce5ed0d4233168c47337e7e8eb22`；DLL/GHA `ace48a98a7a24cd1f7a0743ffd9703978f458a660c9c00ea9ad984d087c38f5d` |
| resolver 规格复审 | ✅ Ready：0/0/0 | resolver/gate 12/12、Python 3/3、Release0/0；原Important关闭，起止manifest一致 |
| 终局代码质量复审 | ✅ Ready：0/0/0 | Task9 126/126、Python 17/17、Release 0/0、diff-check clean；起止 manifest 与 DLL/GHA SHA 均一致 |
| 范围外测试透明说明 | 🟠 不计入 Task 9 finding | 审查员误跑全套 `--no-build` 1089 项时有 1 个既有环境路径测试失败；Task9 精确验收集随后 126/126 通过，主代理仍将独立复验 |
| 主代理 Task 9 定向复验 | ✅ fresh 通过 | xUnit 126/126；Python 合同 17/17；均由主代理独立执行 |
| 主代理全量回归 | ✅ fresh 通过 | Core 1089/1089；Python 392/392（73.29s）；范围外路径用例在本工作树中通过 |
| 主代理构建/静态/指纹 | ✅ fresh 通过 | Release 0/0；34 文件、staged=0、diff-check clean；所有禁止项 0；DLL/GHA `ace48a98…38f5d`；manifest `a7b21715…e8eb22` |
| Task 9 Git 提交 | ✅ 完成 | `b56100a`；34 files changed，+5184/-127；提交后仅进度看板处于未暂存状态 |
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
| 下一执行 | 🟠 质量finding真实RED→GREEN | 单一实现线程补行为测试并最小修复；再次冻结后由原质量审查员复审 |
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

Task 9 已完成并提交 `b56100a`：34 文件候选通过规格与质量终局复审 0/0/0；主代理 fresh 通过 Task9 xUnit 126/126、Task9 Python 17/17、Core 1089/1089、Python 392/392、Release 0/0，以及静态与候选指纹复核。审查员误跑全 Core 时出现的 1 个范围外既有环境路径失败，在主代理工作树复跑中已通过。

Task 10 已完成并提交 `4890e07 feat: complete Stage03 validation export workflow`：恰好16个功能/测试文件，提交manifest为`243858f46d69ce33998b1504a398a777dbb3b05d7344d90842ed5b265f777e04`；规格与质量终局复审均0/0/0，主代理独立fresh通过定向78/78、Stage03/MVD348/348、Core1167/1167、Python401/401、component9/9、Release0/0、静态与产物审计。正式进度已升至10/12（83%）。下一焦点为Task11文档、CI、完整自动化和单GHA无备份部署；实机点击与指定RVT验收仍保留到Task12。

## 进度口径

- 只有“实现、自动化验证、审查、提交”全部通过，Task 才标为 100%。
- 测试数字只记录当前候选代码的新鲜结果；代码再次变化后，旧结果仅作为历史基线，不作为完成证明。
- Task 11 只有完成无备份的单 GHA 部署后才算完成。
- Task 12 只有在 Revit 2020 指定 RVT 上完成真实闭环后才算完成；此前不会宣称整个开发完成。
- 每次 Task 状态变化、全量验证、提交、部署或实机验收后，更新本页的日期、证据和百分比。
