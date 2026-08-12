# Revit 2020 原生插件 Stage03：H-IFC 导出与 IFCFlux 人工验收设计

## 1. 目标与产品边界

在现有唯一 Revit 产品线 `feat/revit-native-addin-mcp-v0.3` 中增加完整 Stage03，使用户能够从已完成 Stage01/Stage02 的 RVT 直接生成可交给 IFCFlux 人工检查的 H-IFC 文件。

本阶段自动完成：

```text
RVT 现场扫描
→ 字段与载体预检
→ Autodesk IFC4 RAW 导出
→ H-IFC 转译与属性补全
→ STEP 语法复读
→ Entity / Owner / Pset / Property / IFC 类型 / 值 / 单位精确回读
→ 输出正式或强制测试 H-IFC
→ 输出字段级 JSON 与运行证据
→ 标记 IFCFLUX_MANUAL_PENDING
```

IFCFlux 无 API，因此插件不伪造 IFCFlux 自动通过结果。最终外部识别由用户手工导入 IFCFlux 完成。

产品版本升级为 `0.4.0`。不新建产品分支，不保留普通版/MCP 版两套安装包，不修改 GHA 产品线。

## 2. 实现独立性

原生插件继续与 GHA 相对独立：

- 原生 Stage03 不引用 `Grasshopper.dll`、`RhinoCommon.dll`、`RhinoInside.Revit.dll` 或 `.gha`；
- 不运行 GH Component，也不依赖 GH 的状态机；
- 继续消费同一份权威 HBR 规则数据库、参数 GUID、IFC Entity/Pset/Property/type/owner strategy；
- 旧 GHA Stage03 代码只作为算法与故障证据参考，原生产品内建立独立命名空间与原生 ExternalEvent 调度；
- 现有 Stage01、Stage02 的人工入口与 MCP 行为保持不变。

## 3. 用户工作流

### 3.1 原生工作台

左侧第三个目录由占位页替换为真实页面：

```text
03 检测与 H-IFC
```

页面包含：

1. 当前文件、Stage01 Payload Hash、RulePack 身份；
2. 输出目录选择；
3. 模式选择：严格模式 / 强制测试模式；
4. 强制原因输入框，仅强制模式可用且必填；
5. 操作按钮：
   - 扫描与预检；
   - 导出并转译；
   - 重新校验已有结果；
   - 打开输出目录；
6. 汇总卡片：构件数、字段数、阻断数、警告数、可导出数；
7. 问题列表：构件、角色、属性路径、状态、原因；
8. 固定高度并可内部滚动的运行报告区；
9. 输出文件卡片：RAW IFC、H-IFC、字段报告、验收报告、失败报告。

### 3.2 推荐操作顺序

```text
扫描与预检
→ 检查问题
→ 选择严格或强制测试
→ 确认导出
→ 取得 H-IFC 与报告
→ 手动导入 IFCFlux
```

## 4. 严格模式与强制测试模式

### 4.1 严格模式（默认）

存在任何可阻断业务问题时，不发布正式 H-IFC：

- Stage01 未初始化或 Payload/Hash/规则身份不一致；
- 项目条件未明确声明；
- Stage02 相关必填参数缺失；
- 参数 GUID、StorageType、绑定范围或 Revit 参数类型冲突；
- 构件角色无法唯一确认；
- 必填属性空值或值格式错误；
- 预检与导出前现场模型证据不一致；
- H-IFC 精确回读未通过。

严格模式可以生成预检报告；只有内部校验通过时才发布正式 H-IFC。

### 4.2 强制测试模式

允许在存在可强制业务阻断时生成局部 H-IFC，用于 IFCFlux 定位：

- 必须填写强制原因；
- 文件名带 `_FORCED_TEST`；
- 报告明确列出被跳过、缺失或无法挂接的字段；
- 结果状态不能标记为正式通过；
- 未解析到唯一 Owner 的字段不猜测挂接，跳过并报告；
- 不向 H-IFC 内额外写入测试水印 Pset，避免污染外部识别。

### 4.3 不可强制的技术错误

以下错误即使在强制模式也必须停止：

- Revit 2020 环境不满足、没有活动项目文档、RVT 未保存；
- 输出目录不可写或目标文件冲突；
- Autodesk IFC4 RAW 导出失败；
- RAW IFC 缺失、为空或 SHA-256 无法计算；
- RAW STEP 语法不可解析或不是支持的 IFC schema；
- H-IFC 变更器内部异常；
- candidate 写盘失败；
- candidate 无法复读；
- final 文件精确回读失败；
- RAW 文件在转译过程中被修改；
- 正式发布和原子替换失败。

## 5. 数据来源与现场扫描

Stage03 不相信缓存的 Stage02 Preview，必须重新读取当前 Revit 文档：

1. 读取 Stage01 Extensible Storage、canonical payload、payload hash、FileGuid；
2. 验证项目条件声明；
3. 从权威规则包计算当前模型 Profile 下的适用角色与字段；
4. 扫描当前文档中规则涉及的模型元素；
5. 依据 Revit 类别、ElementKind、数据库批准的精确别名和显式角色匹配载体；
6. 按固定 GUID 读取 INSTANCE/TYPE 参数；
7. 转换为 canonical value；
8. 冻结 DocumentFingerprint 和 `scan_hash`。

`ProjectInformation` 继续作为 `IfcProject + IfcSite + IfcBuilding` 的批准多角色载体。

## 6. RAW IFC 导出

使用 Revit 2020 原生 `Document.Export` / `IFCExportOptions` 输出 Autodesk IFC4 RAW，不依赖官方 H-IFC 插件。

固定要求：

- IFC4；
- 当前完整项目文档；
- 输出前保存状态与 DocumentFingerprint 再检查；
- 使用独立临时工作目录；
- RAW 文件只读进入后处理，不在原文件上直接修改；
- 导出后记录绝对路径、文件大小、UTC 时间和 SHA-256；
- 转译结束后再次核对 RAW SHA-256，必须完全不变。

## 7. H-IFC 转译

### 7.1 Owner 定位

按规则数据库的 owner strategy 定位 IFC Owner：

- `SINGLE_ENTITY_BY_TYPE`：IfcProject / IfcSite / IfcBuilding；
- Revit 构件：优先稳定 IFC GlobalId / Revit UniqueId 对应关系；
- 多候选或无候选：严格模式阻断，强制模式跳过并报告；
- 不使用模糊名称猜测 Owner。

### 7.2 Pset 与 Property

对每个适用字段：

- 使用数据库规定的 `IfcEntity / PropertySet / Property / declaredIfcType / canonicalUnit`；
- 找到已有同名 Pset/Property 时执行类型与值核对；
- 缺失时创建；
- 冲突时不得静默覆盖不兼容类型；
- 使用确定性 IFC GUID，保证同一文档、同一 Owner、同一 Pset 重复运行结果稳定；
- 同一 Owner/Pset 不创建重复关系。

### 7.3 序列化与发布

```text
RAW.ifc
→ 内存解析
→ enrich
→ candidate.ifc
→ candidate 复读与精确检查
→ final.ifc 原子发布
```

candidate 失败时保留在隔离目录，不能覆盖正式输出。

## 8. 内部验收与状态

内部验收必须逐字段检查：

- Owner entity type；
- Owner GlobalId；
- PropertySet 名称；
- Property 名称；
- IFC value type；
- canonical value；
- unit；
- relationship 是否存在；
- 是否重复；
- 实际值是否与 Revit 现场证据一致。

顶层状态：

```text
INTERNAL_VALIDATED
INTERNAL_FAILED
IFCFLUX_MANUAL_PENDING
```

成功生成文件时，内部结果为 `INTERNAL_VALIDATED` 或强制测试的内部部分通过状态，同时外部状态固定为 `IFCFLUX_MANUAL_PENDING`。

插件不能自行产生 `IFCFLUX_PASSED`。

## 9. 输出文件与命名

每次运行创建独立 run 目录，避免相互覆盖：

```text
<OutputRoot>/<RvtBaseName>_<yyyyMMdd-HHmmss>_<RunId>/
```

严格模式：

```text
<RvtBaseName>_RAW.ifc
<RvtBaseName>_HIFC.ifc
<RvtBaseName>_fields.json
<RvtBaseName>_validation.json
<RvtBaseName>_ifcflux-checklist.md
```

强制测试模式：

```text
<RvtBaseName>_RAW.ifc
<RvtBaseName>_HIFC_FORCED_TEST.ifc
<RvtBaseName>_fields_FORCED_TEST.json
<RvtBaseName>_validation_FORCED_TEST.json
<RvtBaseName>_ifcflux-checklist_FORCED_TEST.md
```

失败时额外输出：

```text
<RvtBaseName>_failure.json
quarantine/<candidate>.ifc
```

## 10. 报告合同

`validation.json` 至少包含：

- schemaVersion、productVersion、runId；
- mode、forceReason；
- Revit 版本、RVT 路径、文档指纹；
- FileGuid、Stage01 payload hash；
- RulePack ID/version/SHA-256；
- 扫描摘要和 `scan_hash`；
- RAW path/size/SHA-256；
- H-IFC path/size/SHA-256；
- internal status；
- IFCFlux status=`IFCFLUX_MANUAL_PENDING`；
- blocker/warning 列表；
- 每个输出文件的哈希；
- UTC 开始、结束时间。

`fields.json` 对每个字段记录：

- propertyId；
- Revit element id/unique id；
- carrier role；
- owner entity/global id；
- Pset/property；
- expected/actual IFC type；
- expected/actual canonical value；
- unit；
- status 与 message。

## 11. MCP 工具

在现有 9 个工具之外增加：

```text
bimbaogui_stage03_scan
bimbaogui_stage03_export
bimbaogui_stage03_get_last_result
bimbaogui_stage03_revalidate_file
```

安全规则：

- `stage03_scan` 只读，返回 `scan_hash` 与摘要；
- `stage03_export` 属于文件写操作，必须提供 `scan_hash`、`confirm=true`、输出目录、模式；
- 强制模式必须提供非空 `force_reason`；
- `scan_hash` 使用一次性、30 分钟租约；
- 导出开始前在 Revit 主线程重新扫描并比较 hash；
- MCP 只调用与人工界面相同的 Stage03 服务；
- 不开放任意 Revit API、任意脚本或任意文件写入工具。

## 12. 错误处理

错误码按阶段细分，避免把所有错误折叠为 `INVALID_IFC`：

```text
STAGE03_PRECONDITION_FAILED
STAGE03_SCAN_FAILED
STAGE03_SCAN_STALE
IFC_RAW_EXPORT_FAILED
IFC_RAW_MISSING
IFC_RAW_READ_FAILED
IFC_ENCODING_FAILED
IFC_STEP_PARSE_FAILED
IFC_SCHEMA_UNSUPPORTED
IFC_OWNER_UNRESOLVED
IFC_PSET_MUTATION_FAILED
IFC_CANDIDATE_WRITE_FAILED
IFC_CANDIDATE_REREAD_FAILED
IFC_EXACT_VALIDATION_FAILED
RAW_IFC_CHANGED
IFC_FINAL_PUBLISH_FAILED
OUTPUT_PATH_FAILED
FORCE_REASON_REQUIRED
```

UI 显示用户可操作说明；JSON 保留技术码、阶段、异常类型和堆栈摘要。

## 13. 测试与验收

### 13.1 自动化

- Stage01/Stage02 v0.3.2 非回归；
- 规则数据库身份和 359 个属性映射不漂移；
- 严格/强制门禁；
- 输出命名与不覆盖；
- STEP parser 与 serializer round-trip；
- Owner、Pset、Property、类型和值 exact 检查；
- ProjectInformation 多角色；
- RAW hash 不变；
- candidate 隔离；
- MCP 工具发现、租约与参数校验；
- Release 0 warning / 0 error；
- 安装、升级、探针、卸载 smoke。

### 13.2 Revit 2020 实机

必须在真实 RVT 中完成：

1. Stage01 已写入且项目条件已声明；
2. Stage02 已准备参数；
3. 严格预检；
4. 强制测试导出；
5. RAW 与 H-IFC 同时生成；
6. 关闭重开 RVT 后再次导出；
7. 比较字段报告、hash 和重复运行稳定性。

### 13.3 IFCFlux 人工验收

用户手动将最终 H-IFC 导入 IFCFlux，检查：

- 文件可打开且模型几何存在；
- IfcProject / IfcSite / IfcBuilding 层级；
- 典型构件 Owner；
- 典型 Pset/Property；
- 中文名称与值；
- Number/Integer/Boolean/Length/Area/Volume/Date/Identifier 等类型；
- 强制模式中跳过字段是否与报告一致。

首次实际测试至少抽查：项目、场地、建筑、墙、楼板、门窗、屋顶/楼梯以及当前模型真实存在的 GenericModel 载体。

## 14. 不在本次范围内

- 自动控制 IFCFlux；
- 伪造 IFCFlux 通过状态；
- 在线报规平台自动上传；
- 自动修复所有缺失业务值；
- 任意 C# 或任意 Revit API MCP 工具；
- GHA Stage03 重构；
- Revit 2021+ 多版本适配。

## 15. 完成定义

v0.4.0 只有在以下全部满足后才可交付：

1. Stage01、Stage02 人工与 MCP 功能非回归；
2. 原生 Stage03 页面可完成扫描、严格/强制导出、重新校验和打开目录；
3. 真实 Revit 2020 能生成 RAW IFC、最终 H-IFC、fields/validation/checklist；
4. final H-IFC 通过插件内部 exact 回读；
5. RAW hash 保持不变；
6. MCP Stage03 工具可发现且遵守确认租约；
7. 安装包可直接覆盖 v0.3.2；
8. 外部状态明确为 `IFCFLUX_MANUAL_PENDING`；
9. 用户可以拿最终文件进入 IFCFlux 开展第一次实际识别测试。
