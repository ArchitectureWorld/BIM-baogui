# BIMBaoGui Revit 原生插件 v0.4.1 优化实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不改变 Stage02、Stage03、H-IFC 转译和既有 MCP 工具行为的前提下，将统一 Revit 原生产品升级至 `0.4.1`，在工作台顶部显示当前 Revit 进程实际加载 DLL 的版本、构建号、提交号和路径，并为 Stage01 安全必选项提供确定性默认值。

**Architecture:** 新增独立的运行时程序集身份读取器，所有版本展示均从当前已加载的 `BIMBaoGui.RevitAddin.dll` 读取；GitHub Actions 只负责将构建号和 Commit SHA 注入程序集元数据。Stage01 新增原生默认策略层，仅补齐可安全确定的初始化基线，不修改共享 HBR 映射数据库，不触碰 GHA 产品线，也不覆盖当前 RVT 或既有 Stage01 Payload 中的真实非空数据。

**Tech Stack:** Revit 2020 API、.NET Framework 4.8、WPF、xUnit、Python/pytest 合同测试、GitHub Actions、PowerShell 用户级安装器、MCP .NET SDK。

## Global Constraints

- 唯一开发分支：`feat/revit-native-addin-mcp-v0.3`；本计划直接提交并实施在该分支，不再创建新的 Revit 功能分支。
- 基线提交：`e57473d813f58e9e528b2afc69d31d4e32b602cf`。
- 对外统一产品版本：`0.4.1`；Revit Add-in、MCP Server、MCP Contracts、H-IFC Core、安装器目录和安装包名称必须一致。
- 只允许修改 Revit 原生产品线：`src/BIMBaoGui.RevitAddin/**`、`src/BIMBaoGui.McpServer/**`、`src/BIMBaoGui.McpContracts/**`、`src/BIMBaoGui.HifcCore/**`、对应测试、安装器、Revit 工作流和 Revit 文档。
- 明确禁止修改：`src/BIMBaoGui.Stage01/**`、`.github/workflows/build-stage01-gha.yml` 以及任何 GHA UI、组件 GUID、Grasshopper 状态机。
- 本次不修改 `specs/hbr-rules/v1/source/hbr_rule_source.v1.json` 的 H-IFC Entity、Pset、Property、参数 GUID、类型、单位或 Owner 映射；Stage01 便利默认值属于 Revit 原生工作流策略，不建立第二份映射数据库。
- 不增加“一键自测”按钮、菜单、MCP 工具或隐藏后台流程。
- 不自动填入项目名称、项目编号、项目地址、建设单位、设计单位、子项名称、企业名称、信用代码、联系人等真实业务事实。
- X 继续表示南北坐标，Y 继续表示东西坐标；默认 `0` 仅是新表单初始化基线，当前 RVT 的实际项目位置和既有 Payload 值优先。
- `无上述项目条件（已确认）` 默认选中；它与所有实际项目条件双向互斥。
- 现有 Stage01 canonical JSON、Payload Schema、Extensible Storage GUID、固定共享参数 GUID、Stage02 确定性预览、Stage03 严格/强制门禁、13 个 MCP 工具名称和请求结构均保持兼容。
- 没有真实 Revit 2020 进程证据时，不得把 CI 结论描述为“Revit 实机全流程已通过”。

---

## 一、v0.4.1 优化目标

### 1. 运行时身份必须可信

工作台顶部直接显示：

```text
插件：BIMBaoGui Revit Add-in v0.4.1
构建：GitHub Actions #<run_number> · Commit <short_sha>
DLL：C:\Users\...\AppData\Roaming\Autodesk\Revit\Addins\2020\BIMBaoGui.RevitAddin\BIMBaoGui.RevitAddin.dll
```

身份来源固定为当前 Revit 进程已经加载的程序集：

- 版本：`AssemblyInformationalVersionAttribute` / `AssemblyFileVersionAttribute` / `AssemblyName.Version`；
- 构建号：`AssemblyMetadataAttribute("HBR.BuildNumber", ...)`；
- Commit：`AssemblyMetadataAttribute("HBR.CommitSha", ...)`；
- 路径：`typeof(WorkspaceControl).Assembly.Location`。

不得从 README、安装脚本、文件名、预期安装目录或 UI 常量推断。

### 2. Stage01 安全必选项默认完成

新建表单、重置表单以及旧 Payload 缺失字段迁移时，安全默认值为：

| 字段 | 默认值 | 说明 |
|---|---|---|
| 模型文件类型 | `总平模型` | 保留当前规则目录默认 |
| 模型范围 | `项目总平面报规模型` | 保留当前规则目录默认 |
| 坐标系名称 | `CGCS2000` | 保留当前规则目录默认 |
| 高程系名称 | `1985国家高程基准` | 保留当前规则目录默认 |
| X / 南北坐标 | `0` | 新增原生安全基线 |
| Y / 东西坐标 | `0` | 新增原生安全基线 |
| 基点高程 | `0` | 新增原生安全基线 |
| 真北角度 | `0` | 保留当前默认 |
| 长度单位 | `m` | 保留当前系统默认 |
| 面积单位 | `m²` | 保留当前系统默认 |
| 角度单位 | `°` | 保留当前系统默认 |
| 项目条件声明 | `无上述项目条件（已确认） = true` | 新增默认声明 |

优先级固定为：

```text
当前 RVT 实际值
> 当前 RVT 已有 Stage01 Payload 非空值
> Revit 原生安全默认值
> 空值并显示必填阻断
```

### 3. 项目条件不再要求重复点击

- 新模型默认：全部实际条件 `false`，`workflow.project_conditions.none = true`；
- 勾选任一实际条件：自动将 `workflow.project_conditions.none` 设为 `false`；
- 勾选“无上述项目条件”：自动清空全部实际条件；
- 读取旧 Payload 且没有任何实际条件、也没有否定声明：自动补为 `none = true`；
- 读取冲突 Payload：实际条件优先，自动取消 `none`，并返回一条非阻断迁移消息；
- canonical Payload 必须保存最终互斥后的条件状态。

### 4. 功能不回退

本次不改变：

- Stage01 写入事务、事务内回读、事务后回读和整体回滚；
- Stage02 全模型/当前选择扫描、精确角色匹配、参数级事务和构件级原子事务；
- Stage03 RAW IFC 导出、H-IFC 转译、字段回读和 IFCFlux 人工待检状态；
- MCP Named Pipe、ExternalEvent、确认租约和工具协议。

---

## 二、文件结构与职责

### 新建文件

```text
src/BIMBaoGui.RevitAddin/Runtime/PluginRuntimeIdentity.cs
    读取当前实际加载程序集的版本、构建号、Commit 和路径；不依赖 Revit API。

src/BIMBaoGui.RevitAddin/Stage01/NativeStage01DefaultPolicy.cs
    负责新模型安全默认、旧模型缺失值补齐和项目条件确定性归一。

tests/BIMBaoGui.RevitAddin.Tests/PluginRuntimeIdentityTests.cs
    覆盖版本/元数据/路径格式与本地构建回退。

tests/BIMBaoGui.RevitAddin.Tests/NativeStage01DefaultPolicyTests.cs
    覆盖默认值、非覆盖、项目条件互斥和旧 Payload 迁移。

tests/test_revit_addin_v041_contract.py
    锁定 Revit 产品版本、顶部身份 UI、CI 元数据注入、安装器和产物名称。
```

### 修改文件

```text
src/BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.csproj
    升级 0.4.1；声明 HbrBuildNumber/HbrCommitSha；生成 AssemblyMetadata。

src/BIMBaoGui.McpServer/BIMBaoGui.McpServer.csproj
src/BIMBaoGui.McpContracts/BIMBaoGui.McpContracts.csproj
src/BIMBaoGui.HifcCore/BIMBaoGui.HifcCore.csproj
    统一产品二进制版本为 0.4.1。

src/BIMBaoGui.RevitAddin/WorkspaceControl.cs
    在顶部显示实际加载插件身份和 DLL 路径。

src/BIMBaoGui.RevitAddin/Rules/NativeRuleCatalog.cs
    创建默认模型后调用 NativeStage01DefaultPolicy.ApplyForNewModel。

src/BIMBaoGui.RevitAddin/Stage01/NativeProjectConditionDeclarationPolicy.cs
    增加 NormalizeLoadedDeclaration，保持实际条件与 none 双向互斥。

src/BIMBaoGui.RevitAddin/Stage01/NativeStage01ConditionSchemaPolicy.cs
    继续补齐数据库条件键，并将补齐结果交给默认策略统一归一。

src/BIMBaoGui.RevitAddin/Stage01/NativeStage01PayloadCodec.cs
    Payload 解码后执行缺失默认和项目条件迁移，不改变 Payload Schema。

src/BIMBaoGui.RevitAddin/Stage01/NativeStage01RevitReadService.cs
    保证文档值覆盖默认基线；返回迁移消息；不以默认 0 覆盖 Revit 实际项目位置。

src/BIMBaoGui.RevitAddin/Stage01/NativeStage01ViewModel.cs
    LoadModel 时执行防御性归一；默认进入项目条件目录的现有行为保持不变。

installer/Install-Revit2020.ps1
    MCP 版本目录升级为 0.4.1，清理旧 0.4.0 目录。

.github/workflows/build-revit-mcp.yml
    注入 Build Number/Commit SHA；执行 v0.4.1 合同；更新安装路径和产物名。

docs/revit-addin/README.md
    更新版本、顶部身份说明和 Stage01 默认策略。

相关 xUnit / pytest 合同测试
    更新预期版本和新增行为。
```

---

### Task 1: 锁定 v0.4.1 产品版本与分支范围

**Files:**
- Create: `tests/test_revit_addin_v041_contract.py`
- Modify: `src/BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.csproj`
- Modify: `src/BIMBaoGui.McpServer/BIMBaoGui.McpServer.csproj`
- Modify: `src/BIMBaoGui.McpContracts/BIMBaoGui.McpContracts.csproj`
- Modify: `src/BIMBaoGui.HifcCore/BIMBaoGui.HifcCore.csproj`

**Interfaces:**
- Consumes: 四个产品项目文件及唯一 Revit 工作流。
- Produces: 统一语义版本 `0.4.1`，供安装器、UI、CI 和产物命名使用。

- [ ] **Step 1: 写失败的版本合同测试**

```python
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
PROJECTS = [
    ROOT / "src/BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.csproj",
    ROOT / "src/BIMBaoGui.McpServer/BIMBaoGui.McpServer.csproj",
    ROOT / "src/BIMBaoGui.McpContracts/BIMBaoGui.McpContracts.csproj",
    ROOT / "src/BIMBaoGui.HifcCore/BIMBaoGui.HifcCore.csproj",
]


def property_value(path: Path, name: str) -> str:
    root = ET.parse(path).getroot()
    node = root.find(f".//{name}")
    assert node is not None
    return node.text


def test_unified_product_version_is_041():
    for project in PROJECTS:
        assert property_value(project, "Version") == "0.4.1"
        assert property_value(project, "FileVersion") == "0.4.1.0"
        assert property_value(project, "AssemblyVersion") == "0.4.1.0"


def test_plan_does_not_touch_gha_product_paths():
    workflow = (ROOT / ".github/workflows/build-revit-mcp.yml").read_text(
        encoding="utf-8"
    )
    assert "src/BIMBaoGui.Stage01/**" not in workflow
```

- [ ] **Step 2: 运行测试确认当前版本失败**

Run:

```powershell
python -m pytest tests/test_revit_addin_v041_contract.py -q
```

Expected: `0.4.0 != 0.4.1`。

- [ ] **Step 3: 将四个项目版本统一修改为 0.4.1**

```xml
<Version>0.4.1</Version>
<FileVersion>0.4.1.0</FileVersion>
<AssemblyVersion>0.4.1.0</AssemblyVersion>
```

- [ ] **Step 4: 运行版本合同测试**

Run:

```powershell
python -m pytest tests/test_revit_addin_v041_contract.py -q
```

Expected: PASS。

- [ ] **Step 5: 提交**

```bash
git add tests/test_revit_addin_v041_contract.py \
  src/BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.csproj \
  src/BIMBaoGui.McpServer/BIMBaoGui.McpServer.csproj \
  src/BIMBaoGui.McpContracts/BIMBaoGui.McpContracts.csproj \
  src/BIMBaoGui.HifcCore/BIMBaoGui.HifcCore.csproj
git commit -m "chore: align Revit native product version to 0.4.1"
```

---

### Task 2: 实现当前加载程序集的真实运行身份

**Files:**
- Create: `src/BIMBaoGui.RevitAddin/Runtime/PluginRuntimeIdentity.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/PluginRuntimeIdentityTests.cs`
- Modify: `src/BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.csproj`

**Interfaces:**
- Consumes: `System.Reflection.Assembly`。
- Produces:

```csharp
internal sealed class PluginRuntimeIdentity
{
  internal string ProductVersion { get; }
  internal string BuildNumber { get; }
  internal string CommitSha { get; }
  internal string ShortCommitSha { get; }
  internal string AssemblyPath { get; }

  internal static PluginRuntimeIdentity Read(Assembly assembly);
}
```

- [ ] **Step 1: 写失败的运行身份测试**

```csharp
using System;
using BIMBaoGui.RevitAddin.Runtime;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class PluginRuntimeIdentityTests
  {
    [Fact]
    public void CreateNormalizesBuildCommitAndPath()
    {
      PluginRuntimeIdentity identity = PluginRuntimeIdentity.Create(
        "0.4.1+build.250",
        "250",
        "0123456789abcdef",
        @"C:\Temp\BIMBaoGui.RevitAddin.dll");

      Assert.Equal("0.4.1", identity.ProductVersion);
      Assert.Equal("250", identity.BuildNumber);
      Assert.Equal("01234567", identity.ShortCommitSha);
      Assert.EndsWith(
        "BIMBaoGui.RevitAddin.dll",
        identity.AssemblyPath,
        StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LocalBuildUsesExplicitFallbacks()
    {
      PluginRuntimeIdentity identity = PluginRuntimeIdentity.Create(
        "0.4.1",
        "",
        "",
        "");

      Assert.Equal("local", identity.BuildNumber);
      Assert.Equal("unknown", identity.CommitSha);
      Assert.Equal("运行时未提供程序集路径", identity.AssemblyPath);
    }
  }
}
```

- [ ] **Step 2: 运行测试确认类型不存在**

Run:

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release --filter PluginRuntimeIdentityTests
```

Expected: FAIL，`PluginRuntimeIdentity` 不存在。

- [ ] **Step 3: 实现身份读取器**

```csharp
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace BIMBaoGui.RevitAddin.Runtime
{
  internal sealed class PluginRuntimeIdentity
  {
    private PluginRuntimeIdentity(
      string productVersion,
      string buildNumber,
      string commitSha,
      string assemblyPath)
    {
      ProductVersion = NormalizeVersion(productVersion);
      BuildNumber = string.IsNullOrWhiteSpace(buildNumber)
        ? "local"
        : buildNumber.Trim();
      CommitSha = string.IsNullOrWhiteSpace(commitSha)
        ? "unknown"
        : commitSha.Trim();
      ShortCommitSha = CommitSha == "unknown"
        ? CommitSha
        : CommitSha.Substring(0, Math.Min(8, CommitSha.Length));
      AssemblyPath = string.IsNullOrWhiteSpace(assemblyPath)
        ? "运行时未提供程序集路径"
        : Path.GetFullPath(assemblyPath);
    }

    internal string ProductVersion { get; }
    internal string BuildNumber { get; }
    internal string CommitSha { get; }
    internal string ShortCommitSha { get; }
    internal string AssemblyPath { get; }

    internal static PluginRuntimeIdentity Read(Assembly assembly)
    {
      if (assembly == null) throw new ArgumentNullException(nameof(assembly));
      var metadata = assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .GroupBy(value => value.Key, StringComparer.Ordinal)
        .ToDictionary(
          group => group.Key,
          group => group.Last().Value ?? string.Empty,
          StringComparer.Ordinal);
      string informational = assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion;
      string fileVersion = FileVersionInfo
        .GetVersionInfo(assembly.Location)
        .FileVersion;
      string version = informational
        ?? fileVersion
        ?? assembly.GetName().Version?.ToString()
        ?? "unknown";
      metadata.TryGetValue("HBR.BuildNumber", out string build);
      metadata.TryGetValue("HBR.CommitSha", out string commit);
      return new PluginRuntimeIdentity(
        version,
        build,
        commit,
        assembly.Location);
    }

    internal static PluginRuntimeIdentity Create(
      string productVersion,
      string buildNumber,
      string commitSha,
      string assemblyPath)
    {
      return new PluginRuntimeIdentity(
        productVersion,
        buildNumber,
        commitSha,
        assemblyPath);
    }

    private static string NormalizeVersion(string value)
    {
      string normalized = string.IsNullOrWhiteSpace(value)
        ? "unknown"
        : value.Trim();
      int metadata = normalized.IndexOf('+');
      return metadata < 0 ? normalized : normalized.Substring(0, metadata);
    }
  }
}
```

- [ ] **Step 4: 在 Revit Add-in 项目中生成元数据**

```xml
<PropertyGroup>
  <HbrBuildNumber Condition="'$(HbrBuildNumber)' == ''">local</HbrBuildNumber>
  <HbrCommitSha Condition="'$(HbrCommitSha)' == ''">unknown</HbrCommitSha>
  <InformationalVersion>$(Version)+build.$(HbrBuildNumber).sha.$(HbrCommitSha)</InformationalVersion>
</PropertyGroup>

<ItemGroup>
  <AssemblyMetadata Include="HBR.BuildNumber" Value="$(HbrBuildNumber)" />
  <AssemblyMetadata Include="HBR.CommitSha" Value="$(HbrCommitSha)" />
</ItemGroup>
```

- [ ] **Step 5: 运行身份测试**

Run:

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release --filter PluginRuntimeIdentityTests
```

Expected: PASS。

- [ ] **Step 6: 提交**

```bash
git add src/BIMBaoGui.RevitAddin/Runtime/PluginRuntimeIdentity.cs \
  src/BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.csproj \
  tests/BIMBaoGui.RevitAddin.Tests/PluginRuntimeIdentityTests.cs
git commit -m "feat: expose loaded Revit add-in runtime identity"
```

---

### Task 3: 在工作台顶部显示版本、构建号、Commit 和 DLL 路径

**Files:**
- Modify: `src/BIMBaoGui.RevitAddin/WorkspaceControl.cs`
- Modify: `tests/test_revit_addin_stage01_ui_contract.py`
- Modify: `tests/test_revit_addin_v041_contract.py`

**Interfaces:**
- Consumes: `PluginRuntimeIdentity.Read(typeof(WorkspaceControl).Assembly)`。
- Produces: 顶部只读身份区；不占用 Stage01/02/03 报告区域。

- [ ] **Step 1: 添加失败的 UI 合同**

```python
def test_workspace_displays_loaded_plugin_identity_at_top():
    source = (
        ROOT / "src/BIMBaoGui.RevitAddin/WorkspaceControl.cs"
    ).read_text(encoding="utf-8")
    assert "PluginRuntimeIdentity.Read" in source
    assert "插件版本" in source
    assert "构建号" in source
    assert "Commit" in source
    assert "DLL 路径" in source
    assert "Assembly.Location" not in source  # 统一由身份读取器负责
    assert "一键自测" not in source
```

- [ ] **Step 2: 运行合同确认失败**

Run:

```powershell
python -m pytest `
  tests/test_revit_addin_stage01_ui_contract.py `
  tests/test_revit_addin_v041_contract.py -q
```

Expected: FAIL，工作台尚未显示插件身份。

- [ ] **Step 3: 修改 WorkspaceControl 顶部结构**

新增成员：

```csharp
private readonly TextBlock _pluginIdentityText;
private readonly TextBox _pluginPathText;
```

构造函数中，在规则数据库和当前文档之前添加：

```csharp
PluginRuntimeIdentity plugin = PluginRuntimeIdentity.Read(
  typeof(WorkspaceControl).Assembly);
_pluginIdentityText = Body(
  "插件版本：" + plugin.ProductVersion
  + "｜构建号：" + plugin.BuildNumber
  + "｜Commit：" + plugin.ShortCommitSha);
_pluginPathText = new TextBox
{
  Text = "DLL 路径：" + plugin.AssemblyPath,
  IsReadOnly = true,
  BorderThickness = new Thickness(0),
  Background = Brushes.Transparent,
  TextWrapping = TextWrapping.NoWrap,
  HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
  ToolTip = plugin.AssemblyPath,
  Margin = new Thickness(0, 2, 0, 2)
};
identityPanel.Children.Add(_pluginIdentityText);
identityPanel.Children.Add(_pluginPathText);
identityPanel.Children.Add(_ruleText);
identityPanel.Children.Add(_documentText);
```

要求：

- 路径控件固定单行，通过横向滚动和 Tooltip 查看完整路径；
- 不使用自动增高的多行报告控件；
- 不在 Stage01、Stage02 或 Stage03 内重复显示第二套版本信息。

- [ ] **Step 4: 运行 UI 合同和 Release 编译**

Run:

```powershell
python -m pytest `
  tests/test_revit_addin_stage01_ui_contract.py `
  tests/test_revit_addin_v041_contract.py -q

dotnet build src/BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.csproj `
  -c Release -p:TreatWarningsAsErrors=true
```

Expected: 全部 PASS，0 warning / 0 error。

- [ ] **Step 5: 提交**

```bash
git add src/BIMBaoGui.RevitAddin/WorkspaceControl.cs \
  tests/test_revit_addin_stage01_ui_contract.py \
  tests/test_revit_addin_v041_contract.py
git commit -m "feat: show loaded add-in identity in Revit workspace"
```

---

### Task 4: 建立 Revit 原生 Stage01 安全默认策略

**Files:**
- Create: `src/BIMBaoGui.RevitAddin/Stage01/NativeStage01DefaultPolicy.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage01DefaultPolicyTests.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Rules/NativeRuleCatalog.cs`

**Interfaces:**
- Consumes: `NativeStage01Model`、`NativeRuleCatalog`。
- Produces:

```csharp
internal sealed class NativeStage01DefaultReconciliation
{
  internal IReadOnlyList<string> AddedFieldKeys { get; }
  internal bool ConditionDeclarationChanged { get; }
  internal bool Changed { get; }
}

internal static class NativeStage01DefaultPolicy
{
  internal static void ApplyForNewModel(
    NativeStage01Model model,
    NativeRuleCatalog catalog);

  internal static NativeStage01DefaultReconciliation ApplyMissingDefaults(
    NativeStage01Model model,
    NativeRuleCatalog catalog);
}
```

- [ ] **Step 1: 写失败的默认策略测试**

```csharp
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage01;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage01DefaultPolicyTests
  {
    [Fact]
    public void NewModelHasSafeSpatialAndConditionDefaults()
    {
      NativeStage01Model model =
        NativeRuleCatalog.Current.CreateDefaultStage01Model();

      Assert.Equal("0", model.GetValue(NativeStage01Keys.BaseX));
      Assert.Equal("0", model.GetValue(NativeStage01Keys.BaseY));
      Assert.Equal("0", model.GetValue(NativeStage01Keys.BaseElevation));
      Assert.Equal("0", model.GetValue(NativeStage01Keys.TrueNorthAngle));
      Assert.True(model.GetCondition(
        NativeProjectConditionDeclarationPolicy.NoneConditionId));
      Assert.All(
        NativeRuleCatalog.Current.Conditions,
        condition => Assert.False(model.GetCondition(condition.ConditionId)));
    }

    [Fact]
    public void MissingDefaultsNeverOverwriteRealValues()
    {
      NativeStage01Model model =
        NativeRuleCatalog.Current.CreateDefaultStage01Model();
      model.SetValue(NativeStage01Keys.BaseX, "123.45");
      model.SetValue(NativeStage01Keys.ProjectName, "真实项目");

      NativeStage01DefaultPolicy.ApplyMissingDefaults(
        model,
        NativeRuleCatalog.Current);

      Assert.Equal("123.45", model.GetValue(NativeStage01Keys.BaseX));
      Assert.Equal("真实项目", model.GetValue(NativeStage01Keys.ProjectName));
    }

    [Fact]
    public void BusinessFactsRemainEmpty()
    {
      NativeStage01Model model =
        NativeRuleCatalog.Current.CreateDefaultStage01Model();

      Assert.Equal(string.Empty, model.GetValue(NativeStage01Keys.ProjectName));
      Assert.Equal(string.Empty, model.GetValue(NativeStage01Keys.ProjectNumber));
      Assert.Equal(string.Empty, model.GetValue(NativeStage01Keys.SubitemName));
    }
  }
}
```

- [ ] **Step 2: 运行测试确认 X/Y/高程和 none 默认尚未满足**

Run:

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release --filter NativeStage01DefaultPolicyTests
```

Expected: FAIL。

- [ ] **Step 3: 实现最小默认策略**

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using BIMBaoGui.RevitAddin.Rules;

namespace BIMBaoGui.RevitAddin.Stage01
{
  internal sealed class NativeStage01DefaultReconciliation
  {
    internal NativeStage01DefaultReconciliation(
      IEnumerable<string> addedFieldKeys,
      bool conditionDeclarationChanged)
    {
      AddedFieldKeys = new ReadOnlyCollection<string>(
        new List<string>(addedFieldKeys ?? Array.Empty<string>()));
      ConditionDeclarationChanged = conditionDeclarationChanged;
    }

    internal IReadOnlyList<string> AddedFieldKeys { get; }
    internal bool ConditionDeclarationChanged { get; }
    internal bool Changed =>
      AddedFieldKeys.Count > 0 || ConditionDeclarationChanged;
  }

  internal static class NativeStage01DefaultPolicy
  {
    private static readonly KeyValuePair<string, string>[] SafeDefaults =
    {
      new KeyValuePair<string, string>(NativeStage01Keys.BaseX, "0"),
      new KeyValuePair<string, string>(NativeStage01Keys.BaseY, "0"),
      new KeyValuePair<string, string>(NativeStage01Keys.BaseElevation, "0"),
      new KeyValuePair<string, string>(NativeStage01Keys.TrueNorthAngle, "0"),
      new KeyValuePair<string, string>(NativeStage01Keys.LengthUnit, "m"),
      new KeyValuePair<string, string>(NativeStage01Keys.AreaUnit, "m²"),
      new KeyValuePair<string, string>(NativeStage01Keys.AngleUnit, "°")
    };

    internal static void ApplyForNewModel(
      NativeStage01Model model,
      NativeRuleCatalog catalog)
    {
      ApplyMissingDefaults(model, catalog);
    }

    internal static NativeStage01DefaultReconciliation ApplyMissingDefaults(
      NativeStage01Model model,
      NativeRuleCatalog catalog)
    {
      if (model == null) throw new ArgumentNullException(nameof(model));
      if (catalog == null) throw new ArgumentNullException(nameof(catalog));
      var added = new List<string>();
      foreach (KeyValuePair<string, string> pair in SafeDefaults)
      {
        if (!string.IsNullOrWhiteSpace(model.GetValue(pair.Key))) continue;
        model.SetValue(pair.Key, pair.Value);
        added.Add(pair.Key);
      }
      bool conditionChanged =
        NativeProjectConditionDeclarationPolicy.NormalizeLoadedDeclaration(
          model,
          catalog,
          defaultToNoneWhenEmpty: true);
      return new NativeStage01DefaultReconciliation(
        added,
        conditionChanged);
    }
  }
}
```

- [ ] **Step 4: 在 NativeRuleCatalog 创建模型后调用策略**

在 `CreateDefaultStage01Model()` 完成数据库默认和条件默认后、返回前加入：

```csharp
NativeStage01DefaultPolicy.ApplyForNewModel(model, this);
```

- [ ] **Step 5: 运行默认策略和既有规则目录测试**

Run:

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release `
  --filter "NativeStage01DefaultPolicyTests|NativeRuleCatalogTests"
```

Expected: PASS。

- [ ] **Step 6: 提交**

```bash
git add src/BIMBaoGui.RevitAddin/Stage01/NativeStage01DefaultPolicy.cs \
  src/BIMBaoGui.RevitAddin/Rules/NativeRuleCatalog.cs \
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage01DefaultPolicyTests.cs
git commit -m "feat: add safe native Stage01 defaults"
```

---

### Task 5: 归一项目条件声明并兼容旧 Payload

**Files:**
- Modify: `src/BIMBaoGui.RevitAddin/Stage01/NativeProjectConditionDeclarationPolicy.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage01/NativeStage01ConditionSchemaPolicy.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage01/NativeStage01PayloadCodec.cs`
- Modify: `tests/BIMBaoGui.RevitAddin.Tests/NativeProjectConditionDeclarationPolicyTests.cs`
- Modify: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage01ConditionSchemaPolicyTests.cs`
- Modify: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage01PayloadCodecTests.cs`

**Interfaces:**
- Consumes: 已解码或旧版本的 `NativeStage01Model`。
- Produces:

```csharp
internal static bool NormalizeLoadedDeclaration(
  NativeStage01Model model,
  NativeRuleCatalog catalog,
  bool defaultToNoneWhenEmpty);
```

- [ ] **Step 1: 添加失败的互斥和迁移测试**

```csharp
[Fact]
public void EmptyLegacyDeclarationDefaultsToConfirmedNone()
{
  NativeStage01Model model = new NativeStage01Model();
  foreach (NativeConditionDefinition condition in
    NativeRuleCatalog.Current.Conditions)
  {
    model.SetCondition(condition.ConditionId, false);
  }

  bool changed = NativeProjectConditionDeclarationPolicy
    .NormalizeLoadedDeclaration(
      model,
      NativeRuleCatalog.Current,
      defaultToNoneWhenEmpty: true);

  Assert.True(changed);
  Assert.True(model.GetCondition(
    NativeProjectConditionDeclarationPolicy.NoneConditionId));
}

[Fact]
public void ActualConditionsWinWhenLegacyPayloadConflicts()
{
  NativeStage01Model model = new NativeStage01Model();
  string actual = NativeRuleCatalog.Current.Conditions[0].ConditionId;
  model.SetCondition(actual, true);
  model.SetCondition(
    NativeProjectConditionDeclarationPolicy.NoneConditionId,
    true);

  bool changed = NativeProjectConditionDeclarationPolicy
    .NormalizeLoadedDeclaration(
      model,
      NativeRuleCatalog.Current,
      defaultToNoneWhenEmpty: true);

  Assert.True(changed);
  Assert.True(model.GetCondition(actual));
  Assert.False(model.GetCondition(
    NativeProjectConditionDeclarationPolicy.NoneConditionId));
}
```

- [ ] **Step 2: 运行测试确认 NormalizeLoadedDeclaration 不存在**

Run:

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release `
  --filter "NativeProjectConditionDeclarationPolicyTests|NativeStage01PayloadCodecTests"
```

Expected: FAIL。

- [ ] **Step 3: 实现确定性归一**

```csharp
internal static bool NormalizeLoadedDeclaration(
  NativeStage01Model model,
  NativeRuleCatalog catalog,
  bool defaultToNoneWhenEmpty)
{
  if (model == null) throw new ArgumentNullException(nameof(model));
  if (catalog == null) throw new ArgumentNullException(nameof(catalog));
  bool changed = false;
  bool hasActual = catalog.Conditions.Any(condition =>
    model.GetCondition(condition.ConditionId));
  bool none = model.GetCondition(NoneConditionId);

  if (hasActual && none)
  {
    model.SetCondition(NoneConditionId, false);
    changed = true;
  }
  else if (!hasActual && !none && defaultToNoneWhenEmpty)
  {
    model.SetCondition(NoneConditionId, true);
    changed = true;
  }
  return changed;
}
```

- [ ] **Step 4: 在 Payload 解码和条件 Schema 补齐后调用默认策略**

`NativeStage01PayloadCodec.TryApply(...)` 在字段、组织、条件和 PlanningTargets 全部解码完成后调用：

```csharp
NativeStage01ConditionSchemaPolicy.Reconcile(model, catalog);
NativeStage01DefaultPolicy.ApplyMissingDefaults(model, catalog);
```

要求：

- 不改变 `NativeStage01Canonicalizer.PayloadSchemaVersion`；
- 不删除未知键；
- 不覆盖非空字段；
- 重新编码后的 canonical JSON 保存 `workflow.project_conditions.none`。

- [ ] **Step 5: 运行条件、Payload 和 canonical 测试**

Run:

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release `
  --filter "NativeProjectConditionDeclarationPolicyTests|NativeStage01ConditionSchemaPolicyTests|NativeStage01PayloadCodecTests|NativeStage01CanonicalizerTests"
```

Expected: PASS。

- [ ] **Step 6: 提交**

```bash
git add src/BIMBaoGui.RevitAddin/Stage01/NativeProjectConditionDeclarationPolicy.cs \
  src/BIMBaoGui.RevitAddin/Stage01/NativeStage01ConditionSchemaPolicy.cs \
  src/BIMBaoGui.RevitAddin/Stage01/NativeStage01PayloadCodec.cs \
  tests/BIMBaoGui.RevitAddin.Tests/NativeProjectConditionDeclarationPolicyTests.cs \
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage01ConditionSchemaPolicyTests.cs \
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage01PayloadCodecTests.cs
git commit -m "fix: normalize native project condition declarations"
```

---

### Task 6: 保证 Revit 实际值和既有 Payload 优先于默认值

**Files:**
- Modify: `src/BIMBaoGui.RevitAddin/Stage01/NativeStage01RevitReadService.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage01/NativeStage01ViewModel.cs`
- Modify: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage01DefaultPolicyTests.cs`
- Modify: `tests/test_revit_addin_stage01_revit_contract.py`

**Interfaces:**
- Consumes: 当前 RVT 的 `ProjectInformation`、`ProjectPosition`、Storage Payload。
- Produces: 已归一但不伪造业务事实的 `NativeStage01ReadResult.Model`。

- [ ] **Step 1: 添加读取优先级合同**

```python
def test_stage01_read_applies_defaults_without_overwriting_document_values():
    source = read_stage01("NativeStage01RevitReadService.cs")
    assert "PopulateMissingDocumentValues" in source
    assert "NativeStage01DefaultPolicy.ApplyMissingDefaults" in source
    assert source.index("PopulateMissingDocumentValues") < source.index(
        "NativeStage01DefaultPolicy.ApplyMissingDefaults"
    )
    assert "SetIfBlank" in source
```

同时在 xUnit 中增加：

```csharp
[Fact]
public void ApplyMissingDefaultsPreservesDocumentSpatialValues()
{
  NativeStage01Model model = new NativeStage01Model();
  model.SetValue(NativeStage01Keys.BaseX, "998.25");
  model.SetValue(NativeStage01Keys.BaseY, "112.5");
  model.SetValue(NativeStage01Keys.BaseElevation, "35.8");

  NativeStage01DefaultPolicy.ApplyMissingDefaults(
    model,
    NativeRuleCatalog.Current);

  Assert.Equal("998.25", model.GetValue(NativeStage01Keys.BaseX));
  Assert.Equal("112.5", model.GetValue(NativeStage01Keys.BaseY));
  Assert.Equal("35.8", model.GetValue(NativeStage01Keys.BaseElevation));
}
```

- [ ] **Step 2: 修改读取顺序**

`NativeStage01RevitReadService.Read` 的稳定顺序固定为：

```text
1. 读取并判定 Storage
2. Storage 可用则克隆 Payload，否则创建默认模型
3. 补齐数据库新增实际条件键
4. 从当前 RVT 读取项目名称、编号、X、Y、高程、真北和单位；只写入空字段
5. ApplyMissingDefaults：只补仍为空的安全基线，并归一 none 声明
6. 执行 Stage01 校验
```

在 `PopulateMissingDocumentValues(...)` 后加入：

```csharp
NativeStage01DefaultReconciliation defaults =
  NativeStage01DefaultPolicy.ApplyMissingDefaults(model, catalog);
if (defaults.Changed)
{
  messages.Add(
    "已补齐 Revit 原生 Stage01 安全默认值；"
    + "未覆盖当前 RVT 或既有初始化记录中的非空真实值。" );
}
```

`NativeStage01ViewModel.LoadModel` 增加防御性归一：

```csharp
_model = (model ?? _catalog.CreateDefaultStage01Model()).Clone();
NativeStage01DefaultPolicy.ApplyMissingDefaults(_model, _catalog);
```

- [ ] **Step 3: 运行读取合同和 Stage01 领域测试**

Run:

```powershell
python -m pytest tests/test_revit_addin_stage01_revit_contract.py -q

dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release `
  --filter "NativeStage01DefaultPolicyTests|NativeStage01ValidatorTests|NativeStage01PayloadCodecTests"
```

Expected: PASS。

- [ ] **Step 4: 提交**

```bash
git add src/BIMBaoGui.RevitAddin/Stage01/NativeStage01RevitReadService.cs \
  src/BIMBaoGui.RevitAddin/Stage01/NativeStage01ViewModel.cs \
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage01DefaultPolicyTests.cs \
  tests/test_revit_addin_stage01_revit_contract.py
git commit -m "fix: preserve Revit values while applying Stage01 defaults"
```

---

### Task 7: 注入 CI 构建身份并统一安装器和安装包版本

**Files:**
- Modify: `.github/workflows/build-revit-mcp.yml`
- Modify: `installer/Install-Revit2020.ps1`
- Modify: `tests/test_revit_addin_installer_contract.py`
- Modify: `tests/test_revit_addin_mcp_installer_contract.py`
- Modify: `tests/test_revit_addin_v041_contract.py`
- Modify: `docs/revit-addin/README.md`

**Interfaces:**
- Consumes: `github.run_number`、`github.sha`、0.4.1 产品项目。
- Produces: 带真实程序集元数据的 DLL、`0.4.1` MCP 安装目录和统一 ZIP Artifact。

- [ ] **Step 1: 扩展 v0.4.1 CI 合同测试**

```python
def test_ci_injects_build_number_and_commit_sha():
    workflow = (
        ROOT / ".github/workflows/build-revit-mcp.yml"
    ).read_text(encoding="utf-8")
    assert "HbrBuildNumber=${{ github.run_number }}" in workflow
    assert "HbrCommitSha=${{ github.sha }}" in workflow
    assert "BIMBaoGui-Revit2020-Native-MCP-v0.4.1" in workflow


def test_installer_uses_041_mcp_directory():
    installer = (
        ROOT / "installer/Install-Revit2020.ps1"
    ).read_text(encoding="utf-8")
    assert '$mcpVersion = "0.4.1"' in installer
```

- [ ] **Step 2: 运行合同确认当前仍为 0.4.0**

Run:

```powershell
python -m pytest `
  tests/test_revit_addin_v041_contract.py `
  tests/test_revit_addin_installer_contract.py `
  tests/test_revit_addin_mcp_installer_contract.py -q
```

Expected: FAIL。

- [ ] **Step 3: 修改 dotnet test/build 命令注入元数据**

对 Revit Add-in 的测试和正式构建均传入：

```powershell
-p:HbrBuildNumber=${{ github.run_number }} `
-p:HbrCommitSha=${{ github.sha }}
```

正式构建命令示例：

```powershell
dotnet build src/BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.csproj `
  -c Release `
  --no-restore `
  -p:ContinuousIntegrationBuild=true `
  -p:TreatWarningsAsErrors=true `
  -p:HbrBuildNumber=${{ github.run_number }} `
  -p:HbrCommitSha=${{ github.sha }}
```

- [ ] **Step 4: 增加程序集元数据验证步骤**

在构建后增加 PowerShell：

```powershell
$dll = Resolve-Path "src/BIMBaoGui.RevitAddin/bin/Release/net48/BIMBaoGui.RevitAddin.dll"
$assembly = [Reflection.Assembly]::LoadFile($dll.Path)
$metadata = @{}
foreach ($attribute in $assembly.GetCustomAttributesData()) {
  if ($attribute.AttributeType.FullName -ne "System.Reflection.AssemblyMetadataAttribute") { continue }
  $metadata[[string]$attribute.ConstructorArguments[0].Value] =
    [string]$attribute.ConstructorArguments[1].Value
}
if ($metadata["HBR.BuildNumber"] -ne "${{ github.run_number }}") {
  throw "Revit DLL build number metadata mismatch."
}
if ($metadata["HBR.CommitSha"] -ne "${{ github.sha }}") {
  throw "Revit DLL commit metadata mismatch."
}
if ($assembly.GetName().Version.ToString() -ne "0.4.1.0") {
  throw "Revit DLL assembly version mismatch."
}
```

- [ ] **Step 5: 更新安装器和工作流中的 0.4.1 路径**

固定修改：

```text
$mcpVersion = "0.4.1"
MCP 安装目录：%LOCALAPPDATA%\BIMBaoGui\McpServer\0.4.1
Artifact：BIMBaoGui-Revit2020-Native-MCP-v0.4.1
```

Smoke Test 必须：

- 预先创建 `0.4.0` 旧目录；
- 安装后确认 `0.4.0` 已清理、`0.4.1` 存在；
- 验证 Revit DLL、Contracts DLL、HifcCore DLL、MCP EXE 哈希；
- 验证卸载后当前版本目录和 `.addin` 全部删除。

- [ ] **Step 6: 更新 README**

README 明确说明：

```text
版本：0.4.1
顶部身份信息来自当前 Revit 实际加载 DLL
Build/Commit 由 CI 写入程序集元数据
本地开发构建显示 Build local / Commit unknown
Stage01 默认 0 坐标是初始化基线，RVT 实际值优先
无上述项目条件默认确认，可由实际条件自动取消
```

- [ ] **Step 7: 运行合同测试**

Run:

```powershell
python -m pytest `
  tests/test_revit_addin_v041_contract.py `
  tests/test_revit_addin_installer_contract.py `
  tests/test_revit_addin_mcp_installer_contract.py -q
```

Expected: PASS。

- [ ] **Step 8: 提交**

```bash
git add .github/workflows/build-revit-mcp.yml \
  installer/Install-Revit2020.ps1 \
  tests/test_revit_addin_installer_contract.py \
  tests/test_revit_addin_mcp_installer_contract.py \
  tests/test_revit_addin_v041_contract.py \
  docs/revit-addin/README.md
git commit -m "build: package Revit native product v0.4.1"
```

---

### Task 8: 完整回归、代码审查和可安装包验收

**Files:**
- Review: all v0.4.1 changed files
- Verify: `.github/workflows/build-revit-mcp.yml`
- Artifact: `BIMBaoGui-Revit2020-Native-MCP-v0.4.1.zip`

**Interfaces:**
- Consumes: Tasks 1–7 的全部实现。
- Produces: 可覆盖安装的统一 v0.4.1 包及可追溯构建证据。

- [ ] **Step 1: 确认未修改 GHA 产品线**

Run:

```bash
git diff --name-only e57473d813f58e9e528b2afc69d31d4e32b602cf...HEAD
```

Expected: 不出现：

```text
src/BIMBaoGui.Stage01/
.github/workflows/build-stage01-gha.yml
```

- [ ] **Step 2: 运行全部 Python 合同**

Run:

```powershell
python -m pytest `
  tests/test_revit_addin_mcp_non_regression.py `
  tests/test_revit_addin_scaffold_contract.py `
  tests/test_revit_addin_installer_contract.py `
  tests/test_revit_addin_stage01_storage_contract.py `
  tests/test_revit_addin_stage01_revit_contract.py `
  tests/test_revit_addin_stage01_ui_contract.py `
  tests/test_revit_addin_stage02_revit_contract.py `
  tests/test_revit_addin_stage03_revit_contract.py `
  tests/test_revit_addin_stage03_ui_contract.py `
  tests/test_revit_addin_mcp_contract.py `
  tests/test_revit_addin_mcp_stage03_contract.py `
  tests/test_revit_addin_mcp_installer_contract.py `
  tests/test_revit_addin_v041_contract.py `
  -q
```

Expected: 全部 PASS。

- [ ] **Step 3: 验证共享 HBR 数据库未漂移**

Run:

```powershell
python -m pytest `
  tests/test_hbr_rulepack_compiler.py `
  tests/test_hbr_rules_manifest.py -q
```

Expected: PASS；规则包 Entity/Pset/Property/GUID/类型/单位不变。

- [ ] **Step 4: 运行全部 .NET 测试**

Run:

```powershell
dotnet test tests/BIMBaoGui.HifcCore.Tests/BIMBaoGui.HifcCore.Tests.csproj `
  -c Release

dotnet test tests/BIMBaoGui.McpContracts.Tests/BIMBaoGui.McpContracts.Tests.csproj `
  -c Release

dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release `
  -p:HbrBuildNumber=local-test `
  -p:HbrCommitSha=0123456789abcdef
```

Expected: 全部 PASS。

- [ ] **Step 5: Release 编译**

Run:

```powershell
dotnet build src/BIMBaoGui.HifcCore/BIMBaoGui.HifcCore.csproj `
  -c Release -p:TreatWarningsAsErrors=true

dotnet build src/BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.csproj `
  -c Release `
  -p:TreatWarningsAsErrors=true `
  -p:HbrBuildNumber=local-test `
  -p:HbrCommitSha=0123456789abcdef

dotnet build src/BIMBaoGui.McpServer/BIMBaoGui.McpServer.csproj `
  -c Release -p:TreatWarningsAsErrors=true
```

Expected: 0 warning / 0 error。

- [ ] **Step 6: 运行 Windows 安装/卸载 Smoke**

必须验证：

```text
安装 v0.4.1
→ 生成绝对路径 .addin
→ 安装四类二进制
→ SHA-256 与 install-evidence 一致
→ MCP 配置指向 0.4.1 绝对路径
→ 清理旧 0.4.0 目录
→ 卸载后无残留
```

- [ ] **Step 7: GitHub Actions 构建最终 Artifact**

Expected Artifact：

```text
BIMBaoGui-Revit2020-Native-MCP-v0.4.1.zip
```

- [ ] **Step 8: 解压后复核安装包**

```powershell
Get-FileHash .\BIMBaoGui-Revit2020-Native-MCP-v0.4.1.zip -Algorithm SHA256
```

并逐项核对 `SHA256SUMS.txt`。

- [ ] **Step 9: Revit 2020 人工验收清单**

在关闭 Revit 后覆盖安装，再启动 Revit 2020：

```text
1. 工作台顶部显示 v0.4.1。
2. Build 不为 local，Commit 不为 unknown。
3. DLL 路径与 %APPDATA%\Autodesk\Revit\Addins\2020\... 实际安装路径一致。
4. 新建/无记录文件打开 Stage01：X=0、Y=0、高程=0、真北=0。
5. “无上述项目条件（已确认）”默认勾选。
6. 勾选任一实际条件后 none 自动取消；反向操作清空实际条件。
7. 点击“读取当前文件”后，RVT 实际坐标与项目名称覆盖默认基线。
8. 既有非空 Stage01 Payload 不被默认值覆盖。
9. Stage02、Stage03 和 MCP 工具行为与 v0.4.0 保持一致。
```

- [ ] **Step 10: 提交最终验证记录**

```bash
git add docs/revit-addin/README.md
git commit -m "docs: record Revit native v0.4.1 verification"
```

---

## 三、发布判定

只有同时满足以下条件，才能称为 **v0.4.1 可安装测试版**：

1. 所有 Python 合同和 .NET 测试通过；
2. Revit Add-in、HifcCore、MCP Contracts、MCP Server 均为 `0.4.1`；
3. CI 反射读取到真实 Build Number 和 Commit SHA；
4. 安装/卸载 smoke 通过；
5. 安装包哈希清单完整；
6. 未修改 GHA 产品线；
7. 用户在真实 Revit 2020 中看到的顶部版本、构建号和 DLL 路径与安装证据一致。

CI 只能证明代码、协议、编译和安装结构；Revit 2020 中的实际加载身份仍以工作台顶部显示为最终依据。
