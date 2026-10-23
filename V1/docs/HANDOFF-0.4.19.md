# H-IFC V1 开发交接 · 0.4.19

更新时间：2026-10-23（Asia/Shanghai）。本文面向接手开发 agent，说明当前产品、可复现入口、规则来源、验证边界和后续工作。历史文档按各轮事实保留；当前状态优先查本文、[V1 README](../README.md) 与 [正式验证索引](../validation/0.4.19-validation.json)。

## 1. 发布基线与接手入口

| 项目 | 当前值 |
|---|---|
| 仓库 | `https://github.com/ArchitectureWorld/BIM-baogui.git` |
| 产品主线 | `main`；发布标识 `v0.4.19` |
| 本机权威源码 | `D:\18_建模项目\湖北BIM云平台\V1` |
| 包 / 程序集版本 | `0.4.19` / `0.4.19.0` |
| 宿主 | Windows、Revit 2020、.NET Framework 4.8、x64 |
| 本机包 | `V1/dist/Hifc.V1.Revit2020-0.4.19/`，不入 Git |
| 正式安装 | `%APPDATA%\Autodesk\Revit\Addins\2020\Hifc.V1\` |
| 安装清单 | 同级 `Hifc.V1.addin`，入口 `Hifc.Revit.App` |
| 工作区配置 | 安装目录 `settings.json` 的 `workspace_root` 指向本机 `V1` |
| 独立平台程序 | `V1/platform-sync`，版本单独保持 `0.1.0`，真实平台只读 |
| 标准化技能分支 | `codex/hifc-site-standardize-skill`，目录 `V1/skills/hifc-model-standardize/` |

正式化以前的功能实现提交为 `95a3c9e`，候选安装及文档提交为 `e1897ad`。本轮去掉 preview 版本后缀，业务实现未再改动。正式构建产生新二进制哈希，不能将候选验收哈希冒充正式包哈希。

接手首先执行 `git status --short`、`git log -5 --oneline`、`git fetch origin --prune`，核对目标分支及任务范围。该本机工作区存在大量客户材料和未跟踪运行证据，状态非空并不意味着需要清理或提交。只暂存本轮明确文件，禁止 `git add -A`。

`HIFC-Web` 是独立产品支线；技能支线也有较旧的代码基线。不得用它们的整棵目录覆盖 main，不合并它们的旧安装器或旧版本；需要复用时只选明确文件。保留已有工作区、客户成果和冻结验收资料。

## 2. 产品流程与体验约定

主入口：**H-IFC V1 → 模型属性管理 → 模型检查 / 属性管理**。流程是 **1 人工确认阶段 → 2 自动识别或确认模型类型 → 3 全面检查 → 4 写入 Revit**。

- 每轮重新全面扫描。缓存用于恢复阶段、设置、草稿和人工编辑记录，不能代替当前模型事实。
- 管理窗口可与 Revit 同时操作；构件列表支持定位。无需预选即可检查。独立“选中构件赋属性”用于非标准建模的人工指定，写入后静默全面复检，不强制弹出另一窗口。
- 右侧直接编辑；红色表示未完成/待写内容，橙色表示回读成功的人工值。预览、草稿和参数创建均不等于已经写入。事务成功也不等于 RVT 已保存。
- 适用分类提供可键入下拉。平台真实字典支持主功能→子功能建议；没有证据的建筑类别→功能关系不强绑。上级变化保留不匹配的旧子级并提示；多选混合值未编辑时不产生统一赋值。
- 是否属性在插件和 Revit 参数面板为复选框。旧文字布尔须通过受控迁移；编号或普通数字 0/1 不转换。
- 默认完整项目导出，含有效 Area 和未分类的新几何，不依赖预选或当前视图筛选。链接源文档不自动并入；仅检查模型，不检查材质贴图。

## 3. 业务规则及依据边界

冻结参考在 `reference/gonggui/`（280 条属性定义、205 个不同来源参数名），原成功案例在 `reference/success-case-01/`。选择的平台方案为 `reference/platform-420626-20261020/`。源码维护运行时兼容规则，不改写原始证据来消除差异。案例中的对象/字段清单不是全国通用的必填清单。

| 领域 | 目前约定及不可破坏的行为 |
|---|---|
| 阶段和类型 | 阶段由人工确认，模板写“施工图”不推翻已经确认的报规。模型类型依真实构件证据，文件名仅提示；混合或不明需明确上下文。施工图专项业务规则未建立。 |
| 强制提示 | 建筑用途名称缺失，以及区内道路类型为“消防道路”的道路宽度缺失，需在类目和字段明确标红。普通道路不套用手填宽度要求；写入并复检后才解除。 |
| 用途与民用建筑 | 学校单字段实验确认“建筑用途名称＝中学”改变了法规匹配；不能把区域子功能当作这个建筑级字段。2026-10-23 用户在技能审查中另行确认：标准化作业将适用单体的“民用建筑”统一写为“否”，已有“是”纳入明确修正预览。此为最新操作要求，不改写历史实验因果，也不由此推出“不参与人防”。正式插件仍保护已有明确值，该统一规则由技能通过插件显式批量写入执行。 |
| 面积 | 有效人工楼层面积、区域申报面积、项目投影面积等优先。插件显示“该数值可手动修改并被平台读取”，但实际平台结果仍须本轮验收。楼层总值按已确认区域比例分配并处理舍入余差，不能在每块区域重复整层面积。写入与导出共用算法。 |
| 范围检查 | 分摊范围不齐、来源冲突或已知投影重叠先阻断并定位；已分类区域合计与人工楼层值相等时，不因无关小楼板候选误拦。项目建筑投影面积独立用于建筑密度输入，不用多层面积之和替代。 |
| 布尔 | 空值默认否；空的建筑基底优先按确认基底层，未确认时首层默认是、其他层否。已有明确人工值保留，包括首层人工否。标准化新规则对民用建筑的显式修改按上一行处理。 |
| 净用地控制红线 | 身份确认后，名称和红线类型均为“净用地控制红线”。规划控制线与规划净用地可为同轮廓不同高度实例，保留两种身份，不能因 XY 重合删掉一个或重复计量。 |
| 绿地和停车 | 无冲突的绿化楼板可生成绿地建议；绿地类型、数量、系数按本项目资料。停车位/停车场对应有效 Area→IfcSpace，不把停车族上的参数当空间业务属性。 |
| 地下 | 人防区域、消防分区、停车场、停车位使用 Area→IfcSpace；地下建筑区域使用楼板→IfcSlab。负标高不自动证明地下楼层。地上地下可以属于同一个 IfcBuilding。 |
| 分类清理 | 类目切换在同一写入事务中清除旧类目专属实例值，保留共用参数与共享类型数据；失败整体回滚。普通名称/尺寸/类型文字“250”不构成专用业务分类。 |
| 自定义分类 | 支持按原文真实写入、保存和导出，显示平台识别待验证；不得宣称所有自由文字均被平台支持。 |

学校的五层面积、1#/2#、绿地“附属绿地”、停车 11/2、消防道路 4m、最高标高作屋面及移动 0.5m 均是项目确认，不能成为所有模型默认值。完整沿革见 [判定依据](模型检查判定依据.md) 与 [开发记录](development-ledger.md)。

## 4. 模块地图与数据流

以下路径相对 `V1/`。

| 入口 / 责任 | 主要源码 |
|---|---|
| Ribbon、命令及宿主启动 | `src/Hifc.Revit/App.cs`、`Commands.cs` |
| 案例与平台业务规则 | `src/Hifc.Reference.Core/CaseWorkflow.cs`、`AutoFillPolicy.cs`、`PlatformRules.cs`、`PlanningInputs.cs`、`ClassificationInputs.cs` |
| 全量取数、识别与几何建议 | `src/Hifc.Revit/Management/CaseModelReader.cs`、`ModelReader.cs`、`ModelGeometry.cs` |
| 非模态主界面及写入调度 | `Management/ManagerController.cs`、`ManagerWindow.cs`、`ManagerUiModels.cs` |
| 预览、批量写入及冲突保护 | `Management/CaseEdits.cs`、`ModelEdits.cs`；核心 `EditGuard.cs` |
| 选择工具 | `Management/SelectionController.cs`、`SelectionService.cs`、`SelectionWindow.cs` |
| 楼层面积同步 | `Management/FloorAreaSynchronization.cs`；核心 `FloorAreaPolicy.cs` |
| 参数来源、物化与归属 | `RevitMappingService.cs`；核心 `ReferencePolicy.cs`、`PropertyUnits.cs` |
| 布尔定义/旧值迁移 | `BooleanParameterService.cs`、`BooleanAliasService.cs`；核心 `BooleanPolicy.cs` |
| 报规表及红线 | 核心 `ReportingWorkbook*.cs`、`Redline*.cs`；宿主 `Management/*Workbook*`、`Management/Redline*` |
| 导出名称与核对窗口 | 核心 `ExportFileNaming.cs`；宿主 `ExportDestinationPicker.cs`、`Management/ExportNamingWindow.cs`、`ExportReviewController.cs` |
| 原生导出、全项目范围 | `NativeExportService.cs`、`ProjectExportScope.cs`、`NativeExportProfile.cs` |
| 原生几何与类型隔离 | `NativeFloorGeometry.cs`、`NativeTypePropertyScope.cs` |
| 单 IFC 交付与内部产物 | 核心 `ExportArtifacts.cs` |
| 同步交接 | 核心 `PlatformSyncHandoff.cs`；宿主 `PlatformSync/`；独立程序 `platform-sync/src/` |

除注明核心路径外，表内省略前缀的宿主路径位于 `src/Hifc.Revit/`。通常先改规则纯逻辑，再接宿主/界面；不要把 Revit 文档对象传给后台线程。

正常写入链：`CaseScanJob` 分批读到 `Complete` → `Snapshot` → `CaseEdits.PreviewBatch` → 核对 `Conflicts/Changes` → `ApplyWithWorkbook` → 回读并重新全面扫描 → 用户保存。单项 `Preview`/`Apply` 保留，但资料与属性同次提交应复用完整路径。选择工具仅操作所选实例，项目和 Level 属性不能绕入该入口。

方法多为 internal，自动验收使用反射。调用前核对当前加载程序集的签名、路径和哈希，不能把旧脚本参数位置当稳定公共 API。`ReadNext` 返回 void，完成状态读 `Complete/Position/Total`，不能只读固定上限后提交局部快照。

## 5. 存储、事务与人工值保护

- 业务参数真正保存在 RVT 的 ProjectInformation、Level、实例或类型上。来源 GUID 由 `ReferencePolicy.SourceGuid` 决定，不能另建同名 GUID 再随意使用 `LookupParameter`。
- `ManagementStateStore` 的阶段、类型、快照、草稿、人工值记录、计算和地下设置在 `RuntimePaths.OutputDirectory()/management-profiles/`。键包含模型路径和 ProjectInformation 身份，另存/复制需重核。此旁路文件不是随 RVT 自动迁移的完整业务状态。
- 报规 Excel 已选数据和来源归档由 `ReportingWorkbookProjectStorage` 写入 RVT ExtensibleStorage；红线资料由 `RedlineProjectStorage` 保存。原始单元格不是自动新增的官方 IFC 属性。
- Excel 按一栋一工作表/文件明确绑定，源空白不清模型值，冲突不默认覆盖。未写资料和属性草稿均阻止直接导出旧值。
- 布尔迁移先验证绑定类别、旧值、明细表/过滤器/全局参数等引用。无法保持依赖时停止该事务，不手工删参数或吞掉错误。不同语义同名字段使用已有别名政策。
- 所有 Revit API 读写在合法 API 上下文。非模态窗口走 ExternalEvent。MCP 的外层事务可能阻止 TransactionGroup、SaveAs 或保存；用干净 ExternalEvent 调度，不靠关闭无关用户模型解决。
- 实验先使用副本。要写用户原文件须有对应授权，先核对最新未保存内容；不能用磁盘旧副本覆盖当前人工编辑。事务完成、保存成功、关闭重开、平台计算分别留证。

## 6. 0.4.19 导出契约

名称固定为 `OUT_项目名称_阶段_具体单体名称或总图_YYYYMMDD_Vn.ifc`。RVT 输入可有 `3D_`，规范普通名按最后一个下划线分项目/单体；旧横杠名是可编辑建议。导出窗口确认名称与目录，不重命名 RVT；总图第三段固定总图，阶段沿用确认。

同目录、同日期、同项目/具体单体按最大版本加一，新日期无已有文件从 V1 开始。历史 IFC、配置、回执和日志也占号避免覆盖。确认时重算并在最终发布采用独占新建，不能用覆盖写绕过竞争检查。

标准化技能另存的 RVT 带阶段、日期和版本。当前插件普通解析器不会完整剥离这些后缀；技能应解析出明确的 project/subject，使用 `StemForNames`/`NextPath` 或确认窗口填写，避免将 V1 当楼名。本轮未扩展主线解析规则。

完整成功顺序：

1. `NativeExportService.ReviewProject` 核对来源和面积，管理状态确认无未处理草稿。
2. `ExportProject(Document,string)` 读取完整项目范围；普通新增未分类几何仍由原生导出器处理。
3. 临时事务中建立映射桥、面积出口、控制线实体及类型属性隔离，调用一次 `Document.Export`。
4. 使用原生 IFC4 DesignTransferView，保留已验证曲线/楼板导出路径，禁止用文本后处理冒充真实映射。
5. 检查事务回滚、已映射来源快照一致、临时视图消失、导出服务恢复及非空 IFC。
6. 在内部目录写成功 receipt，然后独占新建最终 IFC，复制并校验 SHA-256。
7. 完整成功后才登记平台同步候选；同步失败应单独说明，不能反向宣称导出失败或悄悄重复上传。

用户目录只新增 IFC。其余产物在 `%LOCALAPPDATA%\Hifc.V1\export-records\<export-id>\`，包含 `export.receipt.json`、映射配置和日志。通过 `ProjectExportScope.ReceiptPath` 获取实际 receipt，禁止继续推断 `ifcPath + ".receipt.json"`。成功后尝试移除内部 IFC 工作副本；历史用户交付不清理。

receipt schema 当前为 `hifc.native-export-receipt/1.5`。全项目模式不传当前视图过滤或 `ElementsForExport`；局部 `Export(Document,Element[],string)` 仅供开发验收，不能代替交付。receipt 的来源快照范围是 `MAPPED_HIFC_CARRIERS`，不应夸大为所有几何都逐项核验；几何全量证明另用独立审计。

## 7. 平台同步的当前边界

Revit 提供平台设置、同步本次 IFC 和同步状态；普通导出独立可用。独立 TypeScript/Playwright/SQLite 程序运行不需要 LLM。平台绑定包含项目、模型 ID/代次、RVT 路径与身份，不能依赖 IFC 文件名或单体/总图两个标签。

真实适配器当前只读，写入被禁止。完整替换仅在本地 fixture 演练：冻结成功 IFC/receipt 并重验哈希，删除前下载真实旧字节并核对代次，写前持久化意图，结果未知先核对，恢复不直接重放。计算完成与法规审查通过分别显示。本机 SQLite 锁不提供多电脑排他。

后续真实验收还需证明独立浏览器登录与项目/场景/模型身份、旧 IFC 下载对应关系、IFC→FRAG 实际转换、自动计算触发、本次结果关联、断网恢复和跨机器排他。不能把历史“已完成”文字当本轮成功。账号令牌和浏览器 profile 不入库，真实平台模型写入需对应授权。详见 [同步协议](../platform-sync/CONTRACT.md) 与 [程序说明](../platform-sync/README.md)。

## 8. 构建、测试与安装

在仓库根目录使用 PowerShell。需要 .NET SDK、.NET Framework 4.8 targeting pack，Revit API 位于 `C:\Program Files\Autodesk\Revit 2020\`。Autodesk DLL 不分发到 Git 或安装包。

```powershell
pwsh -File V1/tools/Build.ps1
python -m pip install -r V1/requirements-validation.txt
python -m unittest discover -s V1/tests -p 'test_*.py'
Set-Location V1/platform-sync
npm ci --ignore-scripts --no-audit --no-fund
npm test
```

本机 Python 可使用 `C:\Users\2899\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe` 并将 `V1/.deps` 加入 `PYTHONPATH`；核对二进制扩展 Python 版本，不修改全局环境来修补临时试验。独立程序要求 Node 24+，本轮本机为 25.4.0，CI 为 24。

`Build.ps1` 运行核心/WPF/Workbook/Redline 后构建插件并生成 manifest，不自动安装。关闭 Revit 后另行执行：

```powershell
pwsh -File V1/tools/Install-Revit2020.ps1 -PackageDirectory "D:\18_建模项目\湖北BIM云平台\V1\dist\Hifc.V1.Revit2020-0.4.19"
```

安装器不强杀 Revit，备份前一安装并校验包和 DLL。必须再核对版本、settings、manifest 路径和实际加载哈希；磁盘更新不证明运行中宿主已换 DLL。发布者提示需要用户加载后测试才能继续，MCP 开启后的模态提示也会阻塞。优先在当前合法宿主补测，避免无依据重复重启。

原生导出验收入口为 `tools/Run-ExportDeliveryAcceptance.ps1`；其余各 `Run-*Acceptance.ps1` 需先读参数和副本策略。此类脚本会启动 Revit，应确认运行环境和模型范围后调用，不能在用户编辑原文件时盲跑。

## 9. 本版证据与限制

| 验证层级 | 结果 | 说明 |
|---|---|---|
| 正式包构建 | 0 警告、0 错误 | 业务源码与候选一致，仅版本元数据正式化 |
| 核心 / WPF | 267 / 140 通过 | 正式构建重新运行 |
| Workbook / Redline | 21 / 33 通过 | 正式构建重新运行 |
| Python / 同步程序 | 34 / 33 通过 | 同步程序包含本地浏览器回归，非真实平台写入 |
| 候选 Revit 宿主 | 26 通过 | V1/V2、历史 V4 占号后 V5、自定义目录、跨盘发布、模型保持 |
| 候选实际 IFC | 27 项独立核验通过 | 三个 IFC；每份 19 个非空字段严格映射，schema 错误 0 |
| 完整业务审计 | 未通过 | 生成模型保留 35 个空来源和 1 个空 Pset 诊断，与基线相同 |
| 正式包安装 | 5 个 manifest 文件匹配 | ProductVersion 0.4.19、程序集 0.4.19.0；正式新二进制尚未启动宿主 |
| 本轮真实平台 | 未执行 | 不宣称 0.4.19 客户模型迁移或法规通过 |

正式索引：`validation/0.4.19-validation.json`。候选冻结索引：`validation/0.4.19-preview.1-validation.json`。较早布尔/资料和分类原生验证分别见 `0.4.17-preview.2-validation.json`、`0.4.18-preview.2-validation.json`，不得简单累加成一轮验收。

以下原始证据仅在本机，未发布模型或运行日志：`validation/export-delivery-native-20261023-232500/`、`validation/export-delivery-ui-20261023/`、`validation/release-0.4.19-*.log`、`outputs/install-20261023-233450-21db38e1/`。最后一项含 preview 安装备份、安装回执及独立核验。离开本机时依据索引重新构建和验收，不能将不存在的本地路径当远程可下载附件。

## 10. 后续工作及完成标准

1. 新宿主加载正式 0.4.19 后，记录实际 DLL 哈希；客户模型的布尔迁移、分类自定义和资料导入仍需按真实依赖逐项验收。不要因受控模型已通过而批量绕过冲突。
2. 如扩展标准化 RVT 名自动解析，覆盖带阶段/日期/Vn、可选 3D、旧名、总图和多个单体，保留用户确认窗口及版本竞争保护。当前技能可以显式传入身份，不因此阻塞使用。
3. 红线自动比对当前仅覆盖单水平直线棱柱、单外环；曲线、多环/孔洞、斜面和复杂实体需要专项实现。人工坐标先收集资料，单位/坐标框架未定不得伪造核验通过。
4. 部分原生标准数量如 GrossVolume/NetArea 仍有单位或语义疑点；几何算法差异与人工申报值分列。尝试替换 Autodesk 导出器需独立验证曲线、墙、Area 和数量；不能回到已退化的路径。
5. 真实平台同步按第 7 节依赖逐项开放；单模型闭环先于监听、无人值守与批量。失败恢复必须有可核对的旧文件和明确代次。

后续每次发布同步 `Directory.Build.props`、安装默认包、README、`docs/mainline.md`、判定依据、开发记录和新的验证索引；历史证据不改写。测试按实际改动范围执行，记录源码 SHA、包/安装/加载哈希与验收范围。提交只含本轮源码、测试和公开文档，审查差异与敏感资料后推送；远程分支、发布标签及对应 SHA 的 CI 均需核对。

新 agent 不应靠阅读一句“已完成”推进破坏性操作。判断完成应落到当前模型、当前构建、真实保存、实际 IFC 和本次平台回执，各层分别报告。
