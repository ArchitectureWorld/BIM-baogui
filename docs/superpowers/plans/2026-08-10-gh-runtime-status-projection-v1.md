# GH Runtime Status Projection v1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将唯一规则库计算出的运行支持状态和原因稳定投影到 Stage02、Stage03 的 Data Tree、卡片和 fields JSON，并交付已验证、已部署、可直接由 Grasshopper 测试的单一 GHA。

**Architecture:** `HbrRuleDatabase` 通过一个不可变 typed decision 成为唯一状态决策入口；Stage02 在 compiler 中固化 decision，Stage03 在扫描快照中固化 decision，formatter、report writer 和 UI 只消费快照。现有 Stage02 写入门禁、Stage03 Strict/Force、组件 GUID 和端口合同保持不变。

**Tech Stack:** C# 7.3 / .NET Framework 4.8、xUnit、Grasshopper 8 SDK、Revit 2020 API、Python 3.14、pytest 8.3.5、PowerShell、Git。

---

## File map

### New files

- `src/BIMBaoGui.Stage01/Rules/HbrRuntimeStatusDecision.cs` — runtime status、原因码和原因的不可变领域对象与常量。
- `tests/BIMBaoGui.Stage01.Core.Tests/HbrRuntimeStatusProjectionTests.cs` — 真实冻结规则的状态优先级和稳定原因测试。

### Modified files

- `src/BIMBaoGui.Stage01/Rules/HbrRuleDatabase.cs` — 唯一 decision API；旧 string API 委托给它。
- `src/BIMBaoGui.Stage01/Stage02/Stage02Models.cs` — 在预览 operation 中保存 runtime decision。
- `src/BIMBaoGui.Stage01/Stage02/Stage02PreviewCompiler.cs` — 从数据库固化 decision，并纳入 canonical preview hash。
- `src/BIMBaoGui.Stage01/Stage02/Stage02PreparationFieldDetailFormatter.cs` — 输出三个稳定 runtime 字段。
- `src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs` — 形成卡片计数快照。
- `src/BIMBaoGui.Stage01/UI/Stage02PreparationAttributes.cs` — 显示运行支持摘要和首因。
- `src/BIMBaoGui.Stage01/Stage03/Stage03ValidationModels.cs` — 将 runtime decision 与扫描 status 分开保存。
- `src/BIMBaoGui.Stage01/Revit/Stage03ModelScanService.cs` — 对每条真实规则只求值一次并传入字段快照。
- `src/BIMBaoGui.Stage01/Stage03/Stage03WorkflowCoordinator.cs` — clone、同一扫描所有权和 fail-closed 校验传播三字段。
- `src/BIMBaoGui.Stage01/Stage03/Stage03FieldDetailFormatter.cs` — Data Tree 输出 runtime decision。
- `src/BIMBaoGui.Stage01/Diagnostics/Stage03FieldReportWriter.cs` — fields JSON 输出、比较并确定性快照 runtime decision。
- `src/BIMBaoGui.Stage01/Stage03ValidationExportComponent.cs` — 形成 Stage03 卡片状态计数。
- `src/BIMBaoGui.Stage01/UI/Stage03ComponentAttributes.cs` — 显示运行支持摘要。
- `tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj` — 链接新的 Rules 源文件。
- `tests/BIMBaoGui.Stage01.Core.Tests/Stage02PreviewCompilerTests.cs` — Stage02 compiler 决策固化测试。
- `tests/BIMBaoGui.Stage01.Core.Tests/Stage02PreparationInputPolicyTests.cs` — Stage02 字段 JSON 稳定性测试。
- `tests/BIMBaoGui.Stage01.Core.Tests/Stage03FieldDetailFormatterTests.cs` — 扫描 status 与 runtime status 并存测试。
- `tests/BIMBaoGui.Stage01.Core.Tests/Stage03FieldReportWriterTests.cs` — fields JSON 和排序确定性测试。
- `tests/BIMBaoGui.Stage01.Core.Tests/Stage03WorkflowCoordinatorTests.cs` — snapshot 篡改和缺失 decision 的 fail-closed 测试。
- `tests/BIMBaoGui.Stage01.Core.Tests/Stage03ExportGatePolicyTests.cs` — Strict/Force 行为不变测试。
- `tests/test_stage02_component_contract.py` — Stage02 端口、Data Tree、卡片和禁止重复推导合同。
- `tests/test_stage03_component_contract.py` — Stage03 端口、卡片和集中 API 合同。

### Generated/deployed but not committed

- `artifacts/BIMBaoGui.Stage01.gha`
- `artifacts/artifact-manifest.json`
- `C:/Users/2899/AppData/Roaming/Grasshopper/Libraries/BIMbaogui/BIMBaoGui.Stage01.gha`

---

### Task 1: Add the single typed runtime decision API

**Files:**
- Create: `src/BIMBaoGui.Stage01/Rules/HbrRuntimeStatusDecision.cs`
- Create: `tests/BIMBaoGui.Stage01.Core.Tests/HbrRuntimeStatusProjectionTests.cs`
- Modify: `src/BIMBaoGui.Stage01/Rules/HbrRuleDatabase.cs:182-201`
- Modify: `tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj:125-128`
- Test: `tests/BIMBaoGui.Stage01.Core.Tests/HbrRuleDatabaseTests.cs:110-123`

- [ ] **Step 1: Write the failing runtime projection tests**

Create tests that exercise real properties from the embedded frozen pack:

```csharp
[Fact]
public void Decision_uses_owner_precedence_for_real_frozen_properties()
{
  HbrRuleDatabase database = HbrRuleDatabase.Current;
  HbrRuntimeStatusDecision ownerBlocked = database.GetRuntimeStatusDecision(
    database.PropertiesById["d9ae268e-8d11-59e7-bbff-dc7521ec7889"]);
  HbrRuntimeStatusDecision unclassified = database.GetRuntimeStatusDecision(
    database.PropertiesById["ee41f5a8-562b-56f4-b8ef-331783746e09"]);

  Assert.Equal("NOT_IMPLEMENTED", ownerBlocked.Status);
  Assert.Equal("OWNER_STRATEGY_NOT_IMPLEMENTED", ownerBlocked.ReasonCode);
  Assert.Contains("CANONICAL_SPATIAL_ZONE_RECORD", ownerBlocked.Reason);
  Assert.Equal("UNCLASSIFIED_REQUIREMENT", unclassified.Status);
  Assert.Equal("REQUIREMENT_LEVEL_UNCLASSIFIED", unclassified.ReasonCode);
  Assert.Contains("UNCLASSIFIED", unclassified.Reason);
  Assert.Equal(
    ownerBlocked.Status,
    database.GetEffectiveRuntimeStatus(
      database.PropertiesById[
        "d9ae268e-8d11-59e7-bbff-dc7521ec7889"]));
}

[Fact]
public void All_frozen_properties_have_non_empty_typed_decisions()
{
  HbrRuleDatabase database = HbrRuleDatabase.Current;
  HbrRuntimeStatusDecision[] decisions = database.Package.Properties
    .Select(database.GetRuntimeStatusDecision)
    .ToArray();

  Assert.Equal(359, decisions.Length);
  Assert.Equal(57, decisions.Count(x => x.Status == "NOT_IMPLEMENTED"));
  Assert.Equal(302, decisions.Count(
    x => x.Status == "UNCLASSIFIED_REQUIREMENT"));
  Assert.All(decisions, decision =>
  {
    Assert.False(string.IsNullOrWhiteSpace(decision.Status));
    Assert.False(string.IsNullOrWhiteSpace(decision.ReasonCode));
    Assert.False(string.IsNullOrWhiteSpace(decision.Reason));
  });
}
```

Add the new production source link to the Core test project:

```xml
<Compile Include="..\..\src\BIMBaoGui.Stage01\Rules\HbrRuntimeStatusDecision.cs"
         Link="Rules\HbrRuntimeStatusDecision.cs" />
```

- [ ] **Step 2: Run the test and verify RED**

Run:

```powershell
dotnet test tests\BIMBaoGui.Stage01.Core.Tests\BIMBaoGui.Stage01.Core.Tests.csproj `
  -c Release `
  --filter "FullyQualifiedName~HbrRuntimeStatusProjectionTests"
```

Expected: compilation fails because `HbrRuntimeStatusDecision` and `GetRuntimeStatusDecision` do not exist. Record the exact failing count or compiler diagnostics before production code is written.

- [ ] **Step 3: Implement the immutable decision and single database path**

Create the exact public surface:

```csharp
namespace BIMBaoGui.Stage01.Rules
{
  public static class HbrRuntimeStatuses
  {
    public const string Supported = "SUPPORTED";
    public const string NotImplemented = "NOT_IMPLEMENTED";
    public const string UnclassifiedRequirement =
      "UNCLASSIFIED_REQUIREMENT";
    public const string OfficialEvidenceOnly = "OFFICIAL_EVIDENCE_ONLY";
  }

  public static class HbrRuntimeReasonCodes
  {
    public const string Supported = "SUPPORTED";
    public const string OwnerStrategyNotImplemented =
      "OWNER_STRATEGY_NOT_IMPLEMENTED";
    public const string RequirementLevelUnclassified =
      "REQUIREMENT_LEVEL_UNCLASSIFIED";
    public const string OfficialEvidenceOnly = "OFFICIAL_EVIDENCE_ONLY";
  }

  public sealed class HbrRuntimeStatusDecision
  {
    internal HbrRuntimeStatusDecision(
      string status,
      string reasonCode,
      string reason)
    {
      if (string.IsNullOrWhiteSpace(status)
        || string.IsNullOrWhiteSpace(reasonCode)
        || string.IsNullOrWhiteSpace(reason))
        throw new InvalidDataException(
          "HBR runtime status decision must be complete.");
      Status = status;
      ReasonCode = reasonCode;
      Reason = reason;
    }

    public string Status { get; }
    public string ReasonCode { get; }
    public string Reason { get; }
  }
}
```

Add `using System; using System.IO;` and implement `HbrRuleDatabase.GetRuntimeStatusDecision` so both support dimensions must exist, the pack-defined precedence selects one status, and the stable reason comes from that winning dimension:

```csharp
public HbrRuntimeStatusDecision GetRuntimeStatusDecision(
  HbrRuleProperty property)
{
  if (property == null) throw new ArgumentNullException(nameof(property));
  if (!_ownerRuntimeStatuses.TryGetValue(
      property.IfcWrite.OwnerStrategy,
      out string ownerStatus))
    throw new InvalidDataException(
      "HBRP unknown owner strategy for " + property.PropertyId + ".");
  if (!_requirementRuntimeStatuses.TryGetValue(
      property.Requirement.Level,
      out string requirementStatus))
    throw new InvalidDataException(
      "HBRP unknown requirement level for " + property.PropertyId + ".");

  string status = _runtimeStatusPrecedence.FirstOrDefault(
    value => value == ownerStatus || value == requirementStatus);
  switch (status)
  {
    case HbrRuntimeStatuses.NotImplemented:
      return new HbrRuntimeStatusDecision(
        status,
        HbrRuntimeReasonCodes.OwnerStrategyNotImplemented,
        "当前 IFC owner strategy 尚未实现："
          + property.IfcWrite.OwnerStrategy + "。");
    case HbrRuntimeStatuses.UnclassifiedRequirement:
      return new HbrRuntimeStatusDecision(
        status,
        HbrRuntimeReasonCodes.RequirementLevelUnclassified,
        "字段 requirement.level 为 "
          + property.Requirement.Level + "，需求等级待定。");
    case HbrRuntimeStatuses.OfficialEvidenceOnly:
      return new HbrRuntimeStatusDecision(
        status,
        HbrRuntimeReasonCodes.OfficialEvidenceOnly,
        "该字段仅用于官方证据对账，不自动形成写入策略。");
    case HbrRuntimeStatuses.Supported:
      return new HbrRuntimeStatusDecision(
        status,
        HbrRuntimeReasonCodes.Supported,
        "当前运行策略已支持。");
    default:
      throw new InvalidDataException(
        "HBRP has no runtime status for property "
          + property.PropertyId + ".");
  }
}

public string GetEffectiveRuntimeStatus(HbrRuleProperty property)
{
  return GetRuntimeStatusDecision(property).Status;
}
```

- [ ] **Step 4: Run focused and existing rule database tests for GREEN**

Run:

```powershell
dotnet test tests\BIMBaoGui.Stage01.Core.Tests\BIMBaoGui.Stage01.Core.Tests.csproj `
  -c Release `
  --filter "FullyQualifiedName~HbrRuntimeStatusProjectionTests|FullyQualifiedName~HbrRuleDatabaseTests"
```

Expected: all selected tests pass; the established `57 + 302 = 359` distribution remains unchanged.

- [ ] **Step 5: Commit Task 1**

```powershell
git add -- `
  src/BIMBaoGui.Stage01/Rules/HbrRuntimeStatusDecision.cs `
  src/BIMBaoGui.Stage01/Rules/HbrRuleDatabase.cs `
  tests/BIMBaoGui.Stage01.Core.Tests/HbrRuntimeStatusProjectionTests.cs `
  tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj
git commit -m "feat(gh): centralize runtime status decisions"
```

---

### Task 2: Project runtime decisions through Stage02 without changing write gates

**Files:**
- Modify: `src/BIMBaoGui.Stage01/Stage02/Stage02Models.cs:279-533`
- Modify: `src/BIMBaoGui.Stage01/Stage02/Stage02PreviewCompiler.cs:178-271,578-689`
- Modify: `src/BIMBaoGui.Stage01/Stage02/Stage02PreparationFieldDetailFormatter.cs:10-119`
- Modify: `src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs:20-38,1217-1255`
- Modify: `src/BIMBaoGui.Stage01/UI/Stage02PreparationAttributes.cs:12-13,164-292`
- Test: `tests/BIMBaoGui.Stage01.Core.Tests/Stage02PreviewCompilerTests.cs`
- Test: `tests/BIMBaoGui.Stage01.Core.Tests/Stage02PreparationInputPolicyTests.cs:619-729,880-911`
- Test: `tests/test_stage02_component_contract.py`

- [ ] **Step 1: Write Stage02 RED tests**

Add compiler tests named:

```csharp
[Fact]
public void Preview_projects_database_runtime_decision_without_adding_blocker()

[Fact]
public void Preview_runtime_decision_is_canonical_and_overwrites_forged_input()
```

The first uses a real Stage02 property and asserts `RuntimeStatus`, `RuntimeBlockCode`, and `RuntimeBlockReason` equal `_database.GetRuntimeStatusDecision(property)`, while the operation blocker count remains unchanged. The second supplies forged runtime metadata, compiles the preview, and asserts the compiler replaces all three values and that changing any runtime value changes the canonical payload/hash.

Extend `FieldDetailFormatter_RoundTripsEscapedValuesWithStableBytes` with:

```csharp
Assert.Equal(22, root.Count);
Assert.Equal("NOT_IMPLEMENTED", root["runtimeStatus"]);
Assert.Equal("OWNER_STRATEGY_NOT_IMPLEMENTED", root["runtimeBlockCode"]);
Assert.Equal("运行原因" + tricky, root["runtimeBlockReason"]);
```

Insert these expected keys after `applicability` in `AssertTopLevelKeyOrder`:

```csharp
"\"runtimeStatus\":",
"\"runtimeBlockCode\":",
"\"runtimeBlockReason\":",
```

Add Python component contracts that require the three JSON keys and the Chinese card labels `运行支持`、`未实现`、`需求待定`, while rejecting `HbrRuleDatabase`, `OwnerStrategy`, `RequirementLevel`, `GetRuntimeStatusDecision`, and `GetEffectiveRuntimeStatus` inside `GetUiSnapshot` and `Stage02PreparationAttributes`.

- [ ] **Step 2: Run Stage02 tests and verify RED**

Run:

```powershell
dotnet test tests\BIMBaoGui.Stage01.Core.Tests\BIMBaoGui.Stage01.Core.Tests.csproj `
  -c Release `
  --filter "FullyQualifiedName~Stage02PreviewCompilerTests|FullyQualifiedName~Stage02PreparationInputPolicyTests.FieldDetailFormatter"
python -m pytest tests\test_stage02_component_contract.py -q `
  -k "runtime or field_detail or card"
```

Expected: selected .NET and Python tests fail only because runtime fields, canonicalization, and card text are absent. Record exact failures.

- [ ] **Step 3: Add immutable runtime metadata to Stage02 operations**

Keep all existing public/internal constructor signatures. Add to `Stage02WriteOperation`:

```csharp
public string RuntimeStatus { get; }
public string RuntimeBlockCode { get; }
public string RuntimeBlockReason { get; }

internal Stage02WriteOperation WithRuntimeDecision(
  string runtimeStatus,
  string runtimeBlockCode,
  string runtimeBlockReason)
{
  if (string.IsNullOrWhiteSpace(runtimeStatus)
    || string.IsNullOrWhiteSpace(runtimeBlockCode)
    || string.IsNullOrWhiteSpace(runtimeBlockReason))
    throw new ArgumentException(
      "Stage02 runtime decision must be complete.");
  return new Stage02WriteOperation(
    PropertyId,
    ParameterGuid,
    ParameterName,
    ObservedState,
    SuggestedValue,
    ValueSource,
    SuggestionConfidence,
    BindingAction,
    ValueAction,
    Applicability,
    Blockers,
    BindingScope,
    StorageType,
    ParameterType,
    RequirementLevel,
    ConditionId,
    runtimeStatus,
    runtimeBlockCode,
    runtimeBlockReason);
}
```

Extend only the private full constructor with the final three arguments. All existing constructors pass three empty strings. `WithRuleMetadata` and `WithObservedState` pass the current three runtime properties unchanged.

- [ ] **Step 4: Canonically stamp the database decision in the compiler**

After requirement normalization and rule metadata, chain:

```csharp
HbrRuntimeStatusDecision runtimeDecision =
  _database.GetRuntimeStatusDecision(property);
normalized.Add(requirementNormalized
  .WithRuleMetadata(
    requirementNormalized.ObservedState.With(
      targetUniqueId: targetUniqueId),
    property.Revit.BindingScope,
    property.Revit.StorageType,
    property.Revit.ParameterType,
    property.Requirement.Level,
    conditionId)
  .WithRuntimeDecision(
    runtimeDecision.Status,
    runtimeDecision.ReasonCode,
    runtimeDecision.Reason));
```

In `Stage02Canonicalizer.AppendOperation`, write these fields after `applicability` and before `bindingAction`:

```csharp
AppendProperty(builder, "runtimeStatus", operation.RuntimeStatus, false);
AppendProperty(
  builder,
  "runtimeBlockCode",
  operation.RuntimeBlockCode,
  false);
AppendProperty(
  builder,
  "runtimeBlockReason",
  operation.RuntimeBlockReason,
  false);
```

Do not add a Stage02 blocker and do not change `Stage02RequirementDecisionPolicy`.

- [ ] **Step 5: Project stable fields and card summary**

In the formatter, append the same three fields after `applicability` and before `bindingAction`.

Add to `Stage02PreparationUiSnapshot`:

```csharp
internal int RuntimeNotImplementedCount { get; set; }
internal int RuntimeUnclassifiedRequirementCount { get; set; }
internal string FirstRuntimeBlockReason { get; set; } = string.Empty;
```

In `GetUiSnapshot`, flatten operations ordered by element `UniqueId`, then `PropertyId`, then `RuntimeBlockCode`; count operation records, not distinct property IDs:

```csharp
Stage02WriteOperation[] runtimeOperations = (_preview == null
    ? Array.Empty<Stage02MatchedElement>()
    : _preview.Elements)
  .OrderBy(x => x.Element.UniqueId, StringComparer.Ordinal)
  .SelectMany(x => x.Operations
    .OrderBy(y => y.PropertyId, StringComparer.Ordinal)
    .ThenBy(y => y.RuntimeBlockCode, StringComparer.Ordinal))
  .ToArray();
```

Populate counts from `RuntimeStatus` and select the first reason whose status is not `SUPPORTED`. UI must not query the database or re-read requirement/owner strategy.

Set `CardHeight = 470f`, body height `308f`, preserve the footer anchored to `_contentBounds.Bottom - 42f`, and add two rows:

```csharp
{
  "运行支持",
  "未实现 " + snapshot.RuntimeNotImplementedCount
    + "｜需求待定 "
    + snapshot.RuntimeUnclassifiedRequirementCount
},
{
  "首条运行原因",
  string.IsNullOrWhiteSpace(snapshot.FirstRuntimeBlockReason)
    ? "无"
    : Compact(snapshot.FirstRuntimeBlockReason, 46)
},
```

Do not change existing component ports or GUID.

- [ ] **Step 6: Run Stage02 GREEN and focused regression**

Run:

```powershell
dotnet test tests\BIMBaoGui.Stage01.Core.Tests\BIMBaoGui.Stage01.Core.Tests.csproj `
  -c Release `
  --filter "FullyQualifiedName~HbrRuntimeStatusProjectionTests|FullyQualifiedName~Stage02PreviewCompilerTests|FullyQualifiedName~Stage02PreparationInputPolicyTests|FullyQualifiedName~Stage02RequirementDecisionPolicyTests"
python -m pytest tests\test_stage02_component_contract.py -q
```

Expected: all selected tests pass; `Stage02RequirementDecisionPolicyTests` proves the existing write-gate behavior remains unchanged.

- [ ] **Step 7: Commit Task 2**

```powershell
git add -- `
  src/BIMBaoGui.Stage01/Stage02/Stage02Models.cs `
  src/BIMBaoGui.Stage01/Stage02/Stage02PreviewCompiler.cs `
  src/BIMBaoGui.Stage01/Stage02/Stage02PreparationFieldDetailFormatter.cs `
  src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs `
  src/BIMBaoGui.Stage01/UI/Stage02PreparationAttributes.cs `
  tests/BIMBaoGui.Stage01.Core.Tests/Stage02PreviewCompilerTests.cs `
  tests/BIMBaoGui.Stage01.Core.Tests/Stage02PreparationInputPolicyTests.cs `
  tests/test_stage02_component_contract.py
git commit -m "feat(gh): show runtime support in Stage02"
```

---

### Task 3: Separate Stage03 runtime capability from scan results

**Files:**
- Modify: `src/BIMBaoGui.Stage01/Stage03/Stage03ValidationModels.cs:161-205`
- Modify: `src/BIMBaoGui.Stage01/Revit/Stage03ModelScanService.cs`
- Modify: `src/BIMBaoGui.Stage01/Stage03/Stage03WorkflowCoordinator.cs`
- Modify: `src/BIMBaoGui.Stage01/Stage03/Stage03FieldDetailFormatter.cs:148-195`
- Modify: `src/BIMBaoGui.Stage01/Diagnostics/Stage03FieldReportWriter.cs:240-279,399-484,701-787`
- Modify: `src/BIMBaoGui.Stage01/Stage03ValidationExportComponent.cs:20-69,354-385`
- Modify: `src/BIMBaoGui.Stage01/UI/Stage03ComponentAttributes.cs:13-16,176-272`
- Test: `tests/BIMBaoGui.Stage01.Core.Tests/Stage03FieldDetailFormatterTests.cs`
- Test: `tests/BIMBaoGui.Stage01.Core.Tests/Stage03FieldReportWriterTests.cs`
- Test: `tests/BIMBaoGui.Stage01.Core.Tests/Stage03WorkflowCoordinatorTests.cs`
- Test: `tests/BIMBaoGui.Stage01.Core.Tests/Stage03ExportGatePolicyTests.cs`
- Test: `tests/test_stage03_component_contract.py`

- [ ] **Step 1: Write Stage03 RED tests**

Add non-empty runtime values to the common field fixtures:

```csharp
RuntimeStatus = "NOT_IMPLEMENTED",
RuntimeBlockCode = "OWNER_STRATEGY_NOT_IMPLEMENTED",
RuntimeBlockReason =
  "当前 IFC owner strategy 尚未实现：CANONICAL_SPATIAL_ZONE_RECORD。",
```

Update formatter assertions from 35 to 38 fields and assert runtime metadata coexists with `status = MISSING_PARAMETER`.

Add report tests that assert the same three values appear in `fields[]`; add three `FieldResultComparer` tie-pair cases, one for each runtime property; retain forward/reverse byte-equality assertions.

Add coordinator cases where a translator changes `RuntimeStatus`, `RuntimeBlockCode`, or `RuntimeBlockReason`; each mutation must fail the scan-owned comparison. Add one scan snapshot with an empty runtime value and assert `INVALID_FIELD_STATUS`.

Add gate regression cases:

```text
RuleNotImplemented + NOT_IMPLEMENTED:
  Strict => BLOCK, blocker code RULE_NOT_IMPLEMENTED
  Force + non-empty reason => ALLOW, blocker retained

UnclassifiedRequirement + UNCLASSIFIED_REQUIREMENT:
  Strict => BLOCK, blocker code UNCLASSIFIED_REQUIREMENT
  Force + non-empty reason => ALLOW, blocker retained
```

Add Python contracts that keep 5 inputs, 8 outputs and the existing component GUID; require `运行支持` in the card and centralized database decision use in the scanner; prohibit requirement/owner inference in UI.

- [ ] **Step 2: Run Stage03 tests and verify RED**

Run:

```powershell
dotnet test tests\BIMBaoGui.Stage01.Core.Tests\BIMBaoGui.Stage01.Core.Tests.csproj `
  -c Release `
  --filter "FullyQualifiedName~Stage03FieldDetailFormatterTests|FullyQualifiedName~Stage03FieldReportWriterTests|FullyQualifiedName~Stage03WorkflowCoordinatorTests|FullyQualifiedName~Stage03ExportGatePolicyTests"
python -m pytest tests\test_stage03_component_contract.py -q
```

Expected: tests fail because the three runtime snapshot fields and card summary are absent. Existing gate cases remain green; only new runtime projection assertions fail.

- [ ] **Step 3: Add runtime metadata to Stage03 field snapshots**

Add immediately after `Applicability`:

```csharp
public string RuntimeStatus { get; set; } = string.Empty;
public string RuntimeBlockCode { get; set; } = string.Empty;
public string RuntimeBlockReason { get; set; } = string.Empty;
```

In `Stage03ModelScanService`, build one propertyId-keyed decision snapshot from the already sorted properties:

```csharp
IReadOnlyDictionary<string, HbrRuntimeStatusDecision> runtimeDecisions =
  properties.ToDictionary(
    property => property.PropertyId,
    property => _database.GetRuntimeStatusDecision(property),
    StringComparer.Ordinal);
```

Pass the matching decision through `BuildCarrierFailureField`, `BuildField`, and `BaseField`. `BaseField` assigns exactly:

```csharp
RuntimeStatus = runtimeDecision.Status,
RuntimeBlockCode = runtimeDecision.ReasonCode,
RuntimeBlockReason = runtimeDecision.Reason,
```

No formatter, writer or UI may call the database.

- [ ] **Step 4: Preserve runtime metadata through coordinator and reports**

In `Stage03WorkflowCoordinator`:

```csharp
// SameScanOwnedField
&& string.Equals(left.RuntimeStatus, right.RuntimeStatus,
  StringComparison.Ordinal)
&& string.Equals(left.RuntimeBlockCode, right.RuntimeBlockCode,
  StringComparison.Ordinal)
&& string.Equals(left.RuntimeBlockReason, right.RuntimeBlockReason,
  StringComparison.Ordinal)
```

`CloneField` copies the three strings. `SnapshotFields` rejects a field when any of the three values is null, empty, or whitespace and emits the existing `INVALID_FIELD_STATUS` technical failure path.

In `Stage03FieldReportWriter`, `SnapshotField` copies all three fields; `BuildField` emits:

```csharp
["runtimeStatus"] = field.RuntimeStatus,
["runtimeBlockCode"] = field.RuntimeBlockCode,
["runtimeBlockReason"] = field.RuntimeBlockReason,
```

`FieldResultComparer` compares these three strings immediately after `Applicability`, using `StringComparer.Ordinal`. This keeps reverse-order reports deterministic and ensures tie pairs cannot collapse.

In `Stage03FieldDetailFormatter.FormatField`, emit the same three keys. Preserve existing scan `status` and messages.

- [ ] **Step 5: Add the Stage03 card summary without changing ports or gates**

Extend `Stage03ComponentViewState` with four integer counts:

```csharp
internal int RuntimeSupportedCount { get; }
internal int RuntimeNotImplementedCount { get; }
internal int RuntimeUnclassifiedRequirementCount { get; }
internal int RuntimeOfficialEvidenceOnlyCount { get; }
```

`BuildViewStateLocked` counts `field.RuntimeStatus` only. It must not inspect requirement level, owner strategy, or recompute a decision.

Set `CardHeight = 364f`, body height `230f`, and footer Y to `_cardBounds.Y + 326f`. Insert a row after `字段计数`:

```csharp
{
  "运行支持",
  "支持 " + view.RuntimeSupportedCount
    + "｜未实现 " + view.RuntimeNotImplementedCount
    + "｜需求待定 " + view.RuntimeUnclassifiedRequirementCount
    + "｜仅证据 " + view.RuntimeOfficialEvidenceOnlyCount
},
```

Do not modify `Stage03ExportGatePolicy`, `Stage03ScannerFieldPolicy`, Strict/Force branches, component GUID, or the 5-input/8-output layout.

- [ ] **Step 6: Run Stage03 GREEN and gate regression**

Run:

```powershell
dotnet test tests\BIMBaoGui.Stage01.Core.Tests\BIMBaoGui.Stage01.Core.Tests.csproj `
  -c Release `
  --filter "FullyQualifiedName~HbrRuntimeStatusProjectionTests|FullyQualifiedName~Stage03FieldDetailFormatterTests|FullyQualifiedName~Stage03FieldReportWriterTests|FullyQualifiedName~Stage03WorkflowCoordinatorTests|FullyQualifiedName~Stage03ExportGatePolicyTests|FullyQualifiedName~Stage03FieldStatusTests"
python -m pytest tests\test_stage03_component_contract.py -q
```

Expected: all selected tests pass; old gate results and port/GUID contracts remain unchanged.

- [ ] **Step 7: Commit Task 3**

```powershell
git add -- `
  src/BIMBaoGui.Stage01/Stage03/Stage03ValidationModels.cs `
  src/BIMBaoGui.Stage01/Revit/Stage03ModelScanService.cs `
  src/BIMBaoGui.Stage01/Stage03/Stage03WorkflowCoordinator.cs `
  src/BIMBaoGui.Stage01/Stage03/Stage03FieldDetailFormatter.cs `
  src/BIMBaoGui.Stage01/Diagnostics/Stage03FieldReportWriter.cs `
  src/BIMBaoGui.Stage01/Stage03ValidationExportComponent.cs `
  src/BIMBaoGui.Stage01/UI/Stage03ComponentAttributes.cs `
  tests/BIMBaoGui.Stage01.Core.Tests/Stage03FieldDetailFormatterTests.cs `
  tests/BIMBaoGui.Stage01.Core.Tests/Stage03FieldReportWriterTests.cs `
  tests/BIMBaoGui.Stage01.Core.Tests/Stage03WorkflowCoordinatorTests.cs `
  tests/BIMBaoGui.Stage01.Core.Tests/Stage03ExportGatePolicyTests.cs `
  tests/test_stage03_component_contract.py
git commit -m "feat(gh): separate Stage03 runtime support status"
```

---

### Task 4: Verify, bind the artifact to HEAD, and deploy one testable GHA

**Files:**
- Verify: `specs/hbr-rules/v1/source/hbr_rule_source.v1.json`
- Verify: `specs/hbr-rules/v1/manifest.sha256.json`
- Generate: `artifacts/BIMBaoGui.Stage01.gha`
- Generate: `artifacts/artifact-manifest.json`
- Replace: `C:/Users/2899/AppData/Roaming/Grasshopper/Libraries/BIMbaogui/BIMBaoGui.Stage01.gha`
- Remove exact obsolete files:
  - `C:/Users/2899/AppData/Roaming/Grasshopper/Libraries/BIMbaogui/BIMBaoGui.Stage01.gha.bak-20260806-181340`
  - `C:/Users/2899/AppData/Roaming/Grasshopper/Libraries/BIMbaogui/BIMBaoGui.Stage01.gha.bak-20260806-215157`
  - `C:/Users/2899/AppData/Roaming/Grasshopper/Libraries/BIMbaogui/BIMBaoGui.Stage01.gha.pending`

- [ ] **Step 1: Run the complete repository test matrix fresh**

```powershell
$env:PYTEST_DISABLE_PLUGIN_AUTOLOAD='1'
$env:PYTHONDONTWRITEBYTECODE='1'
python -m pytest tests -q
dotnet test tests\BIMBaoGui.Stage01.Core.Tests\BIMBaoGui.Stage01.Core.Tests.csproj `
  -c Release --logger "console;verbosity=minimal"
```

Expected: zero failures. Record exact Python and .NET totals from this fresh run.

- [ ] **Step 2: Build production Release with warnings as errors**

```powershell
dotnet build src\BIMBaoGui.Stage01\BIMBaoGui.Stage01.csproj `
  -c Release `
  -p:ContinuousIntegrationBuild=true `
  -p:TreatWarningsAsErrors=true
```

Expected: exit 0, 0 warnings, 0 errors, and both files exist:

```text
src/BIMBaoGui.Stage01/bin/Release/net48/BIMBaoGui.Stage01.dll
src/BIMBaoGui.Stage01/bin/Release/net48/BIMBaoGui.Stage01.gha
```

- [ ] **Step 3: Prove repository and frozen baseline invariants**

```powershell
git diff --check
git status --short
git diff hbr-planning-mapping-v1.0.0..HEAD -- `
  specs/hbr-rules/v1/source/hbr_rule_source.v1.json `
  specs/hbr-rules/v1/manifest.sha256.json
git rev-parse 'hbr-planning-mapping-v1.0.0^{}'
```

Expected: `git diff --check` clean; no uncommitted production changes; both frozen files have no diff; peeled tag remains `0c5d2c1100c9c80c4306354bab553debe8f191ca`.

- [ ] **Step 4: Create the HEAD-bound artifact and manifest**

```powershell
New-Item -ItemType Directory -Force -Path artifacts | Out-Null
Copy-Item -LiteralPath `
  'src\BIMBaoGui.Stage01\bin\Release\net48\BIMBaoGui.Stage01.gha' `
  -Destination 'artifacts\BIMBaoGui.Stage01.gha' -Force
$commitSha = (git rev-parse HEAD).Trim()
python tools\build_hbr_artifact_manifest.py `
  --root . `
  --gha artifacts\BIMBaoGui.Stage01.gha `
  --rules-manifest specs\hbr-rules\v1\manifest.sha256.json `
  --commit-sha $commitSha `
  --output artifacts\artifact-manifest.json
```

Verify the manifest commit equals `HEAD`, its byte count equals the artifact, and its SHA-256 equals `Get-FileHash`.

- [ ] **Step 5: Verify deployment targets and host-process gate before deletion**

```powershell
$deployDir = 'C:\Users\2899\AppData\Roaming\Grasshopper\Libraries\BIMbaogui'
$resolvedDeploy = (Resolve-Path -LiteralPath $deployDir).Path
if ($resolvedDeploy -ne $deployDir) {
  throw "部署目录解析异常：$resolvedDeploy"
}
$hosts = @(Get-Process -Name Revit,Rhino -ErrorAction SilentlyContinue)
if ($hosts.Count -ne 0) {
  $hosts | Select-Object ProcessName,Id
  throw '请先正常关闭 Revit、Rhino.Inside.Revit、Grasshopper 和 Rhino。'
}
$obsolete = @(
  "$deployDir\BIMBaoGui.Stage01.gha.bak-20260806-181340",
  "$deployDir\BIMBaoGui.Stage01.gha.bak-20260806-215157",
  "$deployDir\BIMBaoGui.Stage01.gha.pending"
)
$obsolete | ForEach-Object {
  if (Test-Path -LiteralPath $_) {
    $resolved = (Resolve-Path -LiteralPath $_).Path
    if ([IO.Path]::GetDirectoryName($resolved) -ne $deployDir) {
      throw "拒绝删除部署目录外文件：$resolved"
    }
  }
}
```

Expected: exact deployment directory verified, Revit/Rhino host count is zero, and every existing obsolete target resolves inside that directory.

- [ ] **Step 6: Remove the three exact obsolete copies and deploy the fixed-name GHA**

```powershell
$obsolete | ForEach-Object {
  if (Test-Path -LiteralPath $_) {
    Remove-Item -LiteralPath $_ -Force
  }
}
Copy-Item -LiteralPath `
  'artifacts\BIMBaoGui.Stage01.gha' `
  -Destination "$deployDir\BIMBaoGui.Stage01.gha" -Force
```

Only these three obsolete files may be deleted. Do not delete or move the deployment directory. These deletions are permanent; repository commits remain the recovery source.

- [ ] **Step 7: Verify deployed bytes, assembly, rule pack, and single-file invariant**

```powershell
$target = "$deployDir\BIMBaoGui.Stage01.gha"
$candidateHash = (Get-FileHash `
  'artifacts\BIMBaoGui.Stage01.gha' -Algorithm SHA256).Hash.ToLowerInvariant()
$deployedHash = (Get-FileHash $target -Algorithm SHA256).Hash.ToLowerInvariant()
$assembly = [Reflection.AssemblyName]::GetAssemblyName($target)
$extras = @(Get-ChildItem -LiteralPath $deployDir -File |
  Where-Object { $_.Name -like 'BIMBaoGui.Stage01.gha.*' })
if ($deployedHash -ne $candidateHash) {
  throw '部署后 GHA 哈希不一致。'
}
if ($assembly.Name -ne 'BIMBaoGui.Stage01'
  -or $assembly.Version.ToString() -ne '0.9.0.0') {
  throw '部署程序集身份不正确。'
}
if ($extras.Count -ne 0) {
  throw '活动目录仍存在 GHA 备份或 pending 文件。'
}
```

Run the existing embedded-rule-pack verification used by `test_hbr_runtime_packaging_contract.py` against both candidate and deployed GHA; expected embedded rule SHA-256 remains `7eb0888016817b93ed6ba191bc2183e3079b3d7ea2f7d58ed74776f88448a3d3`.

- [ ] **Step 8: Hand off the exact GH test sequence**

The user starts:

```text
Revit 2020
  -> Rhino.Inside.Revit / Start
  -> Grasshopper
```

The Grasshopper menu must expose exactly:

```text
湖北BIM报规｜01 文件初始化
湖北BIM报规｜02 构件与属性准备
湖北BIM报规｜03 检测、导出与 H-IFC 转译
```

For the first manual test, connect Stage01 context to Stage02, generate a preview, and inspect Stage02 `Fields` plus its card. Every field record must include `runtimeStatus`, `runtimeBlockCode`, and `runtimeBlockReason`; the card must show `未实现 N｜需求待定 N`. Stage03 must show the same runtime status independently from its existing scan `status`.

---

## Final completion audit

Before claiming completion, verify every item below from current state:

- [ ] Design commit `523d1ea` remains in branch history.
- [ ] Task commits are present and working tree is clean.
- [ ] Frozen tag and two frozen rule files are unchanged.
- [ ] `GetEffectiveRuntimeStatus` has no independent decision logic.
- [ ] Stage02 compiler, not service/UI, stamps runtime decisions.
- [ ] Stage02 blockers and write gate are unchanged.
- [ ] Stage03 runtime fields and scan fields coexist in Data Tree and fields JSON.
- [ ] Stage03 Strict/Force tests retain previous results.
- [ ] Component GUIDs and port counts are unchanged.
- [ ] Fresh Python and .NET full suites pass.
- [ ] Release build reports 0 warning / 0 error.
- [ ] Artifact manifest binds the exact GHA hash and byte count to final HEAD.
- [ ] Deployed GHA hash equals the candidate hash.
- [ ] Deployment directory contains one active fixed-name GHA and zero `.bak` / `.backup` / `.pending` copies.
- [ ] Revit/Rhino were closed during replacement, so the next launch can load the new bytes.
