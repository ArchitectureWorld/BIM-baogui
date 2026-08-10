# BIMBaoGui v0.9.0 Revit 2020 验收清单

本清单供 Task12 实机验收使用。开始前保持所有项目未勾选，并使用指定模型：

```text
D:\18_建模项目\2026.07_湖北银行报规\3D\20260731test02.rvt
```

## 构建与部署

- [ ] Python 全量合同测试通过，并单独记录规则包编译与运行时打包合同结果。
- [ ] Core 全量测试通过；Production Release 构建为 0 警告、0 错误。
- [ ] `artifact-manifest.json` 中的 commit SHA、程序集版本、文件大小和 GHA SHA-256 与本次构建一致。
- [ ] 部署前已关闭 Revit、Rhino.Inside.Revit 和 Grasshopper。
- [ ] 将固定名 `BIMBaoGui.Stage01.gha` 直接覆盖到 `%APPDATA%\Grasshopper\Libraries\BIMbaogui`。
- [ ] 活动目录只有一个 `BIMBaoGui.Stage01.gha`，且没有 `.bak` 或 `.backup` 插件备份。
- [ ] 部署后的 GHA SHA-256 与 `artifact-manifest.json` 一致；回滚只依靠 Git 目标提交重新构建。

## 三个公开组件

- [ ] Grasshopper 当前公开菜单中恰好可见以下三个组件：
  - `湖北BIM报规｜01 文件初始化`
  - `湖北BIM报规｜02 构件与属性准备`
  - `湖北BIM报规｜03 检测、导出与 H-IFC 转译`
- [ ] 三个组件均能放置、连接和求解，名称与编号无重复或缺失。

## Stage01 文件初始化

- [ ] 在指定 RVT 中先记录既有项目身份、坐标 X / Y、高程、真北和项目条件，再执行初始化。
- [ ] 初始化后上述身份与空间基准符合预期，既有坐标 X / Y、高程、真北行为没有回归。
- [ ] 输出的 FileContext 包含当前单一规则包的 `packageId / version / hash`，且与运行时加载身份一致。
- [ ] Stage01 写入、回读、FileGuid、PayloadHash 和 WorkflowVersion 均一致。
- [ ] 保存 RVT 后关闭并重新打开，Stage01 初始化记录仍可读取。

## Stage02 构件与属性准备

- [ ] 连接本次 Stage01 输出的 FileContext，按项目条件获得所需载体、字段和建议值预览。
- [ ] 分别核对选择、交互点选、当前选择或项目信息模式的目标证据，确认后只执行一次写入。
- [ ] 安装后的共享参数在 Revit UI 可见、可编辑，绑定范围与预览一致。
- [ ] 在项目信息属性面板 GUID 哨兵参数 `建筑名称`（`4225a5de-c942-54aa-874a-28a1e67ce39c`）中直接写入固定值 `HBR-S2-PROJECT-SENTINEL-v090`。
- [ ] 在至少一个实例/类型属性面板 GUID 哨兵参数中完成实例绑定验证：选择一个楼层实例，在 `楼层属性信息`（`7dc1a82e-f3d0-5210-b3bf-6b517da25d80`，`bindingScope=INSTANCE`）中直接写入固定值 `HBR-S2-INSTANCE-SENTINEL-v090`。
- [ ] 建议值与两个固定哨兵写入后执行“保存 → 关闭 → 重新打开”；重开后 GUID 回读显示值必须与保存前逐字一致，证明参数和值保持持久。
- [ ] 在原 RVT 生成预览后、确认写入前切换到另一份 RVT，验证切换 RVT 后旧预览失效：记录原 RVT DocumentFingerprint / previewHash 与切换后 RVT DocumentFingerprint；旧预览确认写入尝试必须明确显示 `结果过期`，且不得进入 Revit 写入队列。
- [ ] 预览或写入失败时，报告路径位于活动 GHA 同目录，并记录规则包身份与根因。

| Stage02 证据 | 要求 | 实际记录 |
|---|---|---|
| 保存前属性面板截图路径 / SHA-256 | 同时显示项目参数 GUID、楼层实例参数 GUID 及两个固定哨兵值 |  |
| 重开后属性面板截图路径 / SHA-256 | 同时显示相同 GUID 的重开后 GUID 回读显示值 |  |
| 保存、关闭、重新打开时间 | 记录三个动作的本机时间（UTC+08:00） |  |
| 原 RVT 路径 | 记录生成预览时的绝对路径、DocumentFingerprint / previewHash |  |
| 切换后 RVT 路径 | 记录确认写入尝试时的绝对路径、DocumentFingerprint |  |
| 切换后 GH 状态截图路径 / SHA-256 | 截图必须清晰显示 `结果过期` |  |
| 旧预览确认写入尝试结果 | 记录未进入 Revit 写入队列的可核验证据 |  |

## Stage03 Strict

- [ ] 将 Grasshopper `Boolean Toggle` 接到“全部通过才导出”：`true`（默认值）= Strict，所有活动业务阻断处理完才导出；`false` = Force 测试放行。
- [ ] Force 时，将非空 `Panel` 文本接到“强制原因”；技术致命错误始终阻断，Force 不可绕过。
- [ ] “执行”建议接 `Button`。切换模式、强制原因、输出目录或其他输入后，将“执行”先复位为 `false`，再产生 `false → true` 上升沿重新运行。
- [ ] 卡片显示 Strict / Force、字段计数、运行状态，以及 RAW IFC、HIFC-MVD IFC 和 fields JSON 三条路径。
- [ ] 全模型扫描能分别报告缺构件、名称不匹配、缺参数、空值和未分类。
- [ ] 制造一个活动业务阻断后以 Strict 执行，只生成 `-fields.json`，不生成 `-RAW.ifc` 或 `-HIFC-MVD.ifc`。
- [ ] `STRICT_CLEAN_EXPORT` 仅在权威分类完成后适用：届时处理全部活动业务阻断后再次以 Strict 执行，通过 Autodesk Revit 标准 IFC4 导出并得到 `-RAW.ifc`、`-HIFC-MVD.ifc`、`-fields.json` 三件套；当前不得为达到该结果伪造分类。
- [ ] 记录 RAW IFC SHA-256；H-IFC 转译前后重新计算，RAW IFC SHA-256 保持不变。
- [ ] RAW 不被改写，已有目标不被覆盖，三件套共享同一 `runId` 与规则包 `packageId / version / hash`。

## Stage03 Force

- [ ] Force 未提供非空原因时确定性阻断，且不发布 IFC。
- [ ] 保留已知业务阻断并填写非空原因后执行 Force，得到独立的 `-RAW.ifc`、`-HIFC-MVD.ifc`、`-fields.json` 三件套。
- [ ] fields JSON 记录 `forced=true`、去除首尾空白后的记录原因和全部业务阻断。
- [ ] 注入技术致命错误后，Force 仍然阻断，不绕过导出、I/O、规则身份或转译完整性错误。

## 重启持久性与失败证据

- [ ] 保存、关闭并重新打开指定 RVT 后，Stage01 身份和 Stage02 Revit UI 可见、可编辑参数及其值仍存在。
- [ ] Stage02、Stage03 失败报告都写入活动 GHA 同目录，名称、时间戳、根因和规则包身份完整。
- [ ] Stage03 在 RAW 已成功导出后的后续失败会保留 RAW 和诊断证据，不留下被宣称为成功的最终文件。
- [ ] 截图、Revit journal、失败报告、三件套路径、时间戳与全部 SHA-256 已集中归档。

## 通用证据记录

以下记录栏保持空白，等待 Task12 实机回填；不得预填成功结论。

| 项目 | 实际记录 |
| --- | --- |
| Git commit SHA |  |
| GHA 路径 / SHA-256 |  |
| 指定 RVT 保存前 / 保存后 SHA-256 |  |
| Stage01 FileContext `packageId / version / hash` |  |
| Stage02 参数截图 |  |
| Revit journal / 截图目录 |  |

## Stage03 分场景证据记录

当前 v0.9.0 强制执行且分别留证的四个场景为：`STRICT_BLOCKED`、`FORCE_EMPTY_REASON`、`FORCE_BUSINESS_BYPASS`、`FORCE_TECHNICAL_FATAL`。这些场景的 `runId`、产物路径、哈希和实际结果不得复用。

`STRICT_CLEAN_EXPORT` 是条件场景：当前规则源 359/359 均为 `UNCLASSIFIED`，因此 Strict clean 确定不可达；不得伪造权威分类。该模板仅在权威分类完成后适用，不阻塞当前 v0.9.0 诚实验收，也不计入本轮强制完成项。

表中的“去除首尾空白后的记录原因”是 `forceReason.Trim()` 后写入 fields JSON 的记录值，不是未经处理的输入文本。不适用项必须在“实际记录”栏明确填写 `N/A`；当前所有实际记录槽位保持空白，严禁预填实机成功证据。

Stage03 failure report 证据槽只接受本场景同一 `runId` 的报告。Stage02 failure report 不使用 Stage03 `runId` 归属；只接受 `reportId`、`inputSignature`、`fileGuid`、`documentFingerprint`、规则包 `packageId / version / hash` 均匹配，且 `occurredUtc / occurredLocal` 落在本场景时间窗内的报告。不得引用其他场景或通用区记录替代。路径为 `N/A` 时对应 SHA-256 也必须填 `N/A`；路径非 `N/A` 时必须填写该文件 SHA-256。

每个分类计数均从该场景自己的 fields JSON 回填。`NOT_EVALUATED` 为 0 也要记录；若非 0，必须在实际结果中补充说明。

### `STRICT_BLOCKED`

| 证据项 | 预期 | 实际记录 |
| --- | --- | --- |
| runId | 本场景独立且唯一 |  |
| mode | Strict |  |
| 去除首尾空白后的记录原因 | `N/A` |  |
| allowExport | `false` |  |
| RAW IFC 路径 | `N/A`，不得发布 |  |
| RAW IFC SHA-256 | `N/A`，不得发布 |  |
| HIFC-MVD IFC 路径 | `N/A`，不得发布 |  |
| HIFC-MVD IFC SHA-256 | `N/A`，不得发布 |  |
| fields JSON 路径 | 本场景独立路径 |  |
| fields JSON SHA-256 | 本场景文件哈希 |  |
| Stage02 failure report 路径 | 本场景 Stage02 无技术失败时填 `N/A`；若实际失败，仅记录 `reportId`、`inputSignature`、`fileGuid`、`documentFingerprint`、`packageId / version / hash` 均匹配且 `occurredUtc / occurredLocal` 落在本场景时间窗内的报告，并判为预期偏差 |  |
| Stage02 failure report SHA-256 | Stage02 路径为 `N/A` 时填 `N/A`；否则填写 `reportId`、`inputSignature`、`fileGuid`、`documentFingerprint`、`packageId / version / hash` 均匹配且 `occurredUtc / occurredLocal` 落在本场景时间窗内的报告哈希 |  |
| Stage03 failure report 路径 | 预期为业务阻断且无技术失败，填 `N/A`；若实际失败，仅记录本场景 `runId` 报告并判为预期偏差 |  |
| Stage03 failure report SHA-256 | Stage03 路径为 `N/A` 时填 `N/A`；否则填写本场景 `runId` 报告哈希 |  |
| 预期结果 | 活动业务阻断导致仅发布 fields JSON |  |
| 实际结果 | 与预期逐项比较 |  |

| Stage03FieldStatus | 实际数量 |
| --- | --- |
| `PASS` |  |
| `NOT_APPLICABLE` |  |
| `MISSING_CARRIER` |  |
| `CARRIER_CATEGORY_MISMATCH` |  |
| `CARRIER_NAME_MISMATCH` |  |
| `AMBIGUOUS_CARRIER` |  |
| `MISSING_PARAMETER` |  |
| `EMPTY_REQUIRED_VALUE` |  |
| `INVALID_VALUE` |  |
| `RULE_NOT_IMPLEMENTED` |  |
| `UNCLASSIFIED_REQUIREMENT` |  |
| `IFC_OWNER_NOT_FOUND` |  |
| `IFC_VALUE_MISMATCH` |  |
| `NOT_EVALUATED` |  |

### `STRICT_CLEAN_EXPORT`（条件场景）

本节证据模板仅在权威分类完成后适用。当前 359/359 规则均为 `UNCLASSIFIED`，不得通过伪造分类制造 Strict clean 成功证据；本节留空不阻塞当前 v0.9.0 诚实验收。

| 证据项 | 预期 | 实际记录 |
| --- | --- | --- |
| runId | 本场景独立且唯一 |  |
| mode | Strict |  |
| 去除首尾空白后的记录原因 | `N/A` |  |
| allowExport | `true` |  |
| 源 RVT 路径 | 本场景 Stage03 使用的绝对路径 |  |
| Stage03 执行开始前源 RVT SHA-256 | 任何扫描、Revit IFC 导出或 H-IFC 转译开始前重新计算 |  |
| Stage03 执行结束后源 RVT SHA-256 | 三件套发布并结束本次 Stage03 后重新计算，必须与开始前一致 |  |
| RAW IFC 路径 | 本场景独立路径 |  |
| RAW IFC SHA-256 | 导出后与转译后保持一致 |  |
| RAW IFC 转译开始前 SHA-256 | H-IFC 转译开始前重新计算 |  |
| RAW IFC 转译结束后 SHA-256 | H-IFC 转译结束后重新计算，必须与开始前一致 |  |
| HIFC-MVD IFC 路径 | 本场景独立路径 |  |
| HIFC-MVD IFC SHA-256 | 本场景文件哈希 |  |
| fields JSON 路径 | 本场景独立路径 |  |
| fields JSON SHA-256 | 本场景文件哈希 |  |
| Stage02 failure report 路径 | 本场景 Stage02 无技术失败时填 `N/A`；若实际失败，仅记录 `reportId`、`inputSignature`、`fileGuid`、`documentFingerprint`、`packageId / version / hash` 均匹配且 `occurredUtc / occurredLocal` 落在本场景时间窗内的报告，并判为预期偏差 |  |
| Stage02 failure report SHA-256 | Stage02 路径为 `N/A` 时填 `N/A`；否则填写 `reportId`、`inputSignature`、`fileGuid`、`documentFingerprint`、`packageId / version / hash` 均匹配且 `occurredUtc / occurredLocal` 落在本场景时间窗内的报告哈希 |  |
| Stage03 failure report 路径 | 预期成功且无技术失败，填 `N/A`；若实际失败，仅记录本场景 `runId` 报告并判为预期偏差 |  |
| Stage03 failure report SHA-256 | Stage03 路径为 `N/A` 时填 `N/A`；否则填写本场景 `runId` 报告哈希 |  |
| 预期结果 | 无活动业务阻断并发布三件套 |  |
| 实际结果 | 与预期逐项比较 |  |

以下三行必须使用本场景同一 `runId` 的 final HIFC-MVD IFC；Revit 显示值和显示单位从属性面板实读，final IFC 值从目标实体/Pset 实读，并逐字或按明确单位进行数值对照。任一不一致均不得判为通过。

| 字段 | Revit 参数 GUID | Revit 显示值 | Revit 显示单位 | final IFC 实体 | final IFC Pset | final IFC 属性 | final IFC 类型 | final IFC 值 | 对照结论 |
|---|---|---|---|---|---|---|---|---|---|
| 基点坐标 X | `6b407894-09d4-529a-9f9f-a031219cdeaa` |  |  | `IfcProject` | `Pset_申报信息属性集` | `基点坐标X` | `IfcReal` |  |  |
| 基点坐标 Y | `1a64ef8d-e97c-5fa1-b53f-52b969b6198a` |  |  | `IfcProject` | `Pset_申报信息属性集` | `基点坐标Y` | `IfcReal` |  |  |
| 基点高程 | `50164757-c346-5005-a1b8-7b423c6b8de5` |  |  | `IfcProject` | `Pset_申报信息属性集` | `基点高程` | `IfcReal` |  |  |

| Stage03FieldStatus | 实际数量 |
| --- | --- |
| `PASS` |  |
| `NOT_APPLICABLE` |  |
| `MISSING_CARRIER` |  |
| `CARRIER_CATEGORY_MISMATCH` |  |
| `CARRIER_NAME_MISMATCH` |  |
| `AMBIGUOUS_CARRIER` |  |
| `MISSING_PARAMETER` |  |
| `EMPTY_REQUIRED_VALUE` |  |
| `INVALID_VALUE` |  |
| `RULE_NOT_IMPLEMENTED` |  |
| `UNCLASSIFIED_REQUIREMENT` |  |
| `IFC_OWNER_NOT_FOUND` |  |
| `IFC_VALUE_MISMATCH` |  |
| `NOT_EVALUATED` |  |

### `FORCE_EMPTY_REASON`

本场景必须生成与本场景 `runId` 绑定的独立 fields JSON，并回填路径与 SHA-256。仅 fields JSON 报告写入本身发生技术失败时，才在“实际结果”和本场景 Stage03 failure report 槽说明偏差；不得将该证据标为“不适用”。

| 证据项 | 预期 | 实际记录 |
| --- | --- | --- |
| runId | 本场景独立且唯一 |  |
| mode | Force |  |
| 去除首尾空白后的记录原因 | 去除首尾空白后为空 |  |
| allowExport | `false` |  |
| RAW IFC 路径 | `N/A`，不得发布 |  |
| RAW IFC SHA-256 | `N/A`，不得发布 |  |
| HIFC-MVD IFC 路径 | `N/A`，不得发布 |  |
| HIFC-MVD IFC SHA-256 | `N/A`，不得发布 |  |
| fields JSON 路径 | 本场景独立路径；必须回填 |  |
| fields JSON SHA-256 | 本场景文件哈希；必须回填 |  |
| Stage02 failure report 路径 | 本场景 Stage02 无技术失败时填 `N/A`；若实际失败，仅记录 `reportId`、`inputSignature`、`fileGuid`、`documentFingerprint`、`packageId / version / hash` 均匹配且 `occurredUtc / occurredLocal` 落在本场景时间窗内的报告，并判为预期偏差 |  |
| Stage02 failure report SHA-256 | Stage02 路径为 `N/A` 时填 `N/A`；否则填写 `reportId`、`inputSignature`、`fileGuid`、`documentFingerprint`、`packageId / version / hash` 均匹配且 `occurredUtc / occurredLocal` 落在本场景时间窗内的报告哈希 |  |
| Stage03 failure report 路径 | 预期为 Force 原因业务阻断且无技术失败，填 `N/A`；若实际失败，仅记录本场景 `runId` 报告并判为预期偏差 |  |
| Stage03 failure report SHA-256 | Stage03 路径为 `N/A` 时填 `N/A`；否则填写本场景 `runId` 报告哈希 |  |
| 预期结果 | Force 因记录原因为空而确定性阻断 |  |
| 实际结果 | 与预期逐项比较 |  |

| Stage03FieldStatus | 实际数量 |
| --- | --- |
| `PASS` |  |
| `NOT_APPLICABLE` |  |
| `MISSING_CARRIER` |  |
| `CARRIER_CATEGORY_MISMATCH` |  |
| `CARRIER_NAME_MISMATCH` |  |
| `AMBIGUOUS_CARRIER` |  |
| `MISSING_PARAMETER` |  |
| `EMPTY_REQUIRED_VALUE` |  |
| `INVALID_VALUE` |  |
| `RULE_NOT_IMPLEMENTED` |  |
| `UNCLASSIFIED_REQUIREMENT` |  |
| `IFC_OWNER_NOT_FOUND` |  |
| `IFC_VALUE_MISMATCH` |  |
| `NOT_EVALUATED` |  |

### `FORCE_BUSINESS_BYPASS`

| 证据项 | 预期 | 实际记录 |
| --- | --- | --- |
| runId | 本场景独立且唯一 |  |
| mode | Force |  |
| 去除首尾空白后的记录原因 | 非空且已去除首尾空白 |  |
| allowExport | `true` |  |
| 源 RVT 路径 | 本场景 Stage03 使用的绝对路径 |  |
| Stage03 执行开始前源 RVT SHA-256 | 任何扫描、Revit IFC 导出或 H-IFC 转译开始前重新计算 |  |
| Stage03 执行结束后源 RVT SHA-256 | 三件套发布并结束本次 Stage03 后重新计算，必须与开始前一致 |  |
| RAW IFC 路径 | 本场景独立路径 |  |
| RAW IFC SHA-256 | 导出后与转译后保持一致 |  |
| RAW IFC 转译开始前 SHA-256 | H-IFC 转译开始前重新计算 |  |
| RAW IFC 转译结束后 SHA-256 | H-IFC 转译结束后重新计算，必须与开始前一致 |  |
| HIFC-MVD IFC 路径 | 本场景独立路径 |  |
| HIFC-MVD IFC SHA-256 | 本场景文件哈希 |  |
| fields JSON 路径 | 本场景独立路径 |  |
| fields JSON SHA-256 | 本场景文件哈希 |  |
| Stage02 failure report 路径 | 本场景 Stage02 无技术失败时填 `N/A`；若实际失败，仅记录 `reportId`、`inputSignature`、`fileGuid`、`documentFingerprint`、`packageId / version / hash` 均匹配且 `occurredUtc / occurredLocal` 落在本场景时间窗内的报告，并判为预期偏差 |  |
| Stage02 failure report SHA-256 | Stage02 路径为 `N/A` 时填 `N/A`；否则填写 `reportId`、`inputSignature`、`fileGuid`、`documentFingerprint`、`packageId / version / hash` 均匹配且 `occurredUtc / occurredLocal` 落在本场景时间窗内的报告哈希 |  |
| Stage03 failure report 路径 | 预期成功且无技术失败，填 `N/A`；若实际失败，仅记录本场景 `runId` 报告并判为预期偏差 |  |
| Stage03 failure report SHA-256 | Stage03 路径为 `N/A` 时填 `N/A`；否则填写本场景 `runId` 报告哈希 |  |
| 预期结果 | 仅绕过业务阻断并发布三件套 |  |
| 实际结果 | 与预期逐项比较 |  |

以下三行必须使用本场景同一 `runId` 的 final HIFC-MVD IFC；Revit 显示值和显示单位从属性面板实读，final IFC 值从目标实体/Pset 实读，并逐字或按明确单位进行数值对照。任一不一致均不得判为通过。

| 字段 | Revit 参数 GUID | Revit 显示值 | Revit 显示单位 | final IFC 实体 | final IFC Pset | final IFC 属性 | final IFC 类型 | final IFC 值 | 对照结论 |
|---|---|---|---|---|---|---|---|---|---|
| 基点坐标 X | `6b407894-09d4-529a-9f9f-a031219cdeaa` |  |  | `IfcProject` | `Pset_申报信息属性集` | `基点坐标X` | `IfcReal` |  |  |
| 基点坐标 Y | `1a64ef8d-e97c-5fa1-b53f-52b969b6198a` |  |  | `IfcProject` | `Pset_申报信息属性集` | `基点坐标Y` | `IfcReal` |  |  |
| 基点高程 | `50164757-c346-5005-a1b8-7b423c6b8de5` |  |  | `IfcProject` | `Pset_申报信息属性集` | `基点高程` | `IfcReal` |  |  |

| Stage03FieldStatus | 实际数量 |
| --- | --- |
| `PASS` |  |
| `NOT_APPLICABLE` |  |
| `MISSING_CARRIER` |  |
| `CARRIER_CATEGORY_MISMATCH` |  |
| `CARRIER_NAME_MISMATCH` |  |
| `AMBIGUOUS_CARRIER` |  |
| `MISSING_PARAMETER` |  |
| `EMPTY_REQUIRED_VALUE` |  |
| `INVALID_VALUE` |  |
| `RULE_NOT_IMPLEMENTED` |  |
| `UNCLASSIFIED_REQUIREMENT` |  |
| `IFC_OWNER_NOT_FOUND` |  |
| `IFC_VALUE_MISMATCH` |  |
| `NOT_EVALUATED` |  |

### `FORCE_TECHNICAL_FATAL`

| 证据项 | 预期 | 实际记录 |
| --- | --- | --- |
| runId | 本场景独立且唯一 |  |
| mode | Force |  |
| 去除首尾空白后的记录原因 | 非空且已去除首尾空白 |  |
| allowExport | `false` |  |
| RAW IFC 路径 | 按失败阶段回填，不适用填 `N/A` |  |
| RAW IFC SHA-256 | 按失败阶段回填，不适用填 `N/A` |  |
| HIFC-MVD IFC 路径 | 不得冒充成功，不适用填 `N/A` |  |
| HIFC-MVD IFC SHA-256 | 不得冒充成功，不适用填 `N/A` |  |
| fields JSON 路径 | 按失败阶段回填，不适用填 `N/A` |  |
| fields JSON SHA-256 | 按失败阶段回填，不适用填 `N/A` |  |
| Stage02 failure report 路径 | 本场景 Stage02 无技术失败时填 `N/A`；若实际失败，仅记录 `reportId`、`inputSignature`、`fileGuid`、`documentFingerprint`、`packageId / version / hash` 均匹配且 `occurredUtc / occurredLocal` 落在本场景时间窗内的报告，并判为额外偏差 |  |
| Stage02 failure report SHA-256 | Stage02 路径为 `N/A` 时填 `N/A`；否则填写 `reportId`、`inputSignature`、`fileGuid`、`documentFingerprint`、`packageId / version / hash` 均匹配且 `occurredUtc / occurredLocal` 落在本场景时间窗内的报告哈希 |  |
| Stage03 failure report 路径 | 必须记录本场景 `runId` 的技术致命失败报告；禁止填 `N/A` |  |
| Stage03 failure report SHA-256 | 必须记录本场景 `runId` 报告哈希；禁止填 `N/A` |  |
| 预期结果 | 技术致命错误不可被 Force 绕过 |  |
| 实际结果 | 与预期逐项比较 |  |

| Stage03FieldStatus | 实际数量 |
| --- | --- |
| `PASS` |  |
| `NOT_APPLICABLE` |  |
| `MISSING_CARRIER` |  |
| `CARRIER_CATEGORY_MISMATCH` |  |
| `CARRIER_NAME_MISMATCH` |  |
| `AMBIGUOUS_CARRIER` |  |
| `MISSING_PARAMETER` |  |
| `EMPTY_REQUIRED_VALUE` |  |
| `INVALID_VALUE` |  |
| `RULE_NOT_IMPLEMENTED` |  |
| `UNCLASSIFIED_REQUIREMENT` |  |
| `IFC_OWNER_NOT_FOUND` |  |
| `IFC_VALUE_MISMATCH` |  |
| `NOT_EVALUATED` |  |

## IFC owner 策略

状态只允许在实机运行后回填“已验证”“未验证”或 `N/A`，并关联该场景的检查证据；不得预填成功。

| owner 策略 | STRICT_CLEAN_EXPORT 实际状态 | FORCE_BUSINESS_BYPASS 实际状态 | 证据路径 / SHA-256 |
| --- | --- | --- | --- |
| `BY_EXPORT_GUID` |  |  |  |
| `SINGLE_ENTITY_BY_TYPE` |  |  |  |

只有当前强制项目在指定 RVT 上逐项完成并留证后，才能声明本次 Revit 2020 实机验收完成；`STRICT_CLEAN_EXPORT` 条件场景在权威分类完成前不计入本轮完成门槛。
