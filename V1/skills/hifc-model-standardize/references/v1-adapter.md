# H-IFC V1 报规适配与执行接口

当前技能覆盖报规总图、单体及已有证据的地下区域。共用流程见技能入口，业务规则见 [总图规则](site-rules.md)、[单体规则](building-rules.md) 和 [数据与验收](data-and-validation.md)。施工图专项规则尚未建立。本文末尾的 0.4.2 实验是历史记录，不代表当前能力或所有模型均已验收。

每次读取宿主实际 `Hifc.V1.Revit` 程序集版本、路径与哈希，并核对对应接口；源码、安装目录和加载中 DLL 可能不同。以下接口来自 2026-10-23 的源码核对，不保证后续构建签名不变。签名变化时重新审查，禁止用旧反射调用盲写。0.4.19 正式版本已由相同业务源码重新构建、安装；生成模型原生验证对应 0.4.19-preview.1 的实际哈希，正式新哈希未重复宿主验证。旧轮次及分类增强的验证范围见数据与验收，不替代客户模型迁移或本轮平台结果。

## 命名与最新模型另存

成果 RVT：`3D_项目名称_报规_具体单体名称或总图_YYYYMMDD_Vn.rvt`。例如 `3D_歇马镇中心学校_报规_综合楼_20261023_V1.rvt`；不再附加“单体”。旧“标准化_”前缀只用于读入兼容，新成果使用 `3D_`。

在技能目录运行命名工具，显式身份来自已确认资料，例如：

```powershell
python scripts/standardized_name.py --source "D:\项目\3D_歇马镇中心学校_综合楼.rvt" --project "歇马镇中心学校" --subject "综合楼" --date 20261023
```

工具只读取文件名与目录，返回 `source/target/action/project/subject/stage/filename_kind_hint/ifc_stem`，不修改模型。`save_as` 表示先另存；`reuse` 表示继续用户指定的已有规范成果。新交付加 `--new-version`，可指定已有 `--directory`。版本取同组最大值加一；SaveAs 前再次检查目标不存在。文件名不能可靠拆分时明确项目与模型名，不能把日期或 V1 当成楼栋。

记录源文档对象、不可变来源路径、磁盘哈希及未保存状态。以该文档最新内容在干净 Revit API 上下文中执行 `Document.SaveAs` 到规划出的不同路径，成功并核对目标路径后才做业务写入。文件系统复制只含上次保存的内容，不能代替含当前编辑的 SaveAs。SaveAs 会改变同一 Document 的 PathName，因此保存前后的身份和来源路径分别记录。后续 Save、关闭重开、导出只针对成果；源磁盘哈希应保持。已明确继续既有成果时不重复另存。工作共享模型另核对脱离/另存设置，不能让成果继续向原中心模型同步。

IFC 沿用已确认的导出命名政策，与 RVT 名独立：总图为 `OUT_项目名称_报规_总图_YYYYMMDD_Vn.ifc`；单体为 `OUT_项目名称_报规_具体单体名称_YYYYMMDD_Vn.ifc`。命名工具只给出尚未加版本的 `ifc_stem`；用实际插件 `ExportFileNaming.NextPath(directory, stem)` 取得含 IFC/回执等关联文件碰撞检查的下一个版本。模型类型仍以完整扫描与确认值为准；工具的 `filename_kind_hint` 不是业务判定。

0.4.19 的 `ExportFileNaming.SuggestNames` 处理可选 `3D_`、最后下划线分段和旧横杠建议，但不会完整剥离标准化 RVT 的阶段/日期/版本后缀。直接使用默认建议可能将 V1 当楼名。本技能从命名工具取得 project/subject，在导出确认窗口明确填写；自动执行时用 `StemForNames(project, subject, stage, kind, date)` 与 `NextPath(directory, stem)` 或经核对的 `ifc_stem` 生成路径，并沿用完整模型已确认类型。`filename_kind_hint` 不替代模型类型检测。本次仅修正技能辅助命名，不宣称插件普通解析器已自动支持标准化后缀。

## 契约和接口

- `V1/reference/gonggui/contract.json`：Pset、属性名、数据类型、原始来源参数、实例/类型绑定及允许的 IFC 实体。
- `V1/reference/success-case-01/` 及后续案例/验证目录：分别限定证据范围，不能解释为所有官方必填内容。
- 业务分类参数为实例 `类目`。通用类型参数 `构件类型=250` 不足以确认用地、道路等业务。
- `CaseScanJob(Document, stage, kind)`、`ReadNext(Document,int)`、`Snapshot(Document)`：每次全量取数。ReadNext 返回 void，须读 `Position/Total/Complete`，循环直到 Complete；不能固定一次 100000 后就声称完整。遇到未完成且无进展时失败，不提交局部快照。内部类可通过已加载程序集反射调用。
- `CaseEdits.Preview(doc,snapshot,request = null)`：全面自动预览或单项请求；`PreviewBatch(doc,snapshot,EditRequest[])`：与自动补齐、楼层面积同步一起合并批量请求。检查 `Conflicts`、`Changes`、`ProposedElements`，新请求、来源或模型变化后重新预览。
- `CaseEdits.Apply(doc,preview)`：对同一目标文档校验预览指纹、写入、回读与事务回滚。存在 Excel 资料时使用 `ApplyWithWorkbook(doc,preview,ReportingWorkbookDecision[])`，与选用业务值同次提交。管理窗口 `ManagerController.Write` 已使用 PreviewBatch/ApplyWithWorkbook 并重新全扫；优先复用此链。保存 RVT 是写入后的独立步骤，事务成功不等于已保存。
- `PreviewSelection` 要求所选构件快照，`SelectionService.Write` 仅允许所选实例。项目与楼层属性必须使用全面管理/批量路径，不能向手动选择工具塞入 ProjectInformation 或 Level 来规避范围校验。
- `EditRequest` 是 `Hifc.V1.Revit` 程序集的内部类 `Hifc.Revit.Management.EditRequest`，分类 Kind=分类、Ids、Slot；赋值 Kind=赋值、Ids、PropertySet、Source、Binding、Value。从实际快照选择字段，不猜名字。
- `CaseWorkflow.ForModel` 与 `Summarize` 获取实际案例规则和复检结果。
- `RevitMappingService.Slot/Entity/Properties` 校验分类与载体；`NativeExportService.ReviewProject` 检查来源/阻塞/面积出口。正常交付调用 `ExportProject(Document,string)`，包括当前项目有效 Area 及未分类几何；`Export(Document,Element[],string)` 仅供开发选择范围验收，不能拿它替代全项目交付。
- `ReferencePolicy.SourceGuid(name)` 定义来源共享参数 GUID；不重建另一套 GUID。
- `V1/tools/verify_ifc.py` 独立校验实际 IFC 与 receipt。从 `ExportProject` 返回的 `ProjectExportScope.ReceiptPath` 取得真实内部回执；0.4.19 不再使用 IFC 同级路径推断。该脚本对缺值也会报错，报告需保留缺值事实，不能为测试通过造值。

常见总图映射：

| 业务 | 实际来源字段示例 | 载体注意 |
|---|---|---|
| 规划净用地 | 规划净用地投影面积、规划净用地名称、版本、规划净用地类型 | 需先确认净用地含义 |
| 规划控制线 | 名称、红线类型（按实际来源定义取得） | 确认净用地控制线后，两值均为“净用地控制红线” |
| 道路边界线 | 区内道路类型、道路边界线道路宽度 | 仅消防道路的宽度要求手填并缺失标红 |
| 绿地 | 绿地投影面积、绿地类型、折算系数 | 面积不等于可自行决定折算系数 |
| 停车位 | 停车位位置信息、车位数、是否为室外停车场等 | 对应 Area/IfcSpace；输出链须单独验证 |

人工面积必须先经批量预览，将楼层值和对应区域分配实际写入并保存；`FloorAreaSynchronization.ForExport` 的导出临时联动不能替代此持久化步骤。项目投影面积独立保留人工值。布尔空值/基底例外及旧参数迁移统一遵循数据与验收，不用字符串 LookupParameter 写入替代插件的定义、冲突与迁移检查。

导出前解决属性/资料草稿、当前来源冲突和实际阻塞；直接 API 调用不能跳过管理链的检查。原生服务执行后还有回滚和来源一致性检查，必须等待调用完整返回、检查成功回执与 IFC，并核对导出前后持久参数。出现文件但随后抛错不是导出成功。0.4.19 先在 `%LOCALAPPDATA%\Hifc.V1\export-records\<export-id>\` 完成原生导出及校验，再发布最终 IFC；内部保留配置、日志和 `export.receipt.json`，用户交付目录只生成 IFC。不要将内部附件重新复制到交付目录。需要平台同步时只交接完整成功的冻结 IFC、SHA-256 和回执；当前真实适配器只读，上传/计算的实际操作须按本轮授权与可用工具另行执行。

## 宿主细节

Revit MCP 的代码模板可能自动开启当前活动文档的事务。此时即使后台副本 IsModifiable=false，启动 TransactionGroup 也会被全局活动事务阻止。使用 ExternalEvent，在包装事务结束后的合法 API 上下文执行 SaveAs、目标写入、事务组、保存及关闭重开。一次性 Idling 可用于允许的读写，但不能承担要求 ExternalEvent 的文档切换。先解除一次性回调、核对目标文档，并写独立成功/失败回执；“已排队”不是已执行。

主会话可能使用同一宿主。只操作当前任务明确授权的模型和成果；所需 SaveAs/成果重开以当前任务范围为限。不要为技能验证重启 Revit、安装 DLL、改全局设置或关闭其他正在编辑的项目。不能在合法上下文打开目标时报告具体限制，不通过关闭无关原项目绕过。

ImageExportOptions.SetOfViews 的导图过程可能将后台副本激活。导图前记录原活动文档和视图；在 ExternalEvent 中恢复原文档，再核验 ActiveUIDocument。Idling 事件中禁止 OpenAndActivateDocument，恢复操作必须用 ExternalEvent。写入回执中的原文件路径使用最初记录的不可变值，不使用可能已变成副本的模板 document.PathName。

MCP 会引用许多已加载 DLL，某些环境中的 WeaverBird 与 mscorlib 重复定义 Func。取数脚本使用 Converter 规避。Area 的 LocationPoint.Rotation 不受支持，只读取族实例的旋转。

Revit 2020 中不能用 OfClass(typeof(Area))；应使用 OfCategory(OST_Areas) 后 Cast<Area>，或收集 SpatialElement 再筛选。停车补建在正确 AreaScheme 的 ViewPlan 中，使用 NewAreaBoundaryLine 创建闭合线，再 NewArea(view, UV) 放置面积。名称读回可能附加面积编号，应分别读取名称参数和编号。现有 Area 优先复用；缺失分支可用副本事务回滚测试验证，但回滚测试不等于已交付新增对象。

脚本中的原生 double 参数保留内部单位；单独输出的坐标为 m、面积为 m²。面积图示来源为真实边界；若包含曲线，三角化/离散化结果只能按标注精度使用。不得静默修补无效边界。

使用项目的二进制 Python 依赖前检查版本，例如 cp312 的 Shapely/IfcOpenShell 必须使用 Python 3.12。不要为了运行单次实验修改全局 Python。

## ExternalEvent 执行桥

将 `scripts/ApiWork.cs` 编译到本轮实验目录的 DLL，引用当前宿主的 RevitAPI.dll、RevitAPIUI.dll 和 System.Web.Extensions.dll。可使用 .NET Framework 4.x csc；不安装 addin，不重启宿主。

在 MCP 的合法 API 上下文中 Assembly.LoadFrom 该 DLL，创建 `HifcSkillExperiment.ApiWork`，设置 `Work` 为 `Converter<UIApplication,object>` 回调、`ReceiptPath` 为本轮唯一 JSON 路径。回调的工作正文先校验目标路径与对象身份，再执行事务并返回结果。用 ExternalEvent.Create 创建事件，赋给 `Event` 字段，Raise；保留 handler 引用到完成。桥执行结束后写回执并释放事件。

发起后读取回执中的真实 success/error；操作失败时先核查回滚和活动文档再继续。首次授权另存可从源文档 SaveAs 到不同目标；其余写入、Save、关闭和重开均核对成果路径，不保存覆盖来源文件或关闭其他用户文档。只有名称/路径计划完成时，不能声称 RVT 已另存或标准化完成。

## 不相交绿地与道路的拆分

读取实例的实际 Solid，合并相交体后用 `SolidUtils.SplitVolumes` 判断连通体，不能只数原始 Solid 或顶面环。包含孔洞的连续道路仍可只需一个构件。保留真实 Line/Arc；曲线离散仅用于证据图，不用于替换原生边界。

Revit 2020 使用 `doc.Create.NewFloor(CurveArray, FloorType, Level, bool)` 等实际可用重载。从各连通体的水平顶面取 `GetEdgesAsCurveLoops()`，同一片区的内环应作为孔洞处理；不能把所有环各自创建楼板。斜坡、形状编辑、多顶面或外部依赖未经完整保留和验证时，不套用水平单环重建脚本。

将新构件与对应原连通体做双向布尔差及面积、体积比较，核查子构件两两相交体积和曲线类型/数量。旧构件的自动草图尺寸可随旧草图移除；先核实不是外部视图标注或锁定的设计约束。删除实际返回的 IDs 必须全部属于已核查依赖。重新分配唯一实例标记，保留类型与阶段。逐构件通过插件计算并写入自己的投影面积；记录总曲线积分面积和插件投影和，不能混用两种算法作为守恒判据。

`Face.Area` 在部分情形是估算；`Solid.Volume` 在曲面存在时可能通过离散面近似计算。重建相同曲线边界后，这两个 API 读数可能有很小差异。不要仅扩大容差：用边界端点、中点、长度及曲线类型匹配，配合双向布尔差验证。水平 Line/Arc 闭合轮廓可用解析积分独立比较面积；确认是竖直等截面实体后再用面积乘厚度比较体积。保留 API 读数差值、采用的容差和判断依据，不声称近似读数严格相等。

后台事务应有明确的失败处理，避免 Revit 警告弹窗阻塞当前用户。重建可能重新触发既有道路/绿地边界接触警告；先验证原几何与新几何的相交情况一致，再按具体 FailureDefinitionId 和已核对的对象对记录处理。未知警告、错误或新增重叠应回滚，不批量忽略所有警告。`scripts/capture_model.cs` 的楼板盘点包含 `connectedComponents` 与 `connectivityError`；读取失败不算通过。

## 历史验证：2026-10-16 报规总图

以下保留当时模型、构建与限制，便于追溯；当前任务重新核对已加载版本和本轮输出，不把旧缺陷直接当作当前状态，也不把后续单项修复合并成整套交付已通过。

2026-10-16 在一个实际报规总图副本中验证：用地、道路、绿地、停车 Area 的明确分类和属性写入，保存重开回读一致，重复分类无新增变更。停车缺失分支可从已判定的连续车位组创建闭合 Area，插件识别为停车位；此分支通过回滚测试，未给已有 Area 的交付副本增加重复对象。

同日的连通性实验将一个含 10 个分离体的绿地楼板重建为 10 个原生楼板，连续且有孔洞的道路保留 1 个。真实曲线、解析面积与整体形状保持一致，每块重新计算插件投影面积，保存重开验证通过。更新后的盘点脚本能识别拆前 10 个分离体及拆后各 1 个连通体，重复分类无变更。

该实验的原生 IFC 有 10 个独立绿地对象及 10 份正确面积属性。IfcOpenShell 仅能解析其中 9 块几何；逐项核对拆前合并对象的 10 个挤出体，同一片区亦解析失败。整体对象解析成功可能静默遗漏某个子体，因此应逐实体核验，不能用聚合对象成功或 Pset 存在替代几何完整性验证。该导出问题未在标准化实验中修复。

首次实验所用 0.4.2 插件（DLL SHA256：`dfcb805c80a0d20e89ae19c02cf966e07e0fc407f9938475bf17c87004ed07e0`）实际输出的 IfcSpace 数为 0，即使停车 Area 已写入和识别；用地及绿地的非空 Pset 值成功导出。该记录属于旧构建，不能仅凭版本号将其解释为当前导出结果。

2026-10-16 主会话后续修正原生导出：显式传入 `ElementsForExport` 并设置 `ExportRoomsInView`，回执通过原生导出 GUID 对应没有 Tag 的 IfcSpace。已读取的三组实机回执分别覆盖完整目标集合、仅停车 Area、仅楼板；前两组均有 1 个停车 IfcSpace 和直接关联的停车属性集，5 项已有属性的值与类型、约 261.249260345㎡ 的几何面积通过核对，均未后处理 IFC。修正版 DLL SHA256 为 `215cb046d7c34b81ed7ae588182c175c455d7e3bbdaa06673b9d9a48b75cabfd`，安装回执确认文件已更新；该回执的 `host_reloaded=false`，安装后新宿主加载仍需另行核实。

后续停车导出测试使用首次标准化副本，并非含 10 块独立绿地的最终副本；不得将两轮结果拼接为最终副本已完整通过新导出链。停车边界仍需复核，缺少来源的业务字段保留为空，第 05 块绿地的 IFC 几何解析问题仍未解决。没有正式平台回执就不能声称报规通过。每次使用应重新核对宿主实际 DLL 与导出结果，不通过伪造体量或后处理 IFC 掩盖缺口。
