# Revit Stage01 项目条件必填声明 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让人工界面和 MCP 都必须明确选择实际项目条件或“无上述项目条件”，并将项目条件固定为 Stage01 第一项。

**Architecture:** 使用一个独立的 `NativeProjectConditionDeclarationPolicy` 管理哨兵条件、互斥操作和四态判定。Validator、ViewModel、WPF 和 MCP Schema 只消费该策略，条件结果继续存入现有 Payload `conditions` 字典，不修改 HBR 数据库和 Extensible Storage。

**Tech Stack:** C# / .NET Framework 4.8、WPF、Revit 2020 API、.NET 8 MCP Server、xUnit、pytest、GitHub Actions。

## Global Constraints

- 只在 `feat/revit-native-addin-mcp-v0.3` 开发，不创建额外分支。
- 保留现有 9 个 MCP 工具名称。
- 不修改 HBR 权威参考数据库。
- 不提升 Stage01 Payload 协议版本 `0.9.0`。
- 不把 Stage01/02 成功表述为 H-IFC 识别成功。
- 最终交付一个可直接覆盖安装的统一安装包。

---

### Task 1: 条件声明领域策略

**Files:**
- Create: `src/BIMBaoGui.RevitAddin/Stage01/NativeProjectConditionDeclarationPolicy.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/NativeProjectConditionDeclarationPolicyTests.cs`

**Interfaces:**
- Produces: `NativeProjectConditionDeclarationPolicy.Evaluate`、`SetActualCondition`、`SetNoConditions`、`NoneConditionId`、`NoneDisplayName`。

- [ ] 写入默认未声明、实际条件选择、无条件选择和冲突四类失败测试。
- [ ] 运行原生领域测试，确认因策略类型不存在而失败。
- [ ] 实现最小四态策略和互斥操作。
- [ ] 运行领域测试，确认策略测试通过。

### Task 2: Validator 与 ViewModel 门禁

**Files:**
- Modify: `src/BIMBaoGui.RevitAddin/Stage01/NativeStage01Validator.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage01/NativeStage01ViewModel.cs`
- Modify: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage01ValidatorTests.cs`
- Modify: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage01ViewModelTests.cs`

**Interfaces:**
- Consumes: Task 1 的条件声明策略。
- Produces: 两个新错误码、项目条件第一目录、条件目录必填计数和 ViewModel 互斥编辑方法。

- [ ] 写入缺少声明、冲突、第一目录和互斥状态测试。
- [ ] 运行领域测试，确认测试按预期失败。
- [ ] 接入 Validator 和 ViewModel。
- [ ] 运行全部原生领域测试并修复回归。

### Task 3: WPF 与 MCP Schema

**Files:**
- Modify: `src/BIMBaoGui.RevitAddin/Stage01/NativeStage01View.cs`
- Modify: `src/BIMBaoGui.RevitAddin/McpBridge/McpStage01Adapter.cs`
- Modify: `tests/test_revit_addin_stage01_ui_contract.py`
- Modify: `tests/test_revit_addin_mcp_contract.py`

**Interfaces:**
- Consumes: Task 2 的 ViewModel 与 Validator。
- Produces: 第一页必填条件界面、无条件复选项、MCP 条件声明元数据。

- [ ] 写入 UI 与 MCP 源码合同测试。
- [ ] 运行 pytest，确认缺少新界面和 Schema 字段而失败。
- [ ] 实现 WPF 项目条件页面和 MCP Schema 投影。
- [ ] 运行相关 pytest 与领域测试。

### Task 4: 版本、安装器和说明

**Files:**
- Modify: `src/BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.csproj`
- Modify: `src/BIMBaoGui.McpServer/BIMBaoGui.McpServer.csproj`
- Modify: `installer/Install-Revit2020.ps1`
- Modify: `installer/McpProbe.cmd`
- Modify: `.github/workflows/build-revit-mcp.yml`
- Modify: `docs/revit-addin/README.md`

**Interfaces:**
- Produces: 统一产品版本 `0.3.2` 和可覆盖安装的构建产物。

- [ ] 将 Revit Add-in、MCP Server、安装路径及 smoke test 统一升级到 `0.3.2`。
- [ ] 在 README 明确项目条件门禁与 H-IFC 尚未闭环。
- [ ] 运行安装器合同与完整构建。

### Task 5: 基线、验证和交付

**Files:**
- Create: `specs/revit-addin/v0.3.2-functional-baseline.json`
- Modify: `tests/test_revit_addin_mcp_non_regression.py`
- Delete: `specs/revit-addin/v0.3.1-functional-baseline.json`

**Interfaces:**
- Consumes: 完成后的 Stage01/Stage02/Workspace 实现提交。
- Produces: v0.3.2 非回归基线和统一 ZIP。

- [ ] 用完成实现的 commit SHA 固化 v0.3.2 基线。
- [ ] 运行统一 GitHub Actions，要求合同、领域测试、Release 编译、MCP publish、安装/卸载 smoke 全部通过。
- [ ] 下载 artifact，核对 ZIP 与内部 `SHA256SUMS.txt`，交付单一安装包。
