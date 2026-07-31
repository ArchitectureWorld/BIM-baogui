# Stage 01 → Stage 02 Context Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将现有 `01 文件初始化` 升级为后续报规组件的正式数据源，输出强类型 `HBR_FileContext`；新增 `02 模型任务与骨架分流`，根据模型类型、规划控制目标和项目条件编译强类型 `HBR_TaskPlan`。

**Architecture:** 在现有 `BIMBaoGui.Stage01.gha` 内增加纯业务领域对象、Grasshopper `GH_Goo`/`GH_PersistentParam` 类型和第二阶段组件。第一阶段继续通过 Rhino.Inside.Revit 直接读写 Revit 2020，并以 Extensible Storage 持久化；Grasshopper 连线承担显式依赖，第二阶段只接受第一阶段输出的强类型上下文，不静默读取隐藏配置。

**Tech Stack:** C#、.NET Framework 4.8、Revit 2020 API、Rhino 8、Grasshopper 8、Rhino.Inside.Revit、xUnit、pytest、GitHub Actions。

## Global Constraints

- 运行基线固定为 **Revit 2020 + Rhino 8 + Rhino.Inside.Revit**。
- Rhino 和 Grasshopper 必须从 Revit 内启动；不支持独立 Rhino 会话写入 Revit。
- 第一代仍是编译型 Grasshopper `.gha`，不增加 Revit WPF/Dockable Pane，不依赖 Human UI、MetaHopper 等第三方插件。
- 所有用户交互继续位于 Grasshopper 组件卡片内；用户可见文字使用中文。
- 现有 `Stage01Component.ComponentGuid = 84a95cc7-2020-4c2e-9e1b-bdfc2b02bb70` 不得改变。
- 本轮插件版本统一升级为 `0.5.0`。
- 总平模型的建筑密度、容积率、绿地率必须填写结构化目标值；不得用虚假默认业务值代替缺失输入。
- 单体建筑—地上、单体建筑—地下不重复人工填写上述项目总目标，状态标记为“继承”；本轮只生成继承任务，不实现跨文件项目级汇总。
- Revit 写入必须由明确按钮触发，写入后必须回读；失败时事务组整体回滚。
- 第二阶段必须通过 Grasshopper 数据线接收 `HBR_FileContext`；读取 Revit 存储仅用于核验当前文档身份，不得绕过连线取得业务配置。
- `FileContextHash`、`TaskPlanHash` 必须由确定性规范化载荷计算；字典插入顺序不得影响哈希。
- 本轮不实现总平、地上、地下的具体几何建模算法，只产出任务和骨架计划。
- 实施时从 `feat/stage01-gha-file-initialization` 创建隔离分支 `feat/stage01-stage02-context-pipeline`，不得直接改 `main`。

---

## File Structure

### 新建文件

- `src/BIMBaoGui.Stage01/Core/PlanningTargetValue.cs`：结构化规划目标值、运算符、单位和稳定 MVD 文本序列化。
- `src/BIMBaoGui.Stage01/Core/PlanningTargetCatalog.cs`：建筑密度、容积率、绿地率的元数据、MVD 字段映射和示例。
- `src/BIMBaoGui.Stage01/Core/PlanningTargetRequirementPolicy.cs`：按模型类型返回必填、继承、条件必填、选填状态。
- `src/BIMBaoGui.Stage01/Context/HBRSpatialReference.cs`：不可变坐标与高程上下文。
- `src/BIMBaoGui.Stage01/Context/HBRFileContext.cs`：不可变文件上下文业务对象。
- `src/BIMBaoGui.Stage01/Context/HBRFileContextCanonicalizer.cs`：上下文规范化 JSON、哈希和反序列化。
- `src/BIMBaoGui.Stage01/Context/HBRFileContextFactory.cs`：由 Stage 01 模型、Revit 快照和初始化状态创建文件上下文。
- `src/BIMBaoGui.Stage01/Context/RuleActivationCatalog.cs`：模型类型及项目条件到激活/不适用规则 ID 的映射。
- `src/BIMBaoGui.Stage01/GrasshopperTypes/HBRFileContextGoo.cs`：`GH_Goo<HBRFileContext>` 封装。
- `src/BIMBaoGui.Stage01/GrasshopperTypes/HBRFileContextParam.cs`：文件上下文专用 Grasshopper 参数。
- `src/BIMBaoGui.Stage01/TaskPlanning/HBRTaskPlan.cs`：不可变任务计划业务对象。
- `src/BIMBaoGui.Stage01/TaskPlanning/TaskRuleDefinition.cs`：任务规则定义。
- `src/BIMBaoGui.Stage01/TaskPlanning/TaskRuleCatalog.cs`：总平、地上、地下的首批任务规则。
- `src/BIMBaoGui.Stage01/TaskPlanning/TaskPlanCompiler.cs`：根据文件上下文编译任务计划。
- `src/BIMBaoGui.Stage01/TaskPlanning/HBRTaskPlanCanonicalizer.cs`：任务计划规范化 JSON、哈希和反序列化。
- `src/BIMBaoGui.Stage01/GrasshopperTypes/HBRTaskPlanGoo.cs`：`GH_Goo<HBRTaskPlan>` 封装。
- `src/BIMBaoGui.Stage01/GrasshopperTypes/HBRTaskPlanParam.cs`：任务计划专用 Grasshopper 参数。
- `src/BIMBaoGui.Stage01/Revit/Stage02RevitContextService.cs`：读取当前 Revit 文档身份，用于 Stage 02 核验。
- `src/BIMBaoGui.Stage01/Stage02TaskPlanComponent.cs`：`02 模型任务与骨架分流` 组件。
- `src/BIMBaoGui.Stage01/UI/PlanningTargetEditor.cs`：组件内结构化目标编辑器。
- `src/BIMBaoGui.Stage01/UI/Stage02ComponentAttributes.cs`：第二阶段任务摘要卡片。
- `tests/BIMBaoGui.Stage01.Core.Tests/PlanningTargetValueTests.cs`
- `tests/BIMBaoGui.Stage01.Core.Tests/PlanningTargetRequirementPolicyTests.cs`
- `tests/BIMBaoGui.Stage01.Core.Tests/HBRFileContextTests.cs`
- `tests/BIMBaoGui.Stage01.Core.Tests/TaskPlanCompilerTests.cs`
- `tests/BIMBaoGui.Stage01.Core.Tests/HBRTaskPlanTests.cs`
- `docs/revit2020-stage01-stage02-runtime-checklist.md`

### 修改文件

- `src/BIMBaoGui.Stage01/Core/Stage01Model.cs`：保存结构化规划目标并支持克隆。
- `src/BIMBaoGui.Stage01/Core/CanonicalPayload.cs`：把结构化目标纳入 Stage 01 确定性载荷。
- `src/BIMBaoGui.Stage01/Core/Stage01PayloadCodec.cs`：支持新目标结构及旧 MVD 文本兼容读取。
- `src/BIMBaoGui.Stage01/Core/Stage01Validation.cs`：动态必填矩阵和目标值校验。
- `src/BIMBaoGui.Stage01/Core/FieldInputRules.cs`：规划指标输入提示与格式校验。
- `src/BIMBaoGui.Stage01/Infrastructure/Stage01RegistryProvider.cs`：将三个规划目标字段交由结构化编辑器管理。
- `src/BIMBaoGui.Stage01/Revit/RevitModels.cs`：补充当前文件 GUID、指纹和存储版本信息。
- `src/BIMBaoGui.Stage01/Revit/Stage01RevitService.cs`：写入/恢复结构化目标，并创建文件上下文所需快照。
- `src/BIMBaoGui.Stage01/Stage01Component.cs`：输出强类型文件上下文。
- `src/BIMBaoGui.Stage01/UI/Stage01ComponentAttributes.cs`：绘制结构化目标行、动态状态和编辑命中区域。
- `src/BIMBaoGui.Stage01/UI/InlineEditor.cs`：复用带即时校验的数值输入。
- `src/BIMBaoGui.Stage01/AssemblyInfo.cs`：版本改为 `0.5.0`。
- `src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj`：版本改为 `0.5.0`。
- `tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj`：链接新增纯业务文件。
- `tests/BIMBaoGui.Stage01.Core.Tests/Stage01ValidationTests.cs`：补充总平必填和非总平继承测试。
- `tests/test_plugin_contract.py`：检查强类型端口、第二阶段组件和版本。
- `README.md`：更新组件链和安装/使用说明。

---

### Task 1: 建立结构化规划目标领域模型

**Files:**
- Create: `src/BIMBaoGui.Stage01/Core/PlanningTargetValue.cs`
- Create: `src/BIMBaoGui.Stage01/Core/PlanningTargetCatalog.cs`
- Create: `src/BIMBaoGui.Stage01/Core/PlanningTargetRequirementPolicy.cs`
- Create: `tests/BIMBaoGui.Stage01.Core.Tests/PlanningTargetValueTests.cs`
- Create: `tests/BIMBaoGui.Stage01.Core.Tests/PlanningTargetRequirementPolicyTests.cs`
- Modify: `tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj`

**Interfaces:**
- Produces: `PlanningTargetValue.TryCreate(...)`, `PlanningTargetValue.ToMvdText()`, `PlanningTargetCatalog.All`, `PlanningTargetRequirementPolicy.GetRequirement(...)`。
- Consumes: 现有模型类型字符串：`总平模型`、`单体建筑—地上`、`单体建筑—地下`。

- [ ] **Step 1: 写结构化目标值失败测试**

```csharp
[Theory]
[InlineData("planning.building_density", PlanningTargetOperator.LessOrEqual, "30", null, PlanningTargetUnit.Percent, "≤30%")]
[InlineData("planning.floor_area_ratio", PlanningTargetOperator.LessOrEqual, "2.00", null, PlanningTargetUnit.Ratio, "≤2.00")]
[InlineData("planning.green_rate", PlanningTargetOperator.GreaterOrEqual, "35", null, PlanningTargetUnit.Percent, "≥35%")]
public void ToMvdText_IsStable(
  string metricCode,
  PlanningTargetOperator op,
  string value1,
  string value2,
  PlanningTargetUnit unit,
  string expected)
{
  Assert.True(PlanningTargetValue.TryCreate(
    metricCode,
    op,
    value1,
    value2,
    unit,
    "项目初始化",
    out PlanningTargetValue target,
    out string error), error);

  Assert.Equal(expected, target.ToMvdText());
}

[Theory]
[InlineData("-1", "百分比必须位于 0 到 100。")]
[InlineData("101", "百分比必须位于 0 到 100。")]
[InlineData("abc", "应填写数值，例如 30。")]
public void PercentTarget_RejectsInvalidValues(string value, string expected)
{
  Assert.False(PlanningTargetValue.TryCreate(
    "planning.building_density",
    PlanningTargetOperator.LessOrEqual,
    value,
    null,
    PlanningTargetUnit.Percent,
    "项目初始化",
    out _,
    out string error));

  Assert.Equal(expected, error);
}
```

- [ ] **Step 2: 运行测试并确认失败**

Run:

```bash
dotnet test tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj -c Release --filter PlanningTargetValueTests
```

Expected: 编译失败，提示 `PlanningTargetValue`、`PlanningTargetOperator`、`PlanningTargetUnit` 尚不存在。

- [ ] **Step 3: 实现规划目标类型**

```csharp
namespace BIMBaoGui.Stage01.Core
{
  internal enum PlanningTargetOperator
  {
    LessOrEqual,
    GreaterOrEqual,
    Equal,
    Range
  }

  internal enum PlanningTargetUnit
  {
    Percent,
    Ratio,
    Count
  }

  internal enum PlanningTargetRequirement
  {
    Required,
    Conditional,
    Optional,
    Inherited,
    NotApplicable
  }

  internal sealed class PlanningTargetValue
  {
    private PlanningTargetValue(
      string metricCode,
      PlanningTargetOperator @operator,
      decimal value1,
      decimal? value2,
      PlanningTargetUnit unit,
      string source)
    {
      MetricCode = metricCode;
      Operator = @operator;
      Value1 = value1;
      Value2 = value2;
      Unit = unit;
      Source = source ?? string.Empty;
    }

    public string MetricCode { get; }
    public PlanningTargetOperator Operator { get; }
    public decimal Value1 { get; }
    public decimal? Value2 { get; }
    public PlanningTargetUnit Unit { get; }
    public string Source { get; }

    public static bool TryCreate(
      string metricCode,
      PlanningTargetOperator @operator,
      string value1,
      string value2,
      PlanningTargetUnit unit,
      string source,
      out PlanningTargetValue target,
      out string error)
    {
      target = null;
      error = string.Empty;
      if (!decimal.TryParse(value1, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal first))
      {
        error = "应填写数值，例如 30。";
        return false;
      }

      decimal? second = null;
      if (@operator == PlanningTargetOperator.Range)
      {
        if (!decimal.TryParse(value2, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsedSecond))
        {
          error = "区间上限必须填写数值。";
          return false;
        }
        if (parsedSecond < first)
        {
          error = "区间上限不得小于下限。";
          return false;
        }
        second = parsedSecond;
      }

      if (unit == PlanningTargetUnit.Percent && (first < 0m || first > 100m || (second.HasValue && second.Value > 100m)))
      {
        error = "百分比必须位于 0 到 100。";
        return false;
      }
      if (unit != PlanningTargetUnit.Percent && (first < 0m || (second.HasValue && second.Value < 0m)))
      {
        error = "数值不得小于 0。";
        return false;
      }

      target = new PlanningTargetValue(metricCode, @operator, first, second, unit, source);
      return true;
    }

    public string ToMvdText()
    {
      string suffix = Unit == PlanningTargetUnit.Percent ? "%" : string.Empty;
      string first = Format(Value1) + suffix;
      if (Operator == PlanningTargetOperator.Range)
        return first + "–" + Format(Value2.Value) + suffix;
      return OperatorSymbol(Operator) + first;
    }

    private static string Format(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string OperatorSymbol(PlanningTargetOperator value)
    {
      switch (value)
      {
        case PlanningTargetOperator.LessOrEqual: return "≤";
        case PlanningTargetOperator.GreaterOrEqual: return "≥";
        case PlanningTargetOperator.Equal: return "=";
        default: return string.Empty;
      }
    }
  }
}
```

`PlanningTargetCatalog` 必须定义三项固定映射：

```csharp
new PlanningTargetDefinition(
  "planning.building_density",
  "IfcProject|Pset_项目控制指标信息属性集|建筑密度",
  "建筑密度",
  PlanningTargetUnit.Percent,
  PlanningTargetOperator.LessOrEqual,
  "30"),
new PlanningTargetDefinition(
  "planning.floor_area_ratio",
  "IfcProject|Pset_项目控制指标信息属性集|容积率",
  "容积率",
  PlanningTargetUnit.Ratio,
  PlanningTargetOperator.LessOrEqual,
  "2.00"),
new PlanningTargetDefinition(
  "planning.green_rate",
  "IfcProject|Pset_项目控制指标信息属性集|绿地率",
  "绿地率",
  PlanningTargetUnit.Percent,
  PlanningTargetOperator.GreaterOrEqual,
  "35")
```

`PlanningTargetRequirementPolicy.GetRequirement` 的精确规则：

```csharp
if (modelFileType == "总平模型") return PlanningTargetRequirement.Required;
if (modelFileType == "单体建筑—地上" || modelFileType == "单体建筑—地下")
  return PlanningTargetRequirement.Inherited;
return PlanningTargetRequirement.NotApplicable;
```

- [ ] **Step 4: 运行目标值与必填策略测试**

Run:

```bash
dotnet test tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj -c Release --filter "PlanningTargetValueTests|PlanningTargetRequirementPolicyTests"
```

Expected: PASS。

- [ ] **Step 5: 提交**

```bash
git add src/BIMBaoGui.Stage01/Core/PlanningTarget*.cs tests/BIMBaoGui.Stage01.Core.Tests/PlanningTarget*.cs tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj
git commit -m "feat: add structured planning target domain"
```

---

### Task 2: 将结构化规划目标纳入 Stage 01 模型、载荷和恢复

**Files:**
- Modify: `src/BIMBaoGui.Stage01/Core/Stage01Model.cs`
- Modify: `src/BIMBaoGui.Stage01/Core/CanonicalPayload.cs`
- Modify: `src/BIMBaoGui.Stage01/Core/Stage01PayloadCodec.cs`
- Modify: `tests/BIMBaoGui.Stage01.Core.Tests/Stage01ModelTests.cs`
- Modify: `tests/BIMBaoGui.Stage01.Core.Tests/CanonicalPayloadTests.cs`
- Create: `tests/BIMBaoGui.Stage01.Core.Tests/Stage01PayloadCodecTests.cs`

**Interfaces:**
- Consumes: `PlanningTargetValue` 和 `PlanningTargetCatalog.All`。
- Produces: `Stage01Model.PlanningTargets`、`SetPlanningTarget`、`TryGetPlanningTarget`，以及能往返恢复规划目标的 canonical payload。

- [ ] **Step 1: 写克隆、确定性载荷和往返恢复失败测试**

```csharp
[Fact]
public void Clone_CopiesPlanningTargetsIndependently()
{
  Stage01Model original = new Stage01Model();
  Assert.True(PlanningTargetValue.TryCreate(
    "planning.green_rate",
    PlanningTargetOperator.GreaterOrEqual,
    "35",
    null,
    PlanningTargetUnit.Percent,
    "项目初始化",
    out PlanningTargetValue target,
    out _));
  original.SetPlanningTarget(target);

  Stage01Model clone = original.Clone();
  clone.RemovePlanningTarget("planning.green_rate");

  Assert.True(original.TryGetPlanningTarget("planning.green_rate", out _));
  Assert.False(clone.TryGetPlanningTarget("planning.green_rate", out _));
}

[Fact]
public void CanonicalPayload_IncludesPlanningTargetsInStableMetricOrder()
{
  Stage01Model first = BuildModelWithTargets(reverse: false);
  Stage01Model second = BuildModelWithTargets(reverse: true);

  Assert.Equal(CanonicalPayload.Build(first), CanonicalPayload.Build(second));
  Assert.Equal(CanonicalPayload.Sha256(CanonicalPayload.Build(first)), CanonicalPayload.Sha256(CanonicalPayload.Build(second)));
}

[Fact]
public void PayloadCodec_RoundTripsStructuredTargets()
{
  Stage01Model source = BuildModelWithTargets(reverse: false);
  string json = CanonicalPayload.Build(source);
  Stage01Model restored = new Stage01Model();

  Assert.True(Stage01PayloadCodec.TryApply(json, restored, out string error), error);
  Assert.True(restored.TryGetPlanningTarget("planning.floor_area_ratio", out PlanningTargetValue value));
  Assert.Equal("≤2", value.ToMvdText());
}
```

- [ ] **Step 2: 运行测试并确认失败**

Run:

```bash
dotnet test tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj -c Release --filter "Stage01ModelTests|CanonicalPayloadTests|Stage01PayloadCodecTests"
```

Expected: 编译失败，提示 Stage01Model 不存在规划目标 API。

- [ ] **Step 3: 扩展 Stage01Model**

在构造函数中增加：

```csharp
PlanningTargets = new Dictionary<string, PlanningTargetValue>(StringComparer.Ordinal);
```

增加公开只读入口和方法：

```csharp
public Dictionary<string, PlanningTargetValue> PlanningTargets { get; }

public void SetPlanningTarget(PlanningTargetValue target)
{
  if (target == null) throw new ArgumentNullException(nameof(target));
  PlanningTargets[target.MetricCode] = target;
  PlanningTargetDefinition definition = PlanningTargetCatalog.Get(target.MetricCode);
  if (definition != null) SetValue(definition.MvdFieldKey, target.ToMvdText());
}

public bool TryGetPlanningTarget(string metricCode, out PlanningTargetValue target) =>
  PlanningTargets.TryGetValue(metricCode ?? string.Empty, out target);

public void RemovePlanningTarget(string metricCode)
{
  PlanningTargets.Remove(metricCode ?? string.Empty);
  PlanningTargetDefinition definition = PlanningTargetCatalog.Get(metricCode);
  if (definition != null) SetValue(definition.MvdFieldKey, string.Empty);
}
```

`Clone()` 必须逐项复制 `PlanningTargetValue`；该类型不可变，可安全共享实例。

- [ ] **Step 4: 扩展 canonical payload 和 codec**

`CanonicalPayload.Build` 中新增稳定结构：

```csharp
planningTargets = model.PlanningTargets
  .OrderBy(pair => pair.Key, StringComparer.Ordinal)
  .ToDictionary(
    pair => pair.Key,
    pair => new
    {
      metricCode = pair.Value.MetricCode,
      @operator = pair.Value.Operator.ToString(),
      value1 = pair.Value.Value1.ToString(CultureInfo.InvariantCulture),
      value2 = pair.Value.Value2?.ToString(CultureInfo.InvariantCulture),
      unit = pair.Value.Unit.ToString(),
      source = pair.Value.Source,
      displayText = pair.Value.ToMvdText()
    },
    StringComparer.Ordinal)
```

`Stage01PayloadCodec.TryApply` 应优先读取 `planningTargets`；旧载荷没有该节点时，按 `PlanningTargetCatalog` 的 MVD 字段文本尝试兼容解析。无法解析的旧文本保留在 `Values`，但不得生成伪结构化值。

- [ ] **Step 5: 运行测试**

Run:

```bash
dotnet test tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj -c Release --filter "Stage01ModelTests|CanonicalPayloadTests|Stage01PayloadCodecTests"
```

Expected: PASS。

- [ ] **Step 6: 提交**

```bash
git add src/BIMBaoGui.Stage01/Core/Stage01Model.cs src/BIMBaoGui.Stage01/Core/CanonicalPayload.cs src/BIMBaoGui.Stage01/Core/Stage01PayloadCodec.cs tests/BIMBaoGui.Stage01.Core.Tests
git commit -m "feat: persist structured planning targets"
```

---

### Task 3: 在 Stage 01 中实现动态必填和结构化目标编辑器

**Files:**
- Create: `src/BIMBaoGui.Stage01/UI/PlanningTargetEditor.cs`
- Modify: `src/BIMBaoGui.Stage01/Core/Stage01Validation.cs`
- Modify: `src/BIMBaoGui.Stage01/Core/FieldInputRules.cs`
- Modify: `src/BIMBaoGui.Stage01/Infrastructure/Stage01RegistryProvider.cs`
- Modify: `src/BIMBaoGui.Stage01/Stage01Component.cs`
- Modify: `src/BIMBaoGui.Stage01/UI/Stage01ComponentAttributes.cs`
- Modify: `src/BIMBaoGui.Stage01/UI/InlineEditor.cs`
- Modify: `tests/BIMBaoGui.Stage01.Core.Tests/Stage01ValidationTests.cs`
- Modify: `tests/test_plugin_contract.py`

**Interfaces:**
- Consumes: `PlanningTargetCatalog`、`PlanningTargetRequirementPolicy`、`Stage01Model.SetPlanningTarget`。
- Produces: `Stage01Component.SetPlanningTarget(...)`、`Stage01Component.GetPlanningTargetRequirement(...)` 和 Stage 01 规划控制指标专用 UI。

- [ ] **Step 1: 写总平必填、非总平继承失败测试**

```csharp
[Theory]
[InlineData("planning.building_density")]
[InlineData("planning.floor_area_ratio")]
[InlineData("planning.green_rate")]
public void Validate_SiteModelRequiresCorePlanningTarget(string metricCode)
{
  Stage01Model model = ValidModel();
  model.SetValue(Stage01Keys.ModelFileType, "总平模型");

  ValidationResult result = Stage01Validator.Validate(model, new List<FieldDefinition>());

  Assert.Contains(result.Messages, message =>
    message.Severity == ValidationSeverity.Error &&
    message.FieldKey == PlanningTargetCatalog.Get(metricCode).MvdFieldKey);
}

[Fact]
public void Validate_AboveGroundModelDoesNotRequireRepeatedManualTargets()
{
  Stage01Model model = ValidModel();
  model.SetValue(Stage01Keys.ModelFileType, "单体建筑—地上");

  ValidationResult result = Stage01Validator.Validate(model, new List<FieldDefinition>());

  Assert.DoesNotContain(result.Messages, message =>
    PlanningTargetCatalog.All.Any(definition => definition.MvdFieldKey == message.FieldKey));
}
```

- [ ] **Step 2: 运行测试并确认失败**

Run:

```bash
dotnet test tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj -c Release --filter Stage01ValidationTests
```

Expected: 总平缺失三个目标时没有错误，测试失败。

- [ ] **Step 3: 实现动态校验**

在 `Stage01Validator.Validate` 中加入：

```csharp
foreach (PlanningTargetDefinition definition in PlanningTargetCatalog.All)
{
  PlanningTargetRequirement requirement = PlanningTargetRequirementPolicy.GetRequirement(
    model.GetValue(Stage01Keys.ModelFileType),
    definition.MetricCode,
    model.Conditions);

  bool hasValue = model.TryGetPlanningTarget(definition.MetricCode, out PlanningTargetValue target);
  if (requirement == PlanningTargetRequirement.Required && !hasValue)
  {
    messages.Add(new ValidationMessage(
      ValidationSeverity.Error,
      definition.MvdFieldKey,
      definition.Label + "为总平模型必填规划目标。"));
  }
  else if (hasValue)
  {
    string error = PlanningTargetInputRules.Validate(target);
    if (!string.IsNullOrWhiteSpace(error))
      messages.Add(new ValidationMessage(ValidationSeverity.Error, definition.MvdFieldKey, error));
  }
}
```

从普通 MVD 字段循环中排除 `PlanningTargetCatalog` 管理的三个字段，避免同一问题重复报错。

- [ ] **Step 4: 实现结构化目标编辑 API**

`Stage01Component` 增加：

```csharp
internal PlanningTargetRequirement GetPlanningTargetRequirement(string metricCode) =>
  PlanningTargetRequirementPolicy.GetRequirement(
    _model.GetValue(Stage01Keys.ModelFileType),
    metricCode,
    _model.Conditions);

internal bool TryGetPlanningTarget(string metricCode, out PlanningTargetValue target) =>
  _model.TryGetPlanningTarget(metricCode, out target);

internal void SetPlanningTarget(PlanningTargetValue target)
{
  _model.SetPlanningTarget(target);
  NotifyModelEdited();
}
```

- [ ] **Step 5: 在规划控制指标目录绘制结构化输入行**

`Stage01ComponentAttributes` 中，`08_规划控制指标` 必须先调用：

```csharp
DrawPlanningTargets(graphics, viewport, PlanningTargetCatalog.All);
```

每行固定包含：

```text
标签 + 必填/继承状态 | 运算符 | 数值1 | 数值2（仅区间） | 单位
```

点击运算符显示 `≤ / ≥ / = / 区间` 下拉；点击数值调用带即时校验的 `PlanningTargetEditor`。单体地上、单体地下显示灰色“继承”，禁止手工修改。不得再把三个字段绘制为普通文本 MVD 行。

`PlanningTargetEditor.Show` 的提交回调必须使用：

```csharp
if (!PlanningTargetValue.TryCreate(metricCode, op, value1, value2, unit, "项目初始化", out PlanningTargetValue target, out string error))
{
  errorLabel.Text = error;
  errorLabel.Visible = true;
  return;
}
accepted(target);
form.Close();
```

格式错误时编辑器保持打开，不能静默接受错误文本。

- [ ] **Step 6: 增加 UI 合同测试**

`tests/test_plugin_contract.py` 增加精确断言：

```python
def test_stage01_draws_structured_planning_targets():
    attributes = read("src/BIMBaoGui.Stage01/UI/Stage01ComponentAttributes.cs")
    editor = read("src/BIMBaoGui.Stage01/UI/PlanningTargetEditor.cs")
    assert "DrawPlanningTargets" in attributes
    assert "PlanningTargetCatalog.All" in attributes
    assert "PlanningTargetValue.TryCreate" in editor
    assert "区间" in editor
    assert "继承" in attributes
```

- [ ] **Step 7: 运行核心测试与合同测试**

Run:

```bash
dotnet test tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj -c Release
python -m pytest tests/test_plugin_contract.py -q
```

Expected: 全部 PASS。

- [ ] **Step 8: 提交**

```bash
git add src/BIMBaoGui.Stage01/Core src/BIMBaoGui.Stage01/Infrastructure src/BIMBaoGui.Stage01/Stage01Component.cs src/BIMBaoGui.Stage01/UI tests
git commit -m "feat: require and edit site planning targets"
```

---

### Task 4: 建立不可变 `HBR_FileContext` 与确定性哈希

**Files:**
- Create: `src/BIMBaoGui.Stage01/Context/HBRSpatialReference.cs`
- Create: `src/BIMBaoGui.Stage01/Context/HBRFileContext.cs`
- Create: `src/BIMBaoGui.Stage01/Context/HBRFileContextCanonicalizer.cs`
- Create: `src/BIMBaoGui.Stage01/Context/HBRFileContextFactory.cs`
- Create: `src/BIMBaoGui.Stage01/Context/RuleActivationCatalog.cs`
- Modify: `src/BIMBaoGui.Stage01/Revit/RevitModels.cs`
- Modify: `src/BIMBaoGui.Stage01/Revit/Stage01RevitService.cs`
- Create: `tests/BIMBaoGui.Stage01.Core.Tests/HBRFileContextTests.cs`
- Modify: `tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj`

**Interfaces:**
- Consumes: Stage 01 模型、规划目标、项目条件、Revit 文件快照。
- Produces: `HBRFileContextFactory.Create(...)`、`HBRFileContextCanonicalizer.Serialize(...)`、`ValidateHash(...)`。

- [ ] **Step 1: 写确定性上下文和哈希失败测试**

```csharp
[Fact]
public void Factory_CreatesStableHashRegardlessOfConditionInsertionOrder()
{
  Stage01Model first = BuildValidSiteModel(reverseConditions: false);
  Stage01Model second = BuildValidSiteModel(reverseConditions: true);
  RevitDocumentSnapshot snapshot = BuildSnapshot();

  HBRFileContext a = HBRFileContextFactory.Create(first, snapshot, initializationPassed: true);
  HBRFileContext b = HBRFileContextFactory.Create(second, snapshot, initializationPassed: true);

  Assert.Equal(a.FileContextHash, b.FileContextHash);
  Assert.True(HBRFileContextCanonicalizer.ValidateHash(a));
}

[Fact]
public void Context_ContainsModelTypeConditionsTargetsAndRuleActivation()
{
  Stage01Model model = BuildValidSiteModel(reverseConditions: false);
  model.SetCondition("site.green", true);

  HBRFileContext context = HBRFileContextFactory.Create(model, BuildSnapshot(), true);

  Assert.Equal("总平模型", context.ModelFileType);
  Assert.True(context.ProjectConditions["site.green"]);
  Assert.Contains("SITE.GREEN", context.ActivatedRuleIds);
  Assert.Contains("SITE.PARKING.OUTDOOR", context.NotApplicableRuleIds);
  Assert.Equal("≥35%", context.PlanningTargets["planning.green_rate"].ToMvdText());
}
```

- [ ] **Step 2: 运行测试并确认失败**

Run:

```bash
dotnet test tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj -c Release --filter HBRFileContextTests
```

Expected: 编译失败，文件上下文类型不存在。

- [ ] **Step 3: 实现不可变上下文对象**

`HBRFileContext` 构造函数必须复制集合，不暴露可变字典：

```csharp
public sealed class HBRFileContext
{
  public string SchemaVersion { get; }
  public string WorkflowVersion { get; }
  public string FileGuid { get; }
  public string RevitDocumentFingerprint { get; }
  public string RevitDocumentTitle { get; }
  public string ProjectNumber { get; }
  public string ProjectName { get; }
  public string SubitemCode { get; }
  public string SubitemName { get; }
  public string ModelFileType { get; }
  public string ModelScope { get; }
  public HBRSpatialReference SpatialReference { get; }
  public IReadOnlyDictionary<string, PlanningTargetValue> PlanningTargets { get; }
  public IReadOnlyDictionary<string, bool> ProjectConditions { get; }
  public IReadOnlyList<string> ActivatedRuleIds { get; }
  public IReadOnlyList<string> NotApplicableRuleIds { get; }
  public bool InitializationPassed { get; }
  public string RulePackVersion { get; }
  public string FileContextHash { get; }
}
```

固定版本：

```csharp
SchemaVersion = "0.1.0";
RulePackVersion = "hbr-planning-0.1.0";
```

- [ ] **Step 4: 实现文件指纹和规则激活**

`RevitDocumentSnapshot` 增加：

```csharp
public string FileGuid { get; set; }
public string DocumentFingerprint { get; set; }
```

`Stage01RevitService` 使用已存 `FileGuid` 和标准化路径生成指纹：

```csharp
string normalizedPath = (document.PathName ?? string.Empty).Trim().ToLowerInvariant();
snapshot.DocumentFingerprint = CanonicalPayload.Sha256(snapshot.FileGuid + "|" + normalizedPath);
```

`RuleActivationCatalog` 必须至少提供：

- 总平默认：`SITE.SKELETON.SPATIAL`、`SITE.LAND.TOTAL`、`SITE.LAND.NET`、`SITE.BUILDING.FOOTPRINT`。
- 条件映射：`site.green → SITE.GREEN`、`site.outdoor_parking → SITE.PARKING.OUTDOOR`、`site.civil_defense → SITE.CIVIL_DEFENSE` 等现有十项条件。
- 地上默认：`ABOVE.SKELETON.LEVELS`、`ABOVE.BUILDING.BODY`、`ABOVE.TARGET.INHERITANCE`。
- 地下默认：`UNDERGROUND.SKELETON.LEVELS`、`UNDERGROUND.BUILDING.BODY`、`UNDERGROUND.TARGET.INHERITANCE`。

- [ ] **Step 5: 实现 canonicalizer**

`HBRFileContextCanonicalizer.SerializeWithoutHash` 必须对字典和列表排序；`FileContextHash` 不进入被哈希载荷：

```csharp
public static string ComputeHash(HBRFileContext context) =>
  CanonicalPayload.Sha256(SerializeWithoutHash(context));

public static bool ValidateHash(HBRFileContext context) =>
  context != null && string.Equals(
    context.FileContextHash,
    ComputeHash(context),
    StringComparison.OrdinalIgnoreCase);
```

- [ ] **Step 6: 运行测试**

Run:

```bash
dotnet test tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj -c Release --filter HBRFileContextTests
```

Expected: PASS。

- [ ] **Step 7: 提交**

```bash
git add src/BIMBaoGui.Stage01/Context src/BIMBaoGui.Stage01/Revit/RevitModels.cs src/BIMBaoGui.Stage01/Revit/Stage01RevitService.cs tests/BIMBaoGui.Stage01.Core.Tests
git commit -m "feat: add deterministic file context"
```

---

### Task 5: 增加 `HBR_FileContext` Grasshopper 类型并修改 Stage 01 输出

**Files:**
- Create: `src/BIMBaoGui.Stage01/GrasshopperTypes/HBRFileContextGoo.cs`
- Create: `src/BIMBaoGui.Stage01/GrasshopperTypes/HBRFileContextParam.cs`
- Modify: `src/BIMBaoGui.Stage01/Stage01Component.cs`
- Modify: `tests/test_plugin_contract.py`

**Interfaces:**
- Consumes: `HBRFileContext` 和 canonicalizer。
- Produces: Grasshopper 类型 `HBRFileContextGoo`、参数 `HBRFileContextParam`；Stage 01 第一个输出改为强类型文件上下文。

- [ ] **Step 1: 写 Grasshopper 类型和输出合同失败测试**

```python
def test_stage01_outputs_typed_file_context_first():
    component = read("src/BIMBaoGui.Stage01/Stage01Component.cs")
    goo = read("src/BIMBaoGui.Stage01/GrasshopperTypes/HBRFileContextGoo.cs")
    param = read("src/BIMBaoGui.Stage01/GrasshopperTypes/HBRFileContextParam.cs")
    assert "HBRFileContextFactory.Create" in component
    assert "new HBRFileContextParam()" in component
    assert "GH_Goo<HBRFileContext>" in goo
    assert "GH_PersistentParam<HBRFileContextGoo>" in param
    assert component.index('"文件上下文"') < component.index('"初始化通过"')
```

- [ ] **Step 2: 运行测试并确认失败**

Run:

```bash
python -m pytest tests/test_plugin_contract.py::test_stage01_outputs_typed_file_context_first -q
```

Expected: FAIL，类型文件尚不存在。

- [ ] **Step 3: 实现 Goo 和参数**

`HBRFileContextGoo`：

```csharp
public sealed class HBRFileContextGoo : GH_Goo<HBRFileContext>
{
  public HBRFileContextGoo() { }
  public HBRFileContextGoo(HBRFileContext value) { Value = value; }

  public override bool IsValid => Value != null && HBRFileContextCanonicalizer.ValidateHash(Value);
  public override string TypeName => "HBR_FileContext";
  public override string TypeDescription => "湖北 BIM 报规单文件上下文";
  public override IGH_Goo Duplicate() => new HBRFileContextGoo(Value);
  public override string ToString() => Value == null
    ? "空 HBR_FileContext"
    : Value.ModelFileType + " · " + Value.ProjectName + " · " + Value.FileContextHash.Substring(0, 8);

  public override bool Write(GH_IWriter writer)
  {
    writer.SetString("HBR.FileContext.Json", HBRFileContextCanonicalizer.Serialize(Value));
    return true;
  }

  public override bool Read(GH_IReader reader)
  {
    if (!reader.ItemExists("HBR.FileContext.Json")) return false;
    return HBRFileContextCanonicalizer.TryDeserialize(
      reader.GetString("HBR.FileContext.Json"),
      out HBRFileContext value,
      out _) && (Value = value) != null;
  }
}
```

`HBRFileContextParam` 的 `Prompt_Singular` 和 `Prompt_Plural` 都返回 `GH_GetterResult.cancel`；该参数只能由组件连线提供，不允许手工伪造。

- [ ] **Step 4: 修改 Stage 01 输出顺序和求解**

输出精确顺序：

```csharp
pManager.AddParameter(new HBRFileContextParam(), "文件上下文", "C", "Stage 02 的强类型输入。", GH_ParamAccess.item);
pManager.AddBooleanParameter("初始化通过", "OK", "写入与回读均通过时为 True。", GH_ParamAccess.item);
pManager.AddTextParameter("状态", "S", "当前文件初始化状态。", GH_ParamAccess.item);
pManager.AddTextParameter("消息", "M", "阻断、校验、写入和回读消息。", GH_ParamAccess.list);
pManager.AddTextParameter("上下文JSON", "J", "仅供调试和外部检查。", GH_ParamAccess.item);
```

`SolveInstance`：

```csharp
bool initialized = _snapshot.IsInitialized && _snapshot.PayloadMatches && !_isCommitting;
HBRFileContext context = HBRFileContextFactory.Create(_model, _snapshot, initialized);

dataAccess.SetData(0, new HBRFileContextGoo(context));
dataAccess.SetData(1, initialized);
dataAccess.SetData(2, ResolveStatus());
dataAccess.SetDataList(3, ResolveMessages());
dataAccess.SetData(4, HBRFileContextCanonicalizer.Serialize(context));
```

- [ ] **Step 5: 运行合同测试和 Release 编译**

Run:

```bash
python -m pytest tests/test_plugin_contract.py -q
dotnet build src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj -c Release
```

Expected: PASS；生成 `BIMBaoGui.Stage01.gha`。

- [ ] **Step 6: 提交**

```bash
git add src/BIMBaoGui.Stage01/GrasshopperTypes src/BIMBaoGui.Stage01/Stage01Component.cs tests/test_plugin_contract.py
git commit -m "feat: output typed file context from Stage 01"
```

---

### Task 6: 建立 `HBR_TaskPlan` 和纯业务任务编译器

**Files:**
- Create: `src/BIMBaoGui.Stage01/TaskPlanning/HBRTaskPlan.cs`
- Create: `src/BIMBaoGui.Stage01/TaskPlanning/TaskRuleDefinition.cs`
- Create: `src/BIMBaoGui.Stage01/TaskPlanning/TaskRuleCatalog.cs`
- Create: `src/BIMBaoGui.Stage01/TaskPlanning/TaskPlanCompiler.cs`
- Create: `src/BIMBaoGui.Stage01/TaskPlanning/HBRTaskPlanCanonicalizer.cs`
- Create: `tests/BIMBaoGui.Stage01.Core.Tests/TaskPlanCompilerTests.cs`
- Create: `tests/BIMBaoGui.Stage01.Core.Tests/HBRTaskPlanTests.cs`
- Modify: `tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj`

**Interfaces:**
- Consumes: 有效的 `HBRFileContext`。
- Produces: `TaskPlanCompiler.Compile(HBRFileContext)`、`HBRTaskPlan`、`HBRTaskPlanCanonicalizer.ValidateHash(...)`。

- [ ] **Step 1: 写三类模型和条件分流失败测试**

```csharp
[Fact]
public void Compile_SiteContextAddsBaseAndSelectedConditionalTasks()
{
  HBRFileContext context = BuildContext(
    "总平模型",
    new Dictionary<string, bool>
    {
      ["site.green"] = true,
      ["site.outdoor_parking"] = false
    });

  HBRTaskPlan plan = TaskPlanCompiler.Compile(context);

  Assert.Contains(plan.RequiredObjects, x => x.RuleId == "SITE.LAND.TOTAL");
  Assert.Contains(plan.ConditionalObjects, x => x.RuleId == "SITE.GREEN");
  Assert.Contains(plan.NotApplicableObjects, x => x.RuleId == "SITE.PARKING.OUTDOOR");
  Assert.Equal("总平", plan.SkeletonPath);
}

[Theory]
[InlineData("单体建筑—地上", "地上", "ABOVE.BUILDING.BODY")]
[InlineData("单体建筑—地下", "地下", "UNDERGROUND.BUILDING.BODY")]
public void Compile_BuildingContextUsesOnlyMatchingRoute(string modelType, string route, string requiredRule)
{
  HBRTaskPlan plan = TaskPlanCompiler.Compile(BuildContext(modelType, new Dictionary<string, bool>()));

  Assert.Equal(route, plan.SkeletonPath);
  Assert.Contains(plan.RequiredObjects, task => task.RuleId == requiredRule);
  Assert.DoesNotContain(plan.RequiredObjects, task => task.RuleId.StartsWith("SITE.", StringComparison.Ordinal));
}

[Fact]
public void Compile_ChangingContextHashChangesTaskPlanHash()
{
  HBRTaskPlan first = TaskPlanCompiler.Compile(BuildContext("总平模型", new Dictionary<string, bool> { ["site.green"] = false }));
  HBRTaskPlan second = TaskPlanCompiler.Compile(BuildContext("总平模型", new Dictionary<string, bool> { ["site.green"] = true }));

  Assert.NotEqual(first.FileContextHash, second.FileContextHash);
  Assert.NotEqual(first.TaskPlanHash, second.TaskPlanHash);
}
```

- [ ] **Step 2: 运行测试并确认失败**

Run:

```bash
dotnet test tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj -c Release --filter "TaskPlanCompilerTests|HBRTaskPlanTests"
```

Expected: 编译失败，任务计划类型不存在。

- [ ] **Step 3: 实现任务规则类型**

`TaskRuleDefinition` 必须包含：

```csharp
public string RuleId { get; }
public string ModelFileType { get; }
public string ConditionKey { get; }
public string ObjectCode { get; }
public bool IsSkeletonTask { get; }
public IReadOnlyList<string> RequiredAttributes { get; }
public IReadOnlyList<string> Dependencies { get; }
public IReadOnlyList<string> GeometryChecks { get; }
public IReadOnlyList<string> PropertyChecks { get; }
public IReadOnlyList<string> TargetComparisons { get; }
```

`TaskRuleCatalog.All` 至少定义以下规则：

```text
SITE.SKELETON.SPATIAL
SITE.LAND.TOTAL
SITE.LAND.NET
SITE.BUILDING.FOOTPRINT
SITE.LAND.OTHER
SITE.ROAD.REDLINE
SITE.ROAD.CENTERLINE
SITE.ROAD.INTERNAL
SITE.FIRE.LANE
SITE.FIRE.FIELD
SITE.GREEN
SITE.PARKING.OUTDOOR
SITE.CIVIL_DEFENSE
SITE.STRUCTURES
ABOVE.SKELETON.LEVELS
ABOVE.BUILDING.BODY
ABOVE.TARGET.INHERITANCE
UNDERGROUND.SKELETON.LEVELS
UNDERGROUND.BUILDING.BODY
UNDERGROUND.TARGET.INHERITANCE
```

总平四项基础规则 `ConditionKey = null`；其余总平条件规则使用 Stage 01 已有的条件键。地上和地下继承规则的 `TargetComparisons` 固定包含 `planning.building_density`、`planning.floor_area_ratio`、`planning.green_rate`。

- [ ] **Step 4: 实现编译器**

```csharp
public static HBRTaskPlan Compile(HBRFileContext context)
{
  if (context == null) throw new ArgumentNullException(nameof(context));
  if (!HBRFileContextCanonicalizer.ValidateHash(context))
    throw new InvalidOperationException("文件上下文哈希无效。");

  IReadOnlyList<TaskRuleDefinition> candidates = TaskRuleCatalog.All
    .Where(rule => string.Equals(rule.ModelFileType, context.ModelFileType, StringComparison.Ordinal))
    .ToList();

  var required = candidates.Where(rule => string.IsNullOrEmpty(rule.ConditionKey)).ToList();
  var conditional = candidates.Where(rule =>
    !string.IsNullOrEmpty(rule.ConditionKey) &&
    context.ProjectConditions.TryGetValue(rule.ConditionKey, out bool enabled) && enabled).ToList();
  var notApplicable = candidates.Where(rule =>
    !string.IsNullOrEmpty(rule.ConditionKey) &&
    (!context.ProjectConditions.TryGetValue(rule.ConditionKey, out bool enabled) || !enabled)).ToList();

  return HBRTaskPlan.Create(
    context.FileContextHash,
    context.ModelFileType,
    ResolveSkeletonPath(context.ModelFileType),
    required,
    conditional,
    notApplicable);
}
```

`HBRTaskPlan.Create` 汇总并去重 `AttributeRequirements`、`BuildSequence`、`Dependencies`、`GeometryChecks`、`PropertyChecks`、`TargetComparisons`、`SkeletonTasks`，然后计算 `TaskPlanHash`。

- [ ] **Step 5: 运行测试**

Run:

```bash
dotnet test tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj -c Release --filter "TaskPlanCompilerTests|HBRTaskPlanTests"
```

Expected: PASS。

- [ ] **Step 6: 提交**

```bash
git add src/BIMBaoGui.Stage01/TaskPlanning tests/BIMBaoGui.Stage01.Core.Tests
git commit -m "feat: compile model-specific task plans"
```

---

### Task 7: 增加 `HBR_TaskPlan` Grasshopper 类型和 Stage 02 组件

**Files:**
- Create: `src/BIMBaoGui.Stage01/GrasshopperTypes/HBRTaskPlanGoo.cs`
- Create: `src/BIMBaoGui.Stage01/GrasshopperTypes/HBRTaskPlanParam.cs`
- Create: `src/BIMBaoGui.Stage01/Revit/Stage02RevitContextService.cs`
- Create: `src/BIMBaoGui.Stage01/Stage02TaskPlanComponent.cs`
- Create: `src/BIMBaoGui.Stage01/UI/Stage02ComponentAttributes.cs`
- Modify: `tests/test_plugin_contract.py`

**Interfaces:**
- Consumes: 连接输入 `HBRFileContextGoo`。
- Produces: `HBRTaskPlanGoo`、骨架路径、激活任务摘要、阻断信息。

- [ ] **Step 1: 写 Stage 02 组件合同失败测试**

```python
def test_stage02_requires_typed_context_and_outputs_typed_task_plan():
    component = read("src/BIMBaoGui.Stage01/Stage02TaskPlanComponent.cs")
    task_goo = read("src/BIMBaoGui.Stage01/GrasshopperTypes/HBRTaskPlanGoo.cs")
    task_param = read("src/BIMBaoGui.Stage01/GrasshopperTypes/HBRTaskPlanParam.cs")
    assert "new HBRFileContextParam()" in component
    assert "new HBRTaskPlanParam()" in component
    assert "TaskPlanCompiler.Compile" in component
    assert "请连接 01 文件初始化的“文件上下文”输出" in component
    assert "GH_Goo<HBRTaskPlan>" in task_goo
    assert "GH_PersistentParam<HBRTaskPlanGoo>" in task_param
```

- [ ] **Step 2: 运行测试并确认失败**

Run:

```bash
python -m pytest tests/test_plugin_contract.py::test_stage02_requires_typed_context_and_outputs_typed_task_plan -q
```

Expected: FAIL，Stage 02 文件不存在。

- [ ] **Step 3: 实现当前 Revit 身份核验服务**

`Stage02RevitContextService.ReadCurrentIdentity` 只能读取身份，不读取业务配置：

```csharp
internal sealed class ActiveRevitFileIdentity
{
  public string FileGuid { get; set; }
  public string DocumentFingerprint { get; set; }
  public string DocumentTitle { get; set; }
}
```

读取流程：

1. `RevitHost.RunReadInHostContext`。
2. `Stage01Storage.Read(document)` 取得已存 `FileGuid`。
3. 使用与 Stage 01 完全相同的指纹算法计算 `DocumentFingerprint`。
4. 不读取规划指标、条件或模型类型。

- [ ] **Step 4: 实现 Stage 02 输入阻断**

`SolveInstance` 必须按顺序检查：

```csharp
HBRFileContextGoo contextGoo = null;
if (!dataAccess.GetData(0, ref contextGoo) || contextGoo?.Value == null)
  return Fail(dataAccess, "请连接 01 文件初始化的“文件上下文”输出。");

HBRFileContext context = contextGoo.Value;
if (!context.InitializationPassed)
  return Fail(dataAccess, "文件初始化尚未通过，请先回到 01 文件初始化完成写入与回读。");
if (!HBRFileContextCanonicalizer.ValidateHash(context))
  return Fail(dataAccess, "文件上下文哈希无效，请重新运行 01 文件初始化。");
if (!string.Equals(context.SchemaVersion, "0.1.0", StringComparison.Ordinal))
  return Fail(dataAccess, "文件上下文版本不兼容：" + context.SchemaVersion);
if (!string.Equals(context.RulePackVersion, "hbr-planning-0.1.0", StringComparison.Ordinal))
  return Fail(dataAccess, "规则包版本不兼容：" + context.RulePackVersion);

ActiveRevitFileIdentity active = Stage02RevitContextService.ReadCurrentIdentity();
if (!string.Equals(active.DocumentFingerprint, context.RevitDocumentFingerprint, StringComparison.OrdinalIgnoreCase))
  return Fail(dataAccess,
    "文件不匹配：上下文来自“" + context.RevitDocumentTitle + "”，当前为“" + active.DocumentTitle + "”。");
```

- [ ] **Step 5: 实现组件端口和输出**

输入：

```csharp
pManager.AddParameter(new HBRFileContextParam(), "文件上下文", "C", "连接 Stage 01 的文件上下文输出。", GH_ParamAccess.item);
```

输出：

```csharp
pManager.AddParameter(new HBRTaskPlanParam(), "任务计划", "T", "当前文件的强类型任务计划。", GH_ParamAccess.item);
pManager.AddTextParameter("骨架路径", "R", "总平 / 地上 / 地下。", GH_ParamAccess.item);
pManager.AddTextParameter("激活任务", "A", "必建和已激活条件任务摘要。", GH_ParamAccess.list);
pManager.AddTextParameter("阻断信息", "B", "输入或文档核验阻断。", GH_ParamAccess.list);
```

成功时：

```csharp
HBRTaskPlan plan = TaskPlanCompiler.Compile(context);
dataAccess.SetData(0, new HBRTaskPlanGoo(plan));
dataAccess.SetData(1, plan.SkeletonPath);
dataAccess.SetDataList(2, plan.RequiredObjects.Concat(plan.ConditionalObjects).Select(task => task.RuleId + "｜" + task.ObjectCode));
dataAccess.SetDataList(3, Array.Empty<string>());
```

- [ ] **Step 6: 实现 Stage 02 卡片摘要**

`Stage02ComponentAttributes` 显示：

```text
模型类型
当前 Revit 文件
上下文哈希前 8 位
骨架路径
必建任务数量
条件任务数量
不适用任务数量
阻断原因（若有）
```

Stage 02 仍保留标准输入/输出端口，卡片不得隐藏 Grasshopper 连线。

- [ ] **Step 7: 运行合同测试和 Release 编译**

Run:

```bash
python -m pytest tests/test_plugin_contract.py -q
dotnet build src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj -c Release
```

Expected: PASS。

- [ ] **Step 8: 提交**

```bash
git add src/BIMBaoGui.Stage01/GrasshopperTypes src/BIMBaoGui.Stage01/Revit/Stage02RevitContextService.cs src/BIMBaoGui.Stage01/Stage02TaskPlanComponent.cs src/BIMBaoGui.Stage01/UI/Stage02ComponentAttributes.cs tests/test_plugin_contract.py
git commit -m "feat: add Stage 02 task routing component"
```

---

### Task 8: 实现上游变化、任务重编译和待复检语义

**Files:**
- Modify: `src/BIMBaoGui.Stage01/Stage02TaskPlanComponent.cs`
- Modify: `src/BIMBaoGui.Stage01/TaskPlanning/HBRTaskPlan.cs`
- Modify: `src/BIMBaoGui.Stage01/UI/Stage02ComponentAttributes.cs`
- Modify: `tests/BIMBaoGui.Stage01.Core.Tests/HBRTaskPlanTests.cs`
- Modify: `tests/test_plugin_contract.py`

**Interfaces:**
- Consumes: `FileContextHash`。
- Produces: `SourceFileContextHash`、`TaskPlanHash`、`RequiresDownstreamReview` 状态。

- [ ] **Step 1: 写上下文变化导致计划变化失败测试**

```csharp
[Fact]
public void TaskPlan_RecordsItsSourceContextHash()
{
  HBRFileContext context = BuildContext("总平模型", new Dictionary<string, bool> { ["site.green"] = true });
  HBRTaskPlan plan = TaskPlanCompiler.Compile(context);

  Assert.Equal(context.FileContextHash, plan.FileContextHash);
  Assert.True(HBRTaskPlanCanonicalizer.ValidateHash(plan));
}
```

合同测试：

```python
def test_stage02_tracks_context_hash_changes():
    component = read("src/BIMBaoGui.Stage01/Stage02TaskPlanComponent.cs")
    assert "_previousFileContextHash" in component
    assert "待复检" in component
    assert "TaskPlanHash" in component
```

- [ ] **Step 2: 运行测试并确认失败**

Run:

```bash
dotnet test tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj -c Release --filter HBRTaskPlanTests
python -m pytest tests/test_plugin_contract.py::test_stage02_tracks_context_hash_changes -q
```

Expected: 至少一个测试失败。

- [ ] **Step 3: 实现组件级变化检测**

Stage 02 保存：

```csharp
private string _previousFileContextHash = string.Empty;
private string _status = "等待文件上下文";
```

求解成功前判断：

```csharp
bool changed = !string.IsNullOrWhiteSpace(_previousFileContextHash) &&
  !string.Equals(_previousFileContextHash, context.FileContextHash, StringComparison.OrdinalIgnoreCase);

HBRTaskPlan plan = TaskPlanCompiler.Compile(context);
_status = changed ? "任务已重新编译，下游结果待复检" : "任务计划已生成";
_previousFileContextHash = context.FileContextHash;
```

`TaskPlanHash` 变化本身就是下游 Grasshopper 组件失效信号；本轮不保存下游完成状态，但必须在 UI 和消息中明确“待复检”。

- [ ] **Step 4: 持久化上次上下文哈希**

`Stage02TaskPlanComponent.Write/Read` 保存：

```csharp
writer.SetString("HBR.Stage02.PreviousFileContextHash", _previousFileContextHash ?? string.Empty);
```

重新打开 GH 文件后，若连接上下文哈希不同，首次求解仍显示待复检。

- [ ] **Step 5: 运行测试**

Run:

```bash
dotnet test tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj -c Release
python -m pytest tests -q
```

Expected: 全部 PASS。

- [ ] **Step 6: 提交**

```bash
git add src/BIMBaoGui.Stage01/Stage02TaskPlanComponent.cs src/BIMBaoGui.Stage01/TaskPlanning/HBRTaskPlan.cs src/BIMBaoGui.Stage01/UI/Stage02ComponentAttributes.cs tests
git commit -m "feat: invalidate task plans when context changes"
```

---

### Task 9: 版本、文档、CI 和可安装 GHA 交付

**Files:**
- Modify: `src/BIMBaoGui.Stage01/AssemblyInfo.cs`
- Modify: `src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj`
- Modify: `README.md`
- Create: `docs/revit2020-stage01-stage02-runtime-checklist.md`
- Modify: `.github/workflows/build-stage01-gha.yml`
- Modify: `tests/test_plugin_contract.py`

**Interfaces:**
- Produces: 单一安装文件 `BIMBaoGui.Stage01.gha`，同时包含 Stage 01 和 Stage 02。

- [ ] **Step 1: 写版本和双组件合同测试**

```python
def test_release_is_v050_and_contains_both_workflow_components():
    project = read("src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj")
    assembly = read("src/BIMBaoGui.Stage01/AssemblyInfo.cs")
    assert "<Version>0.5.0</Version>" in project
    assert 'Version => "0.5.0"' in assembly
    assert (ROOT / "src/BIMBaoGui.Stage01/Stage01Component.cs").exists()
    assert (ROOT / "src/BIMBaoGui.Stage01/Stage02TaskPlanComponent.cs").exists()
```

- [ ] **Step 2: 运行测试并确认失败**

Run:

```bash
python -m pytest tests/test_plugin_contract.py::test_release_is_v050_and_contains_both_workflow_components -q
```

Expected: 版本仍不是 `0.5.0` 时 FAIL。

- [ ] **Step 3: 升级版本并更新 README**

`README.md` 使用以下工作流示例：

```text
湖北BIM报规｜01 文件初始化
    │ 文件上下文（HBR_FileContext）
    ▼
湖北BIM报规｜02 模型任务与骨架分流
    │ 任务计划（HBR_TaskPlan）
    ├─ 总平
    ├─ 地上
    └─ 地下
```

必须说明：

- `01` 完成写入与回读后，`初始化通过=True`。
- 将 `文件上下文` 输出连接到 `02`。
- `02` 不接受 JSON Panel 替代强类型端口。
- 上游条件变化后，`02` 自动重新编译，状态显示下游待复检。

- [ ] **Step 4: 编写 Revit 2020 实机验收清单**

`docs/revit2020-stage01-stage02-runtime-checklist.md` 必须包含以下逐项验收：

```text
A. 总平模型
1. 模型类型选择“总平模型”。
2. 建筑密度、容积率、绿地率为空时不能通过。
3. 输入 ≤30%、≤2.00、≥35% 后完成写入与回读。
4. Stage 02 只输出 SITE.* 任务。
5. 勾选绿地后出现 SITE.GREEN；取消后进入不适用任务。

B. 单体建筑—地上
1. 三个项目总目标显示“继承”，不重复手填。
2. Stage 02 路径为“地上”。
3. 不出现 SITE.* 和 UNDERGROUND.* 必建任务。

C. 单体建筑—地下
1. Stage 02 路径为“地下”。
2. 不出现 SITE.* 和 ABOVE.* 必建任务。

D. 文档匹配与失效
1. 把 A 文件的 FileContext 连接到 B 文件时必须阻断并显示两个文件名。
2. 修改任一项目条件后 FileContextHash 改变。
3. Stage 02 的 TaskPlanHash 改变并显示“下游结果待复检”。
4. 保存、关闭、重新打开 Revit 后，Stage 01 从 Extensible Storage 恢复同一上下文类型。
```

- [ ] **Step 5: 更新 CI 校验**

GitHub Actions 在构建后执行 PowerShell 反射检查：

```powershell
$assembly = [Reflection.Assembly]::LoadFile((Resolve-Path $gha))
$types = $assembly.GetTypes().FullName
if ($types -notcontains 'BIMBaoGui.Stage01.Stage01Component') { throw 'Stage01 component missing' }
if ($types -notcontains 'BIMBaoGui.Stage01.Stage02TaskPlanComponent') { throw 'Stage02 component missing' }
if ($types -notcontains 'BIMBaoGui.Stage01.GrasshopperTypes.HBRFileContextGoo') { throw 'HBRFileContextGoo missing' }
if ($types -notcontains 'BIMBaoGui.Stage01.GrasshopperTypes.HBRTaskPlanGoo') { throw 'HBRTaskPlanGoo missing' }
```

Artifact 名称改为：

```text
BIMBaoGui-Stage01-Stage02-Revit2020-Rhino8-v0.5.0
```

- [ ] **Step 6: 运行完整验证**

Run:

```bash
python -m pytest tests -q
dotnet test tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj -c Release
dotnet build src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj -c Release --no-restore
```

Expected:

- pytest 全部 PASS；
- xUnit 全部 PASS；
- Release 构建成功；
- `src/BIMBaoGui.Stage01/bin/Release/net48/BIMBaoGui.Stage01.gha` 存在且大小大于 50 KB。

- [ ] **Step 7: 提交**

```bash
git add src/BIMBaoGui.Stage01/AssemblyInfo.cs src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj README.md docs/revit2020-stage01-stage02-runtime-checklist.md .github/workflows/build-stage01-gha.yml tests/test_plugin_contract.py
git commit -m "chore: release Stage 01 to Stage 02 pipeline v0.5.0"
```

- [ ] **Step 8: 推送并创建 Draft PR**

```bash
git push -u origin feat/stage01-stage02-context-pipeline
gh pr create \
  --draft \
  --base feat/stage01-gha-file-initialization \
  --head feat/stage01-stage02-context-pipeline \
  --title "feat: connect Stage 01 context to Stage 02 task planning" \
  --body "Adds structured planning targets, HBR_FileContext, HBR_TaskPlan, typed Grasshopper ports, model routing, document identity checks, and context-hash invalidation for Revit 2020 + Rhino 8."
```

Expected: Draft PR 创建成功，不合并。

---

## Final Verification Gate

完成全部任务后，必须再次执行：

```bash
python -m pytest tests -q
dotnet test tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj -c Release
dotnet build src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj -c Release --no-restore
```

然后核对：

1. Stage 01 仍只有一个可视化初始化组件，不回退为散乱 Panel。
2. 总平模型三个规划控制目标均为结构化必填。
3. 单体地上、地下显示继承语义，不重复手填项目总目标。
4. Stage 01 首个输出确实为 `HBR_FileContext`，不是 JSON 文本。
5. Stage 02 必须连接该强类型输出，错误文档必须被阻断。
6. 三类模型得到互斥的骨架路径和任务集合。
7. 条件变化导致 `FileContextHash` 和 `TaskPlanHash` 变化。
8. GHA 版本为 `0.5.0`，CI artifact 可下载。
9. 未实现本轮明确排除的具体几何建模、跨文件汇总和 H-IFC 导出。
