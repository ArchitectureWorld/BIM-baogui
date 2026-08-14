# Revit Native Stage03 H-IFC Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在现有唯一 Revit 2020 原生插件 + MCP 产品线上增加可实际测试的 Stage03：现场预检、严格/强制导出、Autodesk IFC4 RAW、H-IFC 转译、精确回读、报告与 IFCFlux 人工验收文件。

**Architecture:** 继续沿用原生 WPF + ExternalEvent + Named Pipe MCP 架构。新增独立 `Stage03` 命名空间；现场扫描复用原生 Stage01/Stage02 的规则、角色匹配和值编解码；IFC 后处理核心以不依赖 GH/Rhino 的源代码项目 `BIMBaoGui.HifcCore` 承载，原生插件只负责 Revit 现场证据、导出调度、UI 与 MCP 适配。

**Tech Stack:** .NET Framework 4.8、Autodesk Revit 2020 API、WPF、IFC STEP、SHA-256、System.Web.Script.Serialization、Named Pipe、Model Context Protocol C# SDK、GitHub Actions Windows runner。

## Global Constraints

- 唯一开发分支固定为 `feat/revit-native-addin-mcp-v0.3`；不创建额外产品分支。
- 产品版本升级为 `0.4.0`；覆盖安装时清理旧 `0.3.x` MCP 目录。
- Stage01、Stage02 人工入口与 MCP 行为必须保持非回归。
- 不引用 Grasshopper、RhinoCommon、Rhino.Inside.Revit 或 `.gha`。
- 只消费权威 HBR 规则数据库、参数 GUID、IFC Entity/Pset/Property/type/owner strategy。
- 严格模式默认；强制测试模式必须提供非空原因且输出名包含 `FORCED_TEST`。
- IFCFlux 没有 API；外部状态只能为 `IFCFLUX_MANUAL_PENDING`，不能伪造通过。
- RAW IFC 不得原地修改；转译前后 SHA-256 必须一致。
- 所有 Revit API 操作只能经 `ExternalEvent` 在 Revit API 上下文内执行。
- Release 编译必须 0 warning / 0 error。

---

### Task 1: 建立独立 H-IFC 核心项目

**Files:**
- Create: `src/BIMBaoGui.HifcCore/BIMBaoGui.HifcCore.csproj`
- Create: `src/BIMBaoGui.HifcCore/Ifc/*`
- Create: `src/BIMBaoGui.HifcCore/Stage03/*`
- Modify: `src/BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.csproj`
- Test: `tests/BIMBaoGui.HifcCore.Tests/*`

**Interfaces:**
- Produces: `NativeIfcDocument.Parse(string)`, `NativeIfcEnricher.Enrich(...)`, `NativeIfcExactValidator.Validate(...)`, `NativeStage03OutputPathPolicy`。

- [ ] 写失败测试：STEP parse/serialize round-trip、IFC GUID、输出命名、严格/强制门禁。
- [ ] 运行测试并确认失败。
- [ ] 从现有稳定 Stage03/Mvd 算法提取无 GH/Rhino 依赖的独立实现。
- [ ] 运行测试并确认通过。
- [ ] 提交。

### Task 2: 原生 Stage03 现场扫描与预检

**Files:**
- Create: `src/BIMBaoGui.RevitAddin/Stage03/NativeStage03Models.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage03/NativeStage03Scanner.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage03/NativeStage03GatePolicy.cs`
- Test: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage03ScannerPolicyTests.cs`

**Interfaces:**
- Produces: `NativeStage03ScanRequest`, `NativeStage03ScanResult`, `ScanHash`, blocker/warning/field evidence。

- [ ] 写失败测试：Stage01/条件声明、ProjectInformation 多角色、INSTANCE/TYPE、强制可跳过与不可强制错误、确定性 scan hash。
- [ ] 运行测试并确认失败。
- [ ] 实现现场扫描、字段状态、严格/强制门禁和 canonical scan hash。
- [ ] 运行测试并确认通过。
- [ ] 提交。

### Task 3: Revit IFC4 RAW 导出与安全工作目录

**Files:**
- Create: `src/BIMBaoGui.RevitAddin/Stage03/NativeStage03RawIfcExporter.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage03/NativeStage03RunDirectory.cs`
- Test: `tests/test_revit_addin_stage03_revit_contract.py`

**Interfaces:**
- Produces: `NativeStage03RawIfcArtifact Export(Document, NativeStage03RunPaths)`。

- [ ] 写失败合同测试：`Document.Export`、`IFCExportOptions`、IFC4、独立 run 目录、RAW SHA-256、目标冲突保护。
- [ ] 运行测试并确认失败。
- [ ] 实现导出和文件证据。
- [ ] 运行测试并确认通过。
- [ ] 提交。

### Task 4: H-IFC 转译、exact 回读与报告

**Files:**
- Create: `src/BIMBaoGui.RevitAddin/Stage03/NativeStage03TranslationService.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage03/NativeStage03ReportWriter.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage03/NativeStage03WorkflowService.cs`
- Test: `tests/BIMBaoGui.HifcCore.Tests/NativeStage03TranslationTests.cs`

**Interfaces:**
- Produces: `NativeStage03ExecutionResult`，包含 RAW/HIFC/fields/validation/checklist/failure/quarantine。

- [ ] 写失败测试：owner、Pset/property/type/value/unit、重复关系、candidate 隔离、RAW hash 不变、final 原子发布。
- [ ] 运行测试并确认失败。
- [ ] 实现转译、复读、exact validator、字段报告、validation 报告和 IFCFlux checklist。
- [ ] 运行测试并确认通过。
- [ ] 提交。

### Task 5: 原生 WPF Stage03 页面与 ExternalEvent

**Files:**
- Create: `src/BIMBaoGui.RevitAddin/Stage03/NativeStage03View.cs`
- Modify: `src/BIMBaoGui.RevitAddin/WorkspaceControl.cs`
- Modify: `src/BIMBaoGui.RevitAddin/RevitExternalEventDispatcher.cs`
- Test: `tests/test_revit_addin_stage03_ui_contract.py`

**Interfaces:**
- Produces: `RequestStage03Scan`, `RequestStage03Export`, `RequestStage03Revalidate`。

- [ ] 写失败 UI/调度合同测试。
- [ ] 运行测试并确认失败。
- [ ] 实现真实页面：输出目录、严格/强制、原因、预检、导出、复检、打开目录、固定高度报告、问题和输出文件。
- [ ] 运行测试并确认通过。
- [ ] 提交。

### Task 6: MCP Stage03 工具与一次性租约

**Files:**
- Modify: `src/BIMBaoGui.McpContracts/ToolContracts.cs`
- Create: `src/BIMBaoGui.RevitAddin/McpBridge/McpStage03Adapter.cs`
- Modify: `src/BIMBaoGui.RevitAddin/McpBridge/McpBridgeCommandRouter.cs`
- Modify: `src/BIMBaoGui.McpServer/BimBaoGuiTools.cs`
- Test: `tests/test_revit_addin_mcp_stage03_contract.py`

**Interfaces:**
- Produces tools: `bimbaogui_stage03_scan`, `bimbaogui_stage03_export`, `bimbaogui_stage03_get_last_result`, `bimbaogui_stage03_revalidate_file`。

- [ ] 写失败工具发现、参数、租约、强制原因和过期 scan hash 测试。
- [ ] 运行测试并确认失败。
- [ ] 实现 MCP 适配；人工与 MCP 共同调用同一 Stage03 服务。
- [ ] 运行测试并确认通过。
- [ ] 提交。

### Task 7: v0.4.0 安装、文档、CI 与产物

**Files:**
- Modify: `installer/Install-Revit2020.ps1`
- Modify: `.github/workflows/build-revit-mcp.yml`
- Modify: `docs/revit-addin/README.md`
- Create: `specs/revit-addin/v0.4.0-functional-baseline.json`
- Test: installer/packaging contracts。

**Interfaces:**
- Produces: `BIMBaoGui-Revit2020-Native-MCP-v0.4.0.zip`。

- [ ] 更新版本、清理旧版本目录和安装证据。
- [ ] 将 HifcCore DLL、Stage03 文件及报告 schema 纳入安装包和 SHA256SUMS。
- [ ] CI 运行 Stage01/02 非回归、HifcCore、Stage03、MCP SDK、Release build、安装/卸载 smoke。
- [ ] 下载 artifact，复核 ZIP 逐文件哈希。
- [ ] 提交最终文档与基线。
