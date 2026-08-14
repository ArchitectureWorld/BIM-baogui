# Revit Native MCP v0.4.2 Completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete the existing v0.4.2 branch from `2d45d172` through deterministic Preview V2, atomic Assignment readback, shared workbench/MCP semantics, Stage03 export-GUID owners, unified 0.4.2 packaging, green Windows CI, and the verified `BIMBaoGui-Revit2020-Native-MCP-v0.4.2` artifact.

**Architecture:** Keep WPF and MCP as adapters over the same Stage02 request, preview, write, and readback policies. Freeze every write-affecting semantic input into Preview V2 canonical JSON; persist manual assignments in RVT Extensible Storage; resolve Stage03 object owners through Revit export GUIDs and the existing H-IFC `GLOBAL_ID` contract. Preserve the existing 42 commits and add focused TDD commits without rebasing or rewriting history.

**Tech Stack:** C#/.NET Framework 4.8, Autodesk Revit 2020 API, WPF, .NET 8 MCP server, xUnit, Python 3/pytest, PowerShell, GitHub Actions Windows runner.

## Global Constraints

- Target branch: `feat/revit-stage02-manual-semantic-v0.4.2`.
- Starting implementation commit: `ef20066ed53b73483cd568cf2c898ed7817e64af`.
- Revit target: Autodesk Revit 2020 only.
- Shipped product version: `0.4.2`; assembly/file version: `0.4.2.0`.
- Assignment schema remains `HBR_STAGE02_ASSIGNMENTS_V1` / `1.0.0`.
- Preview schema is `HBR_NATIVE_STAGE02_PREVIEW_V2`.
- Atomicity is per Revit element; a batch may report partial success.
- The dirty `main` checkout and unrelated worktrees must not be modified.
- No history rewrite, squash, rebase, force-push, temporary workflow, user RVT/IFC, build output, log, or ZIP is committed.
- MCP cannot bypass carrier policy, Stage01 conditions, preview freshness, parameter contracts, transactions, or readback.
- Stage03 must not guess an IFC owner or fall back to the unique `IfcSite` for green objects.
- Automated evidence must not be described as Revit 2020 or IFCFlux host acceptance without real host evidence.

---

### Task 1: Repair the known v0.4.2 baseline gate

**Files:**
- Modify: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage02RuleCatalogTests.cs`
- Modify: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage02ManualRoleCatalogTests.cs`

**Interfaces:**
- Consumes: embedded base rule pack plus `hbr_rule_source.v0.4.2-overlay.json`.
- Produces: a green native-domain baseline that verifies the manual-only green role and four object fields without analyzer warnings.

- [ ] **Step 1: Re-run the existing failing test as the RED proof**

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release --nologo `
  --filter "FullyQualifiedName~NativeStage02RuleCatalogTests.LoadsAllCarrierRolesAndPropertiesFromTheEmbeddedDatabase"
```

Expected: FAIL with `Expected: 14`, `Actual: 15`.

- [ ] **Step 2: Replace the stale contract and analyzer-warning pattern**

Use the v0.4.2 merged catalog contract:

```csharp
Assert.Equal(15, catalog.CarrierRolesById.Count);
Assert.True(catalog.CarrierRolesById.ContainsKey("SITE_GREEN_OBJECT"));
Assert.Equal(363, catalog.PropertiesById.Count);
Assert.Equal(4, catalog.PropertiesForRole("SITE_GREEN_OBJECT").Count);
```

Replace the `Where` followed by `Assert.Single` pattern with the xUnit predicate overload:

```csharp
NativeStage02ManualRoleContract green = Assert.Single(
  catalog.Roles,
  value => value.RoleId == "SITE_GREEN_OBJECT");
```

- [ ] **Step 3: Run the complete native domain suite**

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release --nologo
```

Expected: 149 tests pass, zero failures, zero xUnit analyzer warnings.

- [ ] **Step 4: Commit the baseline repair**

```powershell
git add tests/BIMBaoGui.RevitAddin.Tests/NativeStage02RuleCatalogTests.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage02ManualRoleCatalogTests.cs
git commit -m "test(stage02): align v0.4.2 rule catalog contract"
```

---

### Task 2: Complete Preview V2 role projection, canonical JSON, and hash

**Files:**
- Modify: `src/BIMBaoGui.RevitAddin/Stage02/NativeStage02PreviewCompiler.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage02/NativeStage02PreviewModels.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage02/NativeStage02RevitService.cs`
- Modify: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage02PreviewCompilerTests.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage02ManualPreviewCompilerTests.cs`

**Interfaces:**
- Consumes: `NativeStage02ElementEvidence.ResolvedRoleMatch`, `AssignmentMode`, `AssignmentSource`, `AssignmentAction`, `ManualCarrierEvidence`, and `NativeStage02PreviewInput` semantic inputs.
- Produces: `NativeStage02PreviewCompiler.Compile(NativeStage02PreviewInput, NativeStage02RuleCatalog)` returning a complete `HBR_NATIVE_STAGE02_PREVIEW_V2` with deterministic `CanonicalJson` and lowercase SHA-256 `PreviewHash`.

- [ ] **Step 1: Write failing semantic-hash tests**

Add behavior tests with literal expected fragments:

```csharp
[Fact]
public void Manual_role_state_is_frozen_into_preview_v2_and_hash()
{
  NativeStage02Preview preview = CompileManual(
    bulkRoleId: "SITE_GREEN_OBJECT",
    overrideRoleId: NativeStage02RoleAssignmentPolicy.AutoOverrideRoleId);

  Assert.Equal("HBR_NATIVE_STAGE02_PREVIEW_V2", preview.SchemaVersion);
  Assert.Contains("\"identificationMode\":\"Manual\"", preview.CanonicalJson);
  Assert.Contains("\"bulkRoleId\":\"SITE_GREEN_OBJECT\"", preview.CanonicalJson);
  Assert.Contains("\"assignmentAction\":\"SaveManualAssignment\"", preview.CanonicalJson);
  Assert.Matches("^[0-9a-f]{64}$", preview.PreviewHash);
}

[Fact]
public void Changing_a_row_override_changes_the_preview_hash()
{
  NativeStage02Preview manual = CompileManual("SITE_GREEN_OBJECT", "SITE_GREEN_OBJECT");
  NativeStage02Preview automatic = CompileManual(
    "SITE_GREEN_OBJECT",
    NativeStage02RoleAssignmentPolicy.AutoOverrideRoleId);

  Assert.NotEqual(manual.PreviewHash, automatic.PreviewHash);
}
```

Also prove that reversing input/override order preserves the hash and that changing only display text does not change the hash after role resolution is frozen.

- [ ] **Step 2: Run Preview V2 tests and verify RED**

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release --nologo `
  --filter "FullyQualifiedName~NativeStage02PreviewCompilerTests|FullyQualifiedName~NativeStage02ManualPreviewCompilerTests"
```

Expected: FAIL because the current compiler rematches automatically, omits semantic inputs from canonical JSON, and does not project Assignment fields into element plans.

- [ ] **Step 3: Project the resolved role and Assignment state**

Use the role already resolved by `NativeStage02RevitService` and fall back only for legacy automatic evidence:

```csharp
NativeStage02RoleMatchResult role = elementEvidence.ResolvedRoleMatch
  ?? NativeStage02RoleMatcher.Match(
    elementEvidence.Element,
    catalog.CarrierRoles,
    input.ModelProfile);
```

Add `AutomaticRoleMatch` to `NativeStage02ElementEvidence`. In
`NativeStage02RevitService`, compute the automatic match before applying current
or persisted manual precedence, then populate both role results and the exact
Assignment decision:

```csharp
NativeStage02RoleMatchResult automaticRole = NativeStage02RoleMatcher.Match(
  snapshot,
  catalog.CarrierRoles,
  modelProfile);
NativeStage02RoleMatchResult effectiveRole = ResolveEffectiveRole(
  snapshot,
  currentAssignment,
  savedAssignment,
  modelProfile,
  conditions,
  catalog);
```

The evidence action is `SaveManualAssignment` for a current manual choice,
`RemoveManualAssignment` for current automatic override over a saved manual
record, `KeepManualAssignment` for an unchanged persisted manual record, and
`None` otherwise. Populate each plan from that evidence:

```csharp
AssignmentMode = elementEvidence.AssignmentMode,
AssignmentSource = elementEvidence.AssignmentSource,
AssignmentAction = elementEvidence.AssignmentAction,
ManualCarrierEvidence = elementEvidence.ManualCarrierEvidence,
AutomaticRoleStatus = elementEvidence.AutomaticRoleMatch.Status,
AutomaticRoleId = elementEvidence.AutomaticRoleMatch.RoleId,
EffectiveRoleId = role.RoleId,
RoleId = role.RoleId,
RoleMatchSource = role.MatchSource
```

Select properties from `role.CandidateRoleIds`; do not rematch manual elements through automatic category aliases.

- [ ] **Step 4: Extend the canonical serializer with exact semantic fields**

Before conditions, write mode, bulk role, and sorted overrides:

```csharp
Property(builder, "identificationMode", preview.IdentificationMode.ToString(), true);
Property(builder, "bulkRoleId", preview.BulkRoleId, true);
builder.Append(",\"roleOverrides\":[");
```

For each element write `automaticRoleStatus`, `automaticRoleId`, `effectiveRoleId`, `assignmentMode`, `assignmentSource`, `assignmentAction`, and `manualCarrierEvidence`. Keep UniqueId, ElementId, category, and element kind. Exclude display name, family/type/level labels, and human message strings from hash input.

- [ ] **Step 5: Run focused and complete native tests**

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release --nologo `
  --filter "FullyQualifiedName~NativeStage02"
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release --nologo
```

Expected: all Stage02 tests and all native tests pass.

- [ ] **Step 6: Commit Preview V2 completion**

```powershell
git add src/BIMBaoGui.RevitAddin/Stage02/NativeStage02PreviewCompiler.cs `
  src/BIMBaoGui.RevitAddin/Stage02/NativeStage02PreviewModels.cs `
  src/BIMBaoGui.RevitAddin/Stage02/NativeStage02RevitService.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage02PreviewCompilerTests.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage02ManualPreviewCompilerTests.cs
git commit -m "feat(stage02): complete preview v2 canonical semantics"
```

---

### Task 3: Complete Assignment create, update, delete, and atomic readback

**Files:**
- Create: `src/BIMBaoGui.RevitAddin/Stage02/NativeStage02SemanticAssignmentWritePolicy.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage02/NativeStage02RevitWriteService.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage02/NativeStage02PreviewModels.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage02SemanticAssignmentWritePolicyTests.cs`
- Modify: `tests/test_revit_addin_stage02_revit_contract.py`

**Interfaces:**
- Consumes: committed `NativeStage02SemanticAssignmentPayload` and one `NativeStage02ElementPlan`.
- Produces: `NativeStage02SemanticAssignmentWritePolicy.Apply(NativeStage02SemanticAssignmentPayload, NativeStage02ElementPlan)`, `Verify(NativeStage02SemanticAssignmentPayload, NativeStage02ElementPlan)`, and write-result counts `AssignedElementCount`, `RemovedAssignmentCount`, `FailedAssignmentCount`.

- [ ] **Step 1: Write failing CRUD and rollback-policy tests**

Define the pure policy contract:

```csharp
NativeStage02SemanticAssignmentPayload created =
  NativeStage02SemanticAssignmentWritePolicy.Apply(empty, SavePlan("A", "SITE_GREEN_OBJECT"));
NativeStage02SemanticAssignmentPayload updated =
  NativeStage02SemanticAssignmentWritePolicy.Apply(created, SavePlan("A", "SITE_FIRE_FIELD"));
NativeStage02SemanticAssignmentPayload removed =
  NativeStage02SemanticAssignmentWritePolicy.Apply(updated, RemovePlan("A"));

Assert.Equal("SITE_GREEN_OBJECT", Assert.Single(created.Assignments).RoleId);
Assert.Equal("SITE_FIRE_FIELD", Assert.Single(updated.Assignments).RoleId);
Assert.Empty(removed.Assignments);
Assert.Empty(empty.Assignments);
```

Add `Verify` cases for matching record, missing record after save, lingering record after remove, wrong role, wrong carrier category, and canonical hash mismatch.

- [ ] **Step 2: Run policy tests and verify RED**

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release --nologo `
  --filter "FullyQualifiedName~NativeStage02SemanticAssignmentWritePolicyTests"
```

Expected: FAIL because the write policy and readback decision types do not exist.

- [ ] **Step 3: Implement immutable Assignment mutation and verification**

Use these exact signatures:

```csharp
internal static NativeStage02SemanticAssignmentPayload Apply(
  NativeStage02SemanticAssignmentPayload committed,
  NativeStage02ElementPlan plan)

internal static NativeStage02SemanticAssignmentReadbackDecision Verify(
  NativeStage02SemanticAssignmentPayload actual,
  NativeStage02ElementPlan plan)
```

`Apply` clones through `Normalize`, upserts manual records for `SaveManualAssignment`, removes records for `RemoveManualAssignment`, and returns a normalized new payload. `Verify` compares role, manual mode, carrier category, carrier element kind, and expected absence after remove; it returns `SEMANTIC_ASSIGNMENT_READBACK_FAILED` on mismatch.

- [ ] **Step 4: Make per-element transactions update committed state only after commit**

Inside `NativeStage02RevitWriteService.Execute`, keep two payload variables:

```csharp
NativeStage02SemanticAssignmentPayload candidatePayload =
  NativeStage02SemanticAssignmentWritePolicy.Apply(assignmentPayload, plan);
NativeStage02SemanticAssignmentStorageSnapshot candidateSnapshot =
  NativeStage02SemanticAssignmentStoragePolicy.CreateSnapshot(candidatePayload);
```

Write `candidateSnapshot` inside the element transaction, call `document.Regenerate()`, reread storage, evaluate it, call `Verify`, and read all written parameters by GUID. Commit only after every check succeeds. Assign `assignmentPayload = candidatePayload` only after `TransactionStatus.Committed`; failed or rolled-back elements must leave the in-memory committed payload unchanged.

An element enters a transaction when it has either a `ValueAction.Set` field or an Assignment save/remove action. Binding failures for its required property block the whole element transaction.

- [ ] **Step 5: Add result counts and contract wiring checks**

Add to `NativeStage02WriteResult`:

```csharp
internal int AssignedElementCount { get; set; }
internal int RemovedAssignmentCount { get; set; }
internal int FailedAssignmentCount { get; set; }
```

Project the same counts through the UI result and MCP result. Update the Python contract to require `NativeStage02SemanticAssignmentWritePolicy.Verify`, post-write storage read, and the stable failure code.

- [ ] **Step 6: Run Stage02 and contract tests**

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release --nologo `
  --filter "FullyQualifiedName~NativeStage02"
python -m pytest tests/test_revit_addin_stage02_revit_contract.py -q
```

Expected: all selected tests pass.

- [ ] **Step 7: Commit atomic Assignment readback**

```powershell
git add src/BIMBaoGui.RevitAddin/Stage02 `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage02SemanticAssignmentWritePolicyTests.cs `
  tests/test_revit_addin_stage02_revit_contract.py
git commit -m "feat(stage02): verify semantic assignments atomically"
```

---

### Task 4: Make the manual workbench use one tested request builder

**Files:**
- Create: `src/BIMBaoGui.RevitAddin/Stage02/NativeStage02WorkbenchRequestPolicy.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage02/NativeStage02View.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage02WorkbenchRequestPolicyTests.cs`
- Modify: `tests/test_revit_addin_stage02_ui_contract.py`

**Interfaces:**
- Consumes: scope, identification mode, selected bulk role, and row override dictionary.
- Produces: `NativeStage02WorkbenchRequestPolicy.Build(NativeStage02ScopeMode, NativeStage02IdentificationMode, string, IReadOnlyDictionary<string, string>)` returning a canonical `NativeStage02PreviewRequest` used by WPF and testable without Revit/WPF automation.

- [ ] **Step 1: Write failing automatic/manual request tests**

```csharp
[Fact]
public void Manual_request_sorts_bulk_and_row_assignments()
{
  NativeStage02PreviewRequest request = NativeStage02WorkbenchRequestPolicy.Build(
    NativeStage02ScopeMode.CustomSelection,
    NativeStage02IdentificationMode.Manual,
    " SITE_GREEN_OBJECT ",
    new Dictionary<string, string>
    {
      ["B"] = "SITE_GREEN_OBJECT",
      ["A"] = NativeStage02RoleAssignmentPolicy.AutoOverrideRoleId
    });

  Assert.Equal("SITE_GREEN_OBJECT", request.BulkRoleId);
  Assert.Equal(new[] { "A", "B" },
    request.RoleOverrides.Select(value => value.ElementUniqueId).ToArray());
}
```

Also assert that automatic mode emits an empty bulk role and no overrides, and full-model mode forces automatic mode.

- [ ] **Step 2: Run workbench tests and verify RED**

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release --nologo `
  --filter "FullyQualifiedName~NativeStage02WorkbenchRequestPolicyTests"
```

Expected: FAIL because the request policy does not exist.

- [ ] **Step 3: Implement and wire the request builder**

Use this signature:

```csharp
internal static NativeStage02PreviewRequest Build(
  NativeStage02ScopeMode scope,
  NativeStage02IdentificationMode identificationMode,
  string bulkRoleId,
  IReadOnlyDictionary<string, string> overrides)
```

Return ordinal-sorted, trimmed overrides. Update `NativeStage02View.RequestPreview()` to call this policy instead of duplicating construction logic. Keep the existing continuous scroll, automatic/manual radio buttons, bulk role selector, per-row override editor, automatic sentinel, and preview invalidation behavior.

- [ ] **Step 4: Verify UI and workbench contracts**

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release --nologo `
  --filter "FullyQualifiedName~NativeStage02WorkbenchRequestPolicyTests|FullyQualifiedName~NativeStage02RoleAssignmentPolicyTests"
python -m pytest tests/test_revit_addin_stage02_ui_contract.py -q
```

Expected: all selected tests pass and the UI contract confirms request-policy wiring.

- [ ] **Step 5: Commit the workbench adapter**

```powershell
git add src/BIMBaoGui.RevitAddin/Stage02/NativeStage02WorkbenchRequestPolicy.cs `
  src/BIMBaoGui.RevitAddin/Stage02/NativeStage02View.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage02WorkbenchRequestPolicyTests.cs `
  tests/test_revit_addin_stage02_ui_contract.py
git commit -m "feat(stage02): unify automatic and manual workbench requests"
```

---

### Task 5: Synchronize the MCP controlled Stage02 entry

**Files:**
- Modify: `src/BIMBaoGui.McpContracts/ToolContracts.cs`
- Modify: `src/BIMBaoGui.McpServer/BimBaoGuiTools.cs`
- Modify: `src/BIMBaoGui.RevitAddin/McpBridge/McpBridgeCommandRouter.cs`
- Modify: `src/BIMBaoGui.RevitAddin/McpBridge/McpStage02Adapter.cs`
- Modify: `tests/BIMBaoGui.McpContracts.Tests/BridgeFrameCodecTests.cs`
- Create: `tests/test_revit_addin_mcp_stage02_contract.py`
- Modify: `tools/BIMBaoGui.McpSmoke/Program.cs`

**Interfaces:**
- Consumes: `Stage02PreviewCommand` with scope, identification mode, bulk role, and typed row overrides.
- Produces: existing tools `bimbaogui_stage02_preview` and `bimbaogui_stage02_write` with Preview V2 semantic output; approved tool count remains 13.

- [ ] **Step 1: Write failing MCP DTO and surface tests**

Extend the public contract expectation:

```csharp
var command = new Stage02PreviewCommand
{
  Scope = "current_selection",
  IdentificationMode = "manual",
  BulkRoleId = "SITE_GREEN_OBJECT",
  RoleOverrides = new[]
  {
    new Stage02RoleOverrideCommand
    {
      ElementUniqueId = "A",
      RoleId = "SITE_GREEN_OBJECT"
    }
  }
};
```

The Python contract must assert the same four inputs reach `McpStage02Adapter.PreviewAsync`, and that preview output includes `schema_version`, `canonical_json`, `identification_mode`, `bulk_role_id`, `automatic_role_id`, `effective_role_id`, `assignment_mode`, `assignment_source`, `assignment_action`, and `manual_carrier_evidence`.

- [ ] **Step 2: Run MCP tests and verify RED**

```powershell
dotnet test tests/BIMBaoGui.McpContracts.Tests/BIMBaoGui.McpContracts.Tests.csproj `
  -c Release --nologo
python -m pytest tests/test_revit_addin_mcp_contract.py `
  tests/test_revit_addin_mcp_stage02_contract.py -q
```

Expected: FAIL because semantic DTO fields and bridge projection are missing.

- [ ] **Step 3: Add typed DTOs and route them through the existing tool**

Add:

```csharp
public sealed class Stage02RoleOverrideCommand
{
  public string ElementUniqueId { get; set; } = string.Empty;
  public string RoleId { get; set; } = string.Empty;
}
```

Extend `Stage02PreviewCommand` with:

```csharp
public string IdentificationMode { get; set; } = "automatic";
public string BulkRoleId { get; set; } = string.Empty;
public IReadOnlyList<Stage02RoleOverrideCommand> RoleOverrides { get; set; } =
  Array.Empty<Stage02RoleOverrideCommand>();
```

Extend `Stage02PreviewCommand`, the MCP tool parameters, router payload, and `McpStage02Adapter.PreviewAsync`. Map external `automatic`/`manual` to `NativeStage02IdentificationMode`, reject any other value with `INVALID_ARGUMENT`, and build a `NativeStage02PreviewRequest` through the same role and carrier policies as WPF.

- [ ] **Step 4: Project Preview V2 and write-result Assignment evidence**

Return canonical schema/hash and all per-element semantic fields. Add `assigned_element_count`, `removed_assignment_count`, and `failed_assignment_count` to write output. Keep lease consumption one-time and keep `confirm=true` mandatory.

- [ ] **Step 5: Run contracts, SDK smoke build, and native MCP wiring tests**

```powershell
dotnet test tests/BIMBaoGui.McpContracts.Tests/BIMBaoGui.McpContracts.Tests.csproj `
  -c Release --nologo
dotnet build tools/BIMBaoGui.McpSmoke/BIMBaoGui.McpSmoke.csproj `
  -c Release --nologo -p:TreatWarningsAsErrors=true
python -m pytest tests/test_revit_addin_mcp_contract.py `
  tests/test_revit_addin_mcp_stage02_contract.py -q
```

Expected: all commands pass; `McpToolNames.Approved` still contains exactly 13 tools.

- [ ] **Step 6: Commit MCP parity**

```powershell
git add src/BIMBaoGui.McpContracts/ToolContracts.cs `
  src/BIMBaoGui.McpServer/BimBaoGuiTools.cs `
  src/BIMBaoGui.RevitAddin/McpBridge `
  tests/BIMBaoGui.McpContracts.Tests/BridgeFrameCodecTests.cs `
  tests/test_revit_addin_mcp_stage02_contract.py `
  tools/BIMBaoGui.McpSmoke/Program.cs
git commit -m "feat(mcp): expose controlled Stage02 semantic assignments"
```

---

### Task 6: Make Stage03 consume green-object owners by export GUID

**Files:**
- Create: `src/BIMBaoGui.RevitAddin/Stage03/NativeStage03ExportGuidOwnerPolicy.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage03/NativeStage03Scanner.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage03/NativeStage03Models.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage03/NativeStage03ReportWriter.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage03ExportGuidOwnerPolicyTests.cs`
- Modify: `tests/BIMBaoGui.HifcCore.Tests/HifcCoreServiceTests.cs`
- Modify: `tests/test_revit_addin_stage03_revit_contract.py`

**Interfaces:**
- Consumes: validated `SITE_GREEN_OBJECT` element plan, rule owner strategy `BY_EXPORT_GUID`, and `ExportUtils.GetExportId` result.
- Produces: `NativeStage03ExportGuidOwnerPolicy.Resolve(string, string, Guid)` mapping to H-IFC `GLOBAL_ID`, plus report evidence `OwnerExportGuid`, `OwnerGlobalId`, and `OwnerResolutionStatus`.

- [ ] **Step 1: Write failing export-GUID policy tests**

```csharp
[Fact]
public void By_export_guid_maps_to_global_id_owner_contract()
{
  Guid exportGuid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
  NativeStage03ExportGuidOwnerDecision decision =
    NativeStage03ExportGuidOwnerPolicy.Resolve(
      "BY_EXPORT_GUID",
      "IfcSlab",
      exportGuid);

  Assert.True(decision.Success);
  Assert.Equal(HifcOwnerStrategies.GlobalId, decision.HifcOwnerStrategy);
  Assert.Equal(exportGuid.ToString("D"), decision.ExportGuid);
  Assert.Equal(IfcGlobalId.Encode(exportGuid), decision.OwnerGlobalId);
  Assert.Equal("OWNER_GUID_READY", decision.Status);
}
```

Add empty GUID, unsupported strategy, and empty entity tests. Add H-IFC fixture tests proving zero matching owners returns `IFC_OWNER_NOT_FOUND` and duplicate entity+GlobalId returns `IFC_OWNER_CONFLICT`. `OWNER_ENTITY_MATCH` is asserted only after RAW IFC inspection, never from the Revit-side GUID projection alone.

- [ ] **Step 2: Run Stage03/H-IFC tests and verify RED**

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release --nologo `
  --filter "FullyQualifiedName~NativeStage03ExportGuidOwnerPolicyTests"
dotnet test tests/BIMBaoGui.HifcCore.Tests/BIMBaoGui.HifcCore.Tests.csproj `
  -c Release --nologo `
  --filter "FullyQualifiedName~ExportGuid"
```

Expected: FAIL because the policy and exact green-object owner tests do not exist.

- [ ] **Step 3: Implement owner-strategy normalization and scanner integration**

Use:

```csharp
internal static NativeStage03ExportGuidOwnerDecision Resolve(
  string ruleOwnerStrategy,
  string ownerEntity,
  Guid exportGuid)
```

Only `BY_EXPORT_GUID` is accepted by this policy. In `NativeStage03Scanner.BuildField`, call `ExportUtils.GetExportId(document, owner.Id)`, pass the result to the policy, then populate `HifcFieldRequest.OwnerStrategy = HifcOwnerStrategies.GlobalId` and `OwnerGlobalId` from the decision. Never route `BY_EXPORT_GUID` into the existing unsupported-strategy branch.

- [ ] **Step 4: Freeze and report exact owner evidence**

Add `OwnerExportGuid` and `OwnerResolutionStatus` to field evidence, Stage03 canonical JSON, WPF/MCP projections, and JSON reports. Require the same owner entity and GlobalId in RAW and final inspection; propagate existing H-IFC `IFC_OWNER_NOT_FOUND` and `IFC_OWNER_CONFLICT` without fallback.

- [ ] **Step 5: Run Stage03, H-IFC, and Python contracts**

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release --nologo `
  --filter "FullyQualifiedName~NativeStage03"
dotnet test tests/BIMBaoGui.HifcCore.Tests/BIMBaoGui.HifcCore.Tests.csproj `
  -c Release --nologo
python -m pytest tests/test_revit_addin_stage03_revit_contract.py `
  tests/test_revit_addin_mcp_stage03_contract.py -q
```

Expected: all selected tests pass.

- [ ] **Step 6: Commit Stage03 export-GUID ownership**

```powershell
git add src/BIMBaoGui.RevitAddin/Stage03 `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage03ExportGuidOwnerPolicyTests.cs `
  tests/BIMBaoGui.HifcCore.Tests/HifcCoreServiceTests.cs `
  tests/test_revit_addin_stage03_revit_contract.py
git commit -m "feat(stage03): consume assigned green object owners"
```

---

### Task 7: Upgrade every shipped surface and installer to 0.4.2

**Files:**
- Modify: `src/BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.csproj`
- Modify: `src/BIMBaoGui.HifcCore/BIMBaoGui.HifcCore.csproj`
- Modify: `src/BIMBaoGui.McpContracts/BIMBaoGui.McpContracts.csproj`
- Modify: `src/BIMBaoGui.McpServer/BIMBaoGui.McpServer.csproj`
- Modify: `installer/Install-Revit2020.ps1`
- Modify: `installer/McpProbe.cmd`
- Modify: `installer/mcp-server-config.example.json`
- Modify: `docs/revit-addin/README.md`
- Modify: `.github/workflows/build-revit-mcp.yml`
- Create: `tests/test_revit_addin_v042_contract.py`
- Modify: `tests/test_revit_addin_mcp_installer_contract.py`
- Modify: `tests/test_revit_addin_mcp_non_regression.py`
- Create: `tools/build_revit_functional_baseline.py`
- Create: `specs/revit-addin/v0.4.2-functional-baseline.json`

**Interfaces:**
- Consumes: final production source tree after Tasks 1–6.
- Produces: unified assembly identity `0.4.2.0`, versioned installer paths, deterministic v0.4.2 source baseline, and workflow artifact `BIMBaoGui-Revit2020-Native-MCP-v0.4.2`.

- [ ] **Step 1: Write the v0.4.2 release contract and verify RED**

The new Python contract asserts these literal values:

```python
for project in PRODUCT_PROJECTS:
    text = project.read_text(encoding="utf-8")
    assert "<Version>0.4.2</Version>" in text
    assert "<FileVersion>0.4.2.0</FileVersion>" in text
    assert "<AssemblyVersion>0.4.2.0</AssemblyVersion>" in text

assert '$mcpVersion = "0.4.2"' in INSTALLER.read_text(encoding="utf-8-sig")
assert "BIMBaoGui-Revit2020-Native-MCP-v0.4.2" in WORKFLOW.read_text(encoding="utf-8")
```

Run:

```powershell
python -m pytest tests/test_revit_addin_v042_contract.py `
  tests/test_revit_addin_mcp_installer_contract.py -q
```

Expected: FAIL on current 0.4.1 identities and paths.

- [ ] **Step 2: Update product, installer, documentation, and workflow identities**

Set all four product projects to:

```xml
<Version>0.4.2</Version>
<FileVersion>0.4.2.0</FileVersion>
<AssemblyVersion>0.4.2.0</AssemblyVersion>
```

Replace controlled MCP path/version assertions with `0.4.2`, update smoke cleanup to seed both `0.4.0` and `0.4.1` superseded directories, and set the upload artifact name exactly to `BIMBaoGui-Revit2020-Native-MCP-v0.4.2`.

- [ ] **Step 3: Add and test a deterministic functional-baseline builder**

The script accepts `--version 0.4.2`, hashes every tracked `*.cs` file beneath the four production roots, and writes sorted keys plus `source_snapshot_sha256`. Test determinism by generating twice into two temporary files and comparing bytes.

Run the builder once after production source is frozen:

```powershell
python tools/build_revit_functional_baseline.py `
  --version 0.4.2 `
  --branch feat/revit-stage02-manual-semantic-v0.4.2 `
  --output specs/revit-addin/v0.4.2-functional-baseline.json
```

Update `test_revit_addin_mcp_non_regression.py` to use this manifest without a branch skip.

- [ ] **Step 4: Run release contracts and assembly identity tests**

```powershell
python -m pytest tests/test_revit_addin_v042_contract.py `
  tests/test_revit_addin_mcp_installer_contract.py `
  tests/test_revit_addin_mcp_non_regression.py -q
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release --nologo `
  --filter "FullyQualifiedName~PluginRuntimeIdentityTests"
```

Expected: all selected tests pass.

- [ ] **Step 5: Commit release identity and frozen baseline**

```powershell
git add src/BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.csproj `
  src/BIMBaoGui.HifcCore/BIMBaoGui.HifcCore.csproj `
  src/BIMBaoGui.McpContracts/BIMBaoGui.McpContracts.csproj `
  src/BIMBaoGui.McpServer/BIMBaoGui.McpServer.csproj `
  installer docs/revit-addin/README.md .github/workflows/build-revit-mcp.yml `
  tests/test_revit_addin_v042_contract.py `
  tests/test_revit_addin_mcp_installer_contract.py `
  tests/test_revit_addin_mcp_non_regression.py `
  tools/build_revit_functional_baseline.py `
  specs/revit-addin/v0.4.2-functional-baseline.json
git commit -m "chore: release Revit native product v0.4.2"
```

---

### Task 8: Run full Windows verification, installer smoke, remote CI, and artifact verification

**Files:**
- Modify only if a verification command exposes a reproducible defect; each defect requires a new failing regression test before production changes.
- Do not commit: `bin/`, `obj/`, `TestResults/`, `artifacts/`, downloaded Actions artifacts, logs, or ZIP files.

**Interfaces:**
- Consumes: completed local branch from Tasks 1–7.
- Produces: local verification logs read in-session, remote commits on the same branch, successful Windows workflows, and a downloaded/verified artifact directory named `BIMBaoGui-Revit2020-Native-MCP-v0.4.2`.

- [ ] **Step 1: Run all Python contracts and rule-pack tests**

```powershell
$env:PYTEST_DISABLE_PLUGIN_AUTOLOAD='1'
$env:PYTHONDONTWRITEBYTECODE='1'
python -m pytest tests -q
```

Expected: zero failures and zero errors.

- [ ] **Step 2: Run every .NET test project in Release**

```powershell
dotnet test tests/BIMBaoGui.HifcCore.Tests/BIMBaoGui.HifcCore.Tests.csproj -c Release --nologo
dotnet test tests/BIMBaoGui.McpContracts.Tests/BIMBaoGui.McpContracts.Tests.csproj -c Release --nologo
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj -c Release --nologo
dotnet test tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj -c Release --nologo
```

Expected: every project reports zero failed and zero skipped tests.

- [ ] **Step 3: Build all shipped products with warnings as errors**

```powershell
$commit = git rev-parse HEAD
dotnet build src/BIMBaoGui.HifcCore/BIMBaoGui.HifcCore.csproj -c Release --nologo -p:TreatWarningsAsErrors=true
dotnet build src/BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.csproj -c Release --nologo -p:TreatWarningsAsErrors=true -p:HbrBuildNumber=local -p:HbrCommitSha=$commit
dotnet build src/BIMBaoGui.McpServer/BIMBaoGui.McpServer.csproj -c Release --nologo -p:TreatWarningsAsErrors=true
dotnet build tools/BIMBaoGui.McpSmoke/BIMBaoGui.McpSmoke.csproj -c Release --nologo -p:TreatWarningsAsErrors=true
```

Expected: zero warnings and zero errors.

- [ ] **Step 4: Reproduce the workflow package and isolated install/uninstall smoke locally**

Publish and assemble the workflow layout under a task-specific temporary root:

```powershell
$commit = git rev-parse HEAD
$releaseRoot = Join-Path $env:TEMP "BIMBaoGui-v042-local-release"
$artifactRoot = Join-Path $releaseRoot "BIMBaoGui-Revit2020-Native-MCP-v0.4.2"
$addinPayload = Join-Path $artifactRoot "BIMBaoGui.RevitAddin"
$mcpPayload = Join-Path $artifactRoot "BIMBaoGui.McpServer"
New-Item -ItemType Directory -Force -Path $addinPayload,$mcpPayload | Out-Null
dotnet publish src/BIMBaoGui.McpServer/BIMBaoGui.McpServer.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:PublishTrimmed=false -p:TreatWarningsAsErrors=true
Copy-Item src/BIMBaoGui.RevitAddin/bin/Release/net48/BIMBaoGui.RevitAddin.dll $addinPayload
Copy-Item src/BIMBaoGui.RevitAddin/bin/Release/net48/BIMBaoGui.McpContracts.dll $addinPayload
Copy-Item src/BIMBaoGui.RevitAddin/bin/Release/net48/BIMBaoGui.HifcCore.dll $addinPayload
Copy-Item src/BIMBaoGui.McpServer/bin/Release/net8.0/win-x64/publish/BIMBaoGui.McpServer.exe $mcpPayload
Copy-Item installer/Install.cmd,installer/Uninstall.cmd,installer/McpProbe.cmd,installer/Install-Revit2020.ps1,installer/BIMBaoGui.RevitAddin.addin,installer/mcp-server-config.example.json $artifactRoot
Copy-Item docs/revit-addin/README.md (Join-Path $artifactRoot "README.md")
```

Then set `$env:APPDATA` and `$env:LOCALAPPDATA` to subdirectories of
`$releaseRoot`, run `Install-Revit2020.ps1 -SourceRoot $artifactRoot -Force`,
verify absolute manifest/config paths, all four assembly versions, and
source/installed SHA-256 equality. Run the installed MCP executable with
`--probe` and require exit code 2 plus `REVIT_NOT_CONNECTED`; run
`Install-Revit2020.ps1 -Uninstall -Force` and assert the manifest, controlled
add-in directory, `McpServer\0.4.2`, and generated config no longer exist.
Restore both environment variables and remove `$releaseRoot` in `finally`.

Expected: install and uninstall complete without touching the user's real Revit add-in directories.

- [ ] **Step 5: Verify repository cleanliness before push**

```powershell
git diff --check
git status --short
git ls-files | rg '(^|/)(artifacts|bin|obj|TestResults|logs|tmp)/|\.zip$|\.log$|\.tmp$|\.bak$'
git ls-files .github/workflows
```

Expected: no tracked build output or temporary workflow; only intended committed workflows are listed; worktree is clean.

- [ ] **Step 6: Reconcile the real remote tip and push the same branch**

```powershell
git fetch origin
git rev-list --left-right --count HEAD...origin/feat/revit-stage02-manual-semantic-v0.4.2
git push origin feat/revit-stage02-manual-semantic-v0.4.2
```

If the count shows remote-only commits, stop before push, inspect those commits, integrate without rewriting either history, and rerun Steps 1–5.

- [ ] **Step 7: Wait for both Windows workflows and inspect failures**

```powershell
gh run list --repo ArchitectureWorld/BIM-baogui `
  --branch feat/revit-stage02-manual-semantic-v0.4.2 `
  --limit 10
```

Require `Build BIMBaoGui Revit MCP` and `Build BIMBaoGui GHA` to finish with `success`. For any failure, resolve its numeric ID and inspect it:

```powershell
$failed = gh run list --repo ArchitectureWorld/BIM-baogui `
  --branch feat/revit-stage02-manual-semantic-v0.4.2 `
  --status failure --limit 1 --json databaseId | ConvertFrom-Json
gh run view $failed.databaseId --repo ArchitectureWorld/BIM-baogui --log-failed
```

Write a failing regression test, fix the cause, rerun local gates, and push a focused fix commit.

- [ ] **Step 8: Download and verify the final artifact**

```powershell
$run = gh run list --repo ArchitectureWorld/BIM-baogui `
  --workflow "Build BIMBaoGui Revit MCP" `
  --branch feat/revit-stage02-manual-semantic-v0.4.2 `
  --status success --limit 1 --json databaseId | ConvertFrom-Json
gh run download $run.databaseId --repo ArchitectureWorld/BIM-baogui `
  --name BIMBaoGui-Revit2020-Native-MCP-v0.4.2 `
  --dir "$env:TEMP\BIMBaoGui-Revit2020-Native-MCP-v0.4.2"
```

Verify every `SHA256SUMS.txt` entry, assembly version `0.4.2.0`, installer evidence version `0.4.2`, single-file MCP executable, and required layout. Report the absolute verified artifact directory and GitHub run URL.

- [ ] **Step 9: Record final evidence without claiming unavailable host acceptance**

Report commit SHA, local test counts, build warning/error counts, installer smoke result, remote workflow run IDs, artifact hashes, and remaining `Revit 2020 / IFCFlux manual pending` boundary. Do not merge to `main` unless the user separately authorizes integration.
