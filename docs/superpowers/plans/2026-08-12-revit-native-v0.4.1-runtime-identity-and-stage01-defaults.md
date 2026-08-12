# BIMBaoGui Revit 原生插件 v0.4.1 修正版实施计划

> 本文替代同名文件的初版方案。修订重点不是扩大功能，而是消除三项会造成业务误判或数据损坏的设计风险：项目条件被默认代选、旧 Payload 在解码阶段被静默改写、不同字段使用同一个全局数据优先级。

## 0. 文档状态

| 项目 | 值 |
|---|---|
| 唯一 Revit 开发分支 | `feat/revit-native-addin-mcp-v0.3` |
| 修订基线 | `e57473d813f58e9e528b2afc69d31d4e32b602cf` |
| 当前已实现产品 | `0.4.0` |
| 本计划目标产品 | `0.4.1` |
| 目标宿主 | Autodesk Revit 2020 |
| Stage01 当前 Payload | `0.9.0` |
| Stage01 目标 Payload | `0.9.1` |
| 外部验收 | IFCFlux 人工检查 |

本文件是实施计划，不是完成证明。只有代码、测试、安装包和 Revit 2020 实机证据全部满足发布门禁后，才可称为 `0.4.1` 可安装测试版。

---

## 1. 修订后的不可变原则

### 1.1 项目条件必须是显式业务声明

项目条件决定 Stage02 的有条件构件与属性是否被激活，因此它不是普通界面便利项，而是项目事实。

必须遵守：

- 不得默认勾选“无上述项目条件（已确认）”；
- 新建表单中所有实际项目条件均为 `false`；
- 新建表单中 `workflow.project_conditions.none = false`；
- 用户必须主动选择一个或多个实际条件，或者主动选择“无上述项目条件（已确认）”；
- 旧 Payload 全部实际条件为 `false` 且缺少 `none` 时，旧 Payload 保持未声明；
- 未声明状态继续触发 `PROJECT_CONDITION_DECLARATION_MISSING`；
- 同时选择实际条件与 `none` 时继续触发 `PROJECT_CONDITION_DECLARATION_CONFLICT`；
- 人工入口与 MCP 入口调用同一套声明校验，不存在绕过路径。

项目条件交互仍保持双向互斥：

```text
用户勾选任一实际条件
→ 自动取消 none

用户主动勾选 none
→ 自动取消全部实际条件

用户取消最后一个实际条件且没有勾选 none
→ 回到未声明状态
```

互斥只处理用户已经作出的选择，不允许把“尚未填写”自动解释为“确认没有”。

### 1.2 Payload 迁移必须有版本边界

`0.4.1` 将 Stage01 Payload 协议从 `0.9.0` 升级到 `0.9.1`：

```csharp
internal const string PayloadSchemaVersion = "0.9.1";
```

升级关系固定为：

```text
0.9.0 → 0.9.1
```

`NativeStage01PayloadCodec.TryDecode` 必须保持纯函数边界：

- TryDecode 只负责语法解析和类型校验；
- 不得在 TryDecode 内补默认值、补条件声明或重写 canonical JSON；
- 解码成功不代表迁移成功；
- 解码不得改变输入 Payload 的任何业务含义。

稳定处理顺序为：

```text
读取原始 Storage
→ 校验原始 Payload SHA-256
→ 纯解码原始 Payload
→ 校验 Storage / envelope / values 的版本与 FileGuid 一致性
→ 判断 Current / MigratableLegacy / Corrupt / UnsupportedFuture
→ Current 执行当前版本 canonical 对账
→ MigratableLegacy 在独立迁移器中生成内存候选
→ 用户显式确认写入后发布 0.9.1 canonical Payload
```

必须先验证原始 Payload SHA-256 与 canonical 状态，再执行显式迁移。旧版本数据不得因为当前代码添加了新键而被错误判定为 `NON_CANONICAL_CURRENT_PAYLOAD`。

### 1.3 不使用全局数据优先级

不得使用一个全局的 RVT > Payload > 默认值优先级。不同字段的真实来源不同，必须由逐字段权威来源矩阵决定读取、比较、写入与漂移提示。

### 1.4 验收边界保持严格

- CI 通过不等于 Revit 2020 实机通过；
- 插件内部 exact 回读通过后，外部状态仍为 `IFCFLUX_MANUAL_PENDING`；
- 没有用户在 IFCFlux 中打开具体文件并保存证据时，不得宣称 IFCFlux 已通过；
- Stage02、Stage03、H-IFC 转译和 13 个 MCP 工具保持兼容；
- 本次不开放任意 C#、任意 Revit API、任意脚本或任意 Transaction 工具。

---

## 2. v0.4.1 目标

### 2.1 运行时身份可信

工作台顶部显示当前 Revit 进程实际加载的程序集身份：

```text
插件版本：0.4.1
构建号：GitHub Actions run number 或 local
Commit：当前构建 SHA 或 unknown
DLL 路径：当前已加载 BIMBaoGui.RevitAddin.dll 的绝对路径
```

来源只能是当前程序集：

- `AssemblyInformationalVersionAttribute`；
- `AssemblyFileVersionAttribute`；
- `AssemblyName.Version`；
- `AssemblyMetadataAttribute("HBR.BuildNumber", ...)`；
- `AssemblyMetadataAttribute("HBR.CommitSha", ...)`；
- `typeof(WorkspaceControl).Assembly.Location`。

README、安装目录常量和文件名不能作为运行时身份来源。

### 2.2 新文件初始化更易用，但不伪造事实

可确定的工作流配置可以预置：

| 字段 | 初始来源 | 规则 |
|---|---|---|
| 模型文件类型 | 当前规则目录默认 | 仅新建表单使用 |
| 模型范围 | 当前规则目录默认 | 仅新建表单使用 |
| 坐标系名称 | 当前规则目录默认 | 仅新建表单使用 |
| 高程系名称 | 当前规则目录默认 | 仅新建表单使用 |
| 长度单位 | 工作流目标单位 `m` | 属于标准化配置 |
| 面积单位 | 工作流目标单位 `m²` | 属于标准化配置 |
| 角度单位 | 工作流目标单位 `°` | 属于标准化配置 |

不能自动生成或替用户判断：

- 项目名称、项目编号、项目地址；
- 子项名称；
- 建设单位、设计单位及其他参建组织；
- 企业信用代码、联系人、联系电话；
- 项目条件声明；
- 任何需要专业判断的规划目标值。

X、Y、高程和真北不使用硬编码 `0` 覆盖模型。无 Stage01 记录时，应优先读取当前 Revit `ProjectPosition`；新建 Revit 文件通常会自然读到零值，但这个零值来自当前 RVT，而不是插件猜测。

---

## 3. 逐字段权威来源矩阵

### 3.1 字段分组

| 字段组 | 权威来源 | Stage01 Payload 的作用 | 固定 GUID 参数的作用 |
|---|---|---|---|
| X / 南北坐标 | `ProjectPosition.NorthSouth` | 保存上次确认值，用于漂移对账 | IFC 投影和回读证据 |
| Y / 东西坐标 | `ProjectPosition.EastWest` | 保存上次确认值，用于漂移对账 | IFC 投影和回读证据 |
| 基点高程 | `ProjectPosition.Elevation` | 保存上次确认值，用于漂移对账 | IFC 投影和回读证据 |
| 真北角度 | `ProjectPosition.Angle` | 保存上次确认值，用于漂移对账 | IFC 投影和回读证据 |
| 项目名称、项目编号 | `ProjectInformation.Name / Number` | 保存上次确认值，用于漂移对账 | IFC 投影和回读证据 |
| 子项名称、模型文件类型、模型范围 | Stage01 Payload | 业务权威记录 | 导出投影，不反向覆盖 Payload |
| 项目条件声明 | Stage01 Payload | 唯一业务权威记录 | 不作为反向来源 |
| 参建组织、规划目标 | Stage01 Payload | 唯一业务权威记录 | 导出投影和回读证据 |
| Stage02/Stage03 构件属性值 | 固定 GUID 参数 | 记录规则身份和流程上下文 | 实际导出值来源 |
| FileGuid、WorkflowVersion | 原生工作流内部策略 | 唯一持久记录 | 不作为业务字段 |
| 长度、面积、角度单位 | 目标工作流配置 + Revit Units 回读 | 保存标准化结果 | 类型与值转换依据 |

### 3.2 不同存储状态下的行为

#### NoRecord

```text
当前 RVT
→ 读取 ProjectInformation、ProjectPosition、Units
→ 只把这些 Revit 原生字段作为新表单初值
→ 业务字段保持规则默认或空值
→ 项目条件保持未声明
```

#### Current

```text
读取 0.9.1 Payload
→ 保留 Payload 中的业务输入
→ 同时读取当前 RVT 现场证据
→ 对 Revit 原生字段做差异比较
→ 显示 drift，不静默覆盖任一侧
```

用户重新提交 Stage01 时，写入操作才把已确认的表单值应用到 Revit，并执行事务后回读。

#### MigratableLegacy

```text
原始 0.9.0 数据先完成哈希和版本校验
→ 在独立迁移器中生成 0.9.1 内存候选
→ 保留全部非空业务值
→ 补齐新增条件键为 false
→ none 缺失时补为 false，保持未声明
→ 显示“等待用户确认迁移”
→ 用户提交后才写回 Storage
```

#### Corrupt / UnsupportedFuture

不创建迁移候选，不写入，不用默认值掩盖错误。

---

## 4. Stage01 Payload 0.9.1 迁移设计

### 4.1 新增类型

```text
src/BIMBaoGui.RevitAddin/Stage01/NativeStage01MigrationService.cs
```

建议接口：

```csharp
internal sealed class NativeStage01MigrationResult
{
  internal bool Success { get; }
  internal string SourceVersion { get; }
  internal string TargetVersion { get; }
  internal NativeStage01Model Model { get; }
  internal IReadOnlyList<string> Messages { get; }
}

internal static class NativeStage01MigrationService
{
  internal static NativeStage01MigrationResult Migrate(
    NativeStage01Payload source,
    NativeRuleCatalog catalog,
    string targetVersion);
}
```

### 4.2 迁移允许做的事

- 把 `workflowVersion` 更新为 `0.9.1`；
- 补齐规则包新增的实际条件键，值固定为 `false`；
- 缺少 `workflow.project_conditions.none` 时补为 `false`；
- 保留未知扩展键，除非其结构违反既有 Payload 合同；
- 生成确定性迁移消息和迁移前后哈希。

### 4.3 迁移禁止做的事

- 不得默认确认“无上述项目条件”；
- 不得把缺失的项目名称、编号、子项或组织信息补成猜测值；
- 不得用 Revit 当前值直接改写旧 Payload 的业务记录；
- 不得在读取阶段覆盖原 Storage；
- 不得跳过用户确认自动发布新版本。

### 4.4 StoragePolicy 调整

`NativeStage01StoragePolicy.Evaluate` 继续负责原始证据分类：

1. 原始 Storage 字段完整性；
2. 原始 Payload SHA-256；
3. 纯解码；
4. FileGuid 与三处版本一致性；
5. 版本比较；
6. 当前版本 canonical 对账。

只有 `0.9.1` 执行当前 canonical 对账。合法 `0.9.0` 返回 `MigratableLegacy`，不拿迁移后的 JSON 与原始 JSON 比较。

---

## 5. Stage01 读取与漂移模型

### 5.1 新增现场证据模型

```csharp
internal sealed class NativeStage01LiveEvidence
{
  internal string ProjectName { get; set; }
  internal string ProjectNumber { get; set; }
  internal string BaseX { get; set; }
  internal string BaseY { get; set; }
  internal string BaseElevation { get; set; }
  internal string TrueNorthAngle { get; set; }
  internal string LengthUnit { get; set; }
  internal string AreaUnit { get; set; }
  internal string AngleUnit { get; set; }
}
```

`NativeStage01ReadResult` 增加：

```csharp
internal NativeStage01LiveEvidence LiveEvidence { get; set; }
internal IReadOnlyList<NativeStage01Drift> Drifts { get; set; }
internal bool RequiresMigrationConfirmation { get; set; }
```

### 5.2 读取顺序

```text
1. 读取 Storage 原始记录
2. StoragePolicy 分类
3. NoRecord 创建新表单；Current 克隆 Payload；Legacy 调用迁移器生成内存候选
4. 独立读取当前 RVT 现场证据
5. 按逐字段权威来源矩阵决定“初值”或“漂移对账”
6. 校验项目条件声明和业务字段
7. 返回模型、现场证据、漂移、迁移状态和消息
```

当前 `SetIfBlank` 的一刀切方式需要拆除。对 NoRecord 可以把 Revit 原生字段作为初值；对 Current 和 MigratableLegacy 只能比较，不能静默覆盖。

---

## 6. 人工工作台修订

### 6.1 顶部运行时身份

新增只读区域：

```text
插件版本｜构建号｜Commit
DLL 路径
规则包身份
当前文档状态
```

DLL 路径保持单行、可横向滚动，并提供 Tooltip。

### 6.2 项目条件

- 左侧第一项仍为“项目条件（必填）”；
- 新表单进入时保持未声明；
- 页面明确显示“必须主动选择”；
- 不提供默认选中的 `none`；
- 旧版迁移后未声明时显示迁移提示，但仍阻断写入；
- 条件互斥继续实时生效。

### 6.3 Revit 原生字段漂移

对项目名称、项目编号、X、Y、高程和真北显示：

```text
上次确认值
当前 RVT 值
状态：一致 / 已变化
```

不得在用户不知情时自动采用其中任一侧。用户提交 Stage01 即表示确认当前表单值作为下一次写入目标。

---

## 7. MCP 兼容性

13 个工具名称保持不变：

```text
bimbaogui_list_revit_sessions
bimbaogui_get_document_status
bimbaogui_get_rule_package_identity
bimbaogui_stage01_get_form_schema
bimbaogui_stage01_read
bimbaogui_stage01_validate
bimbaogui_stage01_write
bimbaogui_stage02_preview
bimbaogui_stage02_write
bimbaogui_stage03_scan
bimbaogui_stage03_export
bimbaogui_stage03_get_last_result
bimbaogui_stage03_revalidate_file
```

允许向响应中追加兼容字段：

```text
payload_schema_version
source_payload_version
requires_migration_confirmation
live_evidence
drifts
```

现有请求字段、确认租约、一次性哈希和工具名称不得改变。

`stage01_validate` 对未声明项目条件继续返回无效；Agent 不得通过省略 `none` 获得自动确认。

---

## 8. CI 稳定性修复

最新 GHA 红灯的实际原因是 Windows Runner 上以下资源探测进程超时：

```text
powershell.exe
ReflectionOnlyLoadFrom(...)
GetManifestResourceNames()
```

规则包测试结果为 `163 passed / 1 timeout`，不是 HBR 规则漂移。

修复要求：

- 优先使用当前 Runner 已安装的 `pwsh`；
- 使用 `Assembly.LoadFile` 只读取 manifest resource names；
- 显式设置错误即停止；
- 超时提高到能够覆盖 Windows Runner 冷启动，但不得无限等待；
- 失败信息必须包含 stdout、stderr、命令和 Assembly 路径；
- 保留“恰好一个 `.hbrpack` 资源”的原始断言。

该修复只提升测试探测稳定性，不修改 GHA 生产程序集、规则包或业务逻辑。

---

## 9. 实施任务

### Task 1：锁定修正版计划合同

**新增：**

```text
tests/test_revit_addin_v041_plan_contract.py
```

合同必须阻止以下回退：

- 默认把 `none` 设为 `true`；
- 在 `TryDecode` 中修改模型；
- 不升级 Payload 版本却增加 canonical 字段；
- 使用单一全局数据优先级；
- 把 CI 结论写成 Revit 或 IFCFlux 实机通过。

### Task 2：修复资源探测超时

**修改：**

```text
tests/test_hbr_rulepack_compiler.py
```

执行红绿验证：

```powershell
python -m pytest `
  tests/test_hbr_rulepack_compiler.py::test_stage01_real_build_is_incremental_and_embeds_only_the_generated_pack `
  -q
```

### Task 3：统一产品版本与运行时身份

**修改：**

```text
src/BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.csproj
src/BIMBaoGui.HifcCore/BIMBaoGui.HifcCore.csproj
src/BIMBaoGui.McpContracts/BIMBaoGui.McpContracts.csproj
src/BIMBaoGui.McpServer/BIMBaoGui.McpServer.csproj
src/BIMBaoGui.RevitAddin/WorkspaceControl.cs
.github/workflows/build-revit-mcp.yml
installer/Install-Revit2020.ps1
```

四个程序集、安装目录和 Artifact 统一为 `0.4.1`。CI 注入 Build Number 与 Commit SHA，并在构建后反射校验。

### Task 4：建立 0.9.1 迁移通道

**新增：**

```text
src/BIMBaoGui.RevitAddin/Stage01/NativeStage01MigrationService.cs
tests/BIMBaoGui.RevitAddin.Tests/NativeStage01MigrationServiceTests.cs
```

**修改：**

```text
NativeStage01Canonicalizer.cs
NativeStage01StoragePolicy.cs
NativeStage01RevitReadService.cs
NativeStage01RevitService.cs
```

必须覆盖：

- 合法 0.9.0 被分类为 `MigratableLegacy`；
- 迁移候选为 0.9.1；
- `none` 缺失后为 `false`；
- 非空业务值不变；
- 未确认写入时原 Storage 不变；
- 迁移后回读达到 `Current`。

### Task 5：建立逐字段权威与漂移对账

**新增：**

```text
src/BIMBaoGui.RevitAddin/Stage01/NativeStage01FieldAuthorityPolicy.cs
src/BIMBaoGui.RevitAddin/Stage01/NativeStage01LiveEvidence.cs
```

**修改：**

```text
NativeStage01RevitReadService.cs
NativeStage01ViewModel.cs
NativeStage01View.cs
McpStage01Adapter.cs
```

必须覆盖：

- NoRecord 使用当前 Revit 原生值作为表单初值；
- Current 不静默覆盖 Payload；
- Revit 现场值变化生成 drift；
- 业务字段只来自 Payload/用户输入；
- X 仍表示南北坐标，Y 仍表示东西坐标。

### Task 6：保持项目条件显式声明

**修改：**

```text
NativeProjectConditionDeclarationPolicy.cs
NativeStage01ConditionSchemaPolicy.cs
NativeStage01ViewModel.cs
NativeStage01View.cs
McpStage01Adapter.cs
```

必须覆盖：

- 新表单为 Missing；
- 旧版缺少 none 仍为 Missing；
- 实际条件与 none 双向互斥；
- 冲突数据不被静默修正为“无条件”；
- 人工与 MCP 校验结果一致。

### Task 7：完整回归与安装包

执行：

```powershell
python -m pytest tests -q

dotnet test tests/BIMBaoGui.HifcCore.Tests/BIMBaoGui.HifcCore.Tests.csproj -c Release
dotnet test tests/BIMBaoGui.McpContracts.Tests/BIMBaoGui.McpContracts.Tests.csproj -c Release
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj -c Release

dotnet build src/BIMBaoGui.HifcCore/BIMBaoGui.HifcCore.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build src/BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build src/BIMBaoGui.McpServer/BIMBaoGui.McpServer.csproj -c Release -p:TreatWarningsAsErrors=true

git diff --check
```

最终 Artifact：

```text
BIMBaoGui-Revit2020-Native-MCP-v0.4.1.zip
```

---

## 10. Revit 2020 实机验收

安装最终 Artifact 后逐项留证：

1. 工作台显示 `0.4.1`、非 `local` Build、非 `unknown` Commit；
2. DLL 路径与实际安装文件一致；
3. 无记录 RVT 能读出当前项目名称、编号、X、Y、高程和真北；
4. 项目条件初始为未声明，不能直接通过 Stage01；
5. 用户主动选择实际条件或 `none` 后通过声明门禁；
6. 合法 0.9.0 文件显示“等待迁移确认”，但原 Storage 未被读取动作改写；
7. 用户确认后写入 0.9.1，并完成事务后回读；
8. 修改 Revit 项目位置后，工作台显示 drift，而不是静默覆盖 Payload；
9. Stage02 全模型与当前选择预览保持兼容；
10. Stage03 严格/强制门禁、RAW IFC、H-IFC exact 回读保持兼容；
11. MCP 13 个工具均可发现且原请求结构继续可用；
12. 最终 H-IFC 在 IFCFlux 中由用户人工检查并保存证据。

---

## 11. 发布门禁

只有同时满足以下条件，才能发布 `0.4.1`：

- 修正版计划合同通过；
- Python 全量测试通过；
- .NET 全量测试通过；
- Release 编译 0 warning / 0 error；
- 四个产品程序集版本一致；
- 0.9.0 → 0.9.1 迁移测试通过；
- 项目条件未被默认代选；
- 逐字段权威来源和 drift 测试通过；
- 安装、覆盖安装和卸载 smoke 通过；
- Revit 2020 实机加载身份与安装证据一致；
- IFCFlux 状态在人工验收前始终保持 `IFCFLUX_MANUAL_PENDING`。

任何一项缺失，都只能描述为“代码或安装结构已验证”，不得描述为完整报规闭环通过。
