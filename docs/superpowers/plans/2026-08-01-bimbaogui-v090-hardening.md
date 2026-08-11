# BIMBaoGui v0.9.0 Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce a test-first Revit 2020 GHA that fixes the confirmed cross-component contracts and is ready for a real official H-IFC export acceptance run.

**Architecture:** Extract deterministic policies into Revit-free net48 classes and keep Autodesk API code as thin adapters. The Revit transaction group remains the write boundary. `HBR_FileContext` becomes schema 0.9.0 because official-protocol compatibility and live payload identity become hashed readiness inputs.

**Tech Stack:** C# net48, xUnit, Autodesk Revit 2020 API, Grasshopper/Rhino.Inside.Revit, Python pytest contract checks, GitHub Actions.

---

### Task 1: Make payload application atomic

**Files:**
- Modify: `tests/BIMBaoGui.Stage01.Core.Tests/Stage01PayloadCodecTests.cs`
- Modify: `src/BIMBaoGui.Stage01/Core/Stage01PayloadCodec.cs`

- [ ] **Step 1: Write the failing regression test**

Add a test that seeds all four collections, applies a payload whose planning-target operator is `INVALID`, asserts `TryApply` is false, and asserts the seeded value, condition, target, and organization all remain unchanged.

```csharp
[Fact]
public void InvalidPayload_DoesNotMutateExistingModel()
{
  var model = new Stage01Model();
  model.SetValue("sentinel", "KEEP_ME");
  model.SetCondition("condition", true);
  AddTarget(model, PlanningTargetCatalog.FloorAreaRatioCode,
    PlanningTargetOperator.LessOrEqual, "2.00");
  model.CurrentOrganization["name"] = "KEEP_ORG";
  string payload = "{\"values\":{\"partial\":\"APPLIED\"},"
    + "\"planningTargets\":{\"x\":{\"operator\":\"INVALID\","
    + "\"value1\":\"1\",\"value2\":\"\",\"unit\":\"Ratio\","
    + "\"source\":\"test\"}},\"conditions\":{},\"organizations\":[]}";

  Assert.False(Stage01PayloadCodec.TryApply(payload, model, out _));
  Assert.Equal("KEEP_ME", model.GetValue("sentinel"));
  Assert.Equal(string.Empty, model.GetValue("partial"));
  Assert.True(model.GetCondition("condition"));
  Assert.NotNull(model.GetPlanningTarget(PlanningTargetCatalog.FloorAreaRatioCode));
  Assert.Equal("KEEP_ORG", model.CurrentOrganization["name"]);
}
```

- [ ] **Step 2: Run the test and verify RED**

```powershell
dotnet test tests\BIMBaoGui.Stage01.Core.Tests\BIMBaoGui.Stage01.Core.Tests.csproj -c Release --filter InvalidPayload_DoesNotMutateExistingModel
```

Expected: FAIL because `sentinel` is cleared and `partial` is present.

- [ ] **Step 3: Parse into a temporary model and swap only after success**

Deserialize and populate a local `Stage01Model parsed`; call `RestoreLegacyPlanningTargets(parsed)`; only then replace the caller's four collections. Preserve caller UI state and confirmation flags.

- [ ] **Step 4: Verify GREEN and commit**

```powershell
dotnet test tests\BIMBaoGui.Stage01.Core.Tests\BIMBaoGui.Stage01.Core.Tests.csproj -c Release
git add src/BIMBaoGui.Stage01/Core/Stage01PayloadCodec.cs tests/BIMBaoGui.Stage01.Core.Tests/Stage01PayloadCodecTests.cs
git commit -m "fix: apply Stage01 payloads atomically"
```

### Task 2: Require an in-session Stage03 rising edge

**Files:**
- Create: `src/BIMBaoGui.Stage01/Core/ExplicitExecutionGate.cs`
- Create: `tests/BIMBaoGui.Stage01.Core.Tests/ExplicitExecutionGateTests.cs`
- Modify: `tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj`
- Modify: `src/BIMBaoGui.Stage01/Stage03OfficialHifcWriteComponent.cs`

- [ ] **Step 1: Write failing gate tests**

Tests must assert: first sample `true` does not fire; first sample `false` does not fire; subsequent false→true fires once; repeated true does not fire; true→false→true fires again.

- [ ] **Step 2: Verify RED**

Expected: compile failure because `ExplicitExecutionGate` does not exist.

- [ ] **Step 3: Implement the minimal gate**

```csharp
internal sealed class ExplicitExecutionGate
{
  private bool _sampled;
  private bool _previous;

  public bool Observe(bool current)
  {
    if (!_sampled)
    {
      _sampled = true;
      _previous = current;
      return false;
    }
    bool fire = current && !_previous;
    _previous = current;
    return fire;
  }
}
```

Replace `_lastExecute` with one gate instance and enqueue only when `Observe(execute)` returns true.

- [ ] **Step 4: Verify and commit**

Run the full .NET suite, then commit as `fix: require explicit Stage03 execution edge`.

### Task 3: Share official aliases only on the same Revit carrier

**Files:**
- Create: `src/BIMBaoGui.Stage01/Hifc/OfficialSourceAliasPolicy.cs`
- Create: `tests/BIMBaoGui.Stage01.Core.Tests/OfficialSourceAliasPolicyTests.cs`
- Modify: `src/BIMBaoGui.Stage01/Hifc/OfficialHifcMappingCatalog.cs`
- Modify: `src/BIMBaoGui.Stage01/Revit/OfficialParameterProjectionService.cs`
- Modify: test project compile links

- [ ] **Step 1: Write failing identity tests**

Assert that mappings with the same binding scope, category, carrier, and official name produce the same identity even when property sets differ. Assert that changing category, carrier, binding scope, or official name changes the identity.

- [ ] **Step 2: Verify RED**

Expected: compile failure because `OfficialSourceAliasPolicy` does not exist.

- [ ] **Step 3: Implement stable identity**

```csharp
public static string BuildIdentityKey(OfficialHifcMapping mapping)
{
  if (mapping == null) throw new ArgumentNullException(nameof(mapping));
  return Normalize(mapping.BindingScope) + "|"
    + Normalize(mapping.Category) + "|"
    + Normalize(mapping.Carrier) + "|"
    + (mapping.OfficialSourceParameterName ?? string.Empty).Trim();
}
```

Use this key for `OfficialSourceParameterGuid`. Remove property-set-based ambiguity rejection. Keep preflight grouping by target element and GUID: identical values collapse to one official write; distinct ordinal values throw `OFFICIAL_SOURCE_VALUE_CONFLICT` with every affected property-set/property name.

- [ ] **Step 4: Add catalog regression coverage**

Assert the two `备注` mappings share one official GUID, the two `建筑物编码` mappings share another, and the two pairs do not share each other's GUID.

- [ ] **Step 5: Verify and commit**

Run the full .NET and Python suites, then commit as `fix: share official aliases by Revit carrier`.

### Task 4: Enforce full parameter type semantics

**Files:**
- Create: `src/BIMBaoGui.Stage01/Hifc/OfficialParameterTypeContract.cs`
- Create: `tests/BIMBaoGui.Stage01.Core.Tests/OfficialParameterTypeContractTests.cs`
- Modify: `src/BIMBaoGui.Stage01/Revit/OfficialParameterProjectionService.cs`
- Modify: test project compile links

- [ ] **Step 1: Write failing type-contract tests**

Cover every supported type and assert `LENGTH` is incompatible with `Number` although both use Double storage. Also assert case-insensitive `LENGTH`/`Length` compatibility and rejection of an unknown type.

- [ ] **Step 2: Verify RED**

Expected: compile failure because the contract does not exist.

- [ ] **Step 3: Implement the pure semantic contract**

Normalize to the eight supported uppercase names and expose:

```csharp
public static bool IsCompatible(string expectedSharedParameterType,
  string actualRevitParameterType)
```

The Revit adapter passes `parameter.Definition.ParameterType.ToString()` after its StorageType check. Error text includes parameter name, expected semantic type, and actual semantic type.

- [ ] **Step 4: Use declared semantics for conversion/readback**

After the semantic check succeeds, convert and parse using the mapping's normalized semantic type rather than rediscovering behavior from an arbitrary existing definition.

- [ ] **Step 5: Verify and commit**

Run the full .NET suite and Release build, then commit as `fix: validate official parameter semantic types`.

### Task 5: Separate first initialization from migration confirmation

**Files:**
- Modify: `src/BIMBaoGui.Stage01/Core/Stage01Validation.cs`
- Modify: `src/BIMBaoGui.Stage01/Stage01Component.cs`
- Modify: `src/BIMBaoGui.Stage01/Revit/Stage01RevitService.cs`
- Modify: `tests/BIMBaoGui.Stage01.Core.Tests/Stage01ValidationTests.cs`

- [ ] **Step 1: Write failing validation-mode tests**

Add `Stage01ValidationMode.FirstInitialization` and `ExistingInitialization`. Assert the blank confirmation is required only for the first mode.

- [ ] **Step 2: Verify RED**

Expected: compile failure because the mode overload does not exist.

- [ ] **Step 3: Add the explicit mode overload**

Keep the existing overload delegating to `FirstInitialization` for compatibility. In component Solve/commit, read the Revit snapshot before validation and choose `ExistingInitialization` when `_snapshot.IsInitialized`. In the Revit commit method, read `StoredInitialization` before validation and choose the same mode.

- [ ] **Step 4: Verify and commit**

Run validation tests and the full .NET suite, then commit as `fix: separate Stage01 migration confirmation`.

### Task 6: Put official compatibility into HBR_FileContext 0.9.0

**Files:**
- Create: `src/BIMBaoGui.Stage01/Hifc/Stage01OfficialCompatibilityPolicy.cs`
- Create: `tests/BIMBaoGui.Stage01.Core.Tests/Stage01OfficialCompatibilityPolicyTests.cs`
- Modify: `src/BIMBaoGui.Stage01/Context/HBRContextVersions.cs`
- Modify: `src/BIMBaoGui.Stage01/Context/HBRFileContext.cs`
- Modify: `src/BIMBaoGui.Stage01/Context/HBRFileContextFactory.cs`
- Modify: `src/BIMBaoGui.Stage01/Context/HBRFileContextCanonicalizer.cs`
- Modify: `src/BIMBaoGui.Stage01/TaskPlanning/TaskPlanCompiler.cs`
- Modify: `src/BIMBaoGui.Stage01/Revit/Stage01OfficialHifcProjectionService.cs`
- Modify: affected context/compiler tests and compile links

- [ ] **Step 1: Write failing compatibility-policy tests**

An empty organization record is compatible; any non-empty organization value produces `BLOCK_PENDING_OFFICIAL_PLUGIN_CONTRACT`. The returned blocker text is deterministic.

- [ ] **Step 2: Verify RED**

Expected: compile failure because the policy does not exist.

- [ ] **Step 3: Reuse one policy in write and context paths**

The Revit projection service appends policy blockers. `HBRFileContextFactory` evaluates the same policy and sets `OfficialProtocolCompatible`.

- [ ] **Step 4: Bump and hash the context contract**

Set `FileContextSchema = "0.9.0"`. Add `officialProtocolCompatible` to canonical JSON before `rulePackVersion`; parse it explicitly. `HBRFileContext.IsReady` requires validity, initialization, and compatibility. `TaskPlanCompiler.ValidateContext` adds a blocker when compatibility is false.

- [ ] **Step 5: Write context round-trip and compiler tests**

Assert the compatibility flag changes the hash, survives JSON round-trip, and blocks Stage02 compilation independently of `InitializationPassed`.

- [ ] **Step 6: Verify and commit**

Run full tests and commit as `fix: carry official compatibility in file context`.

### Task 7: Reject stale Stage02 context using live storage identity

**Files:**
- Create: `src/BIMBaoGui.Stage01/Context/HBRLiveContextPolicy.cs`
- Create: `tests/BIMBaoGui.Stage01.Core.Tests/HBRLiveContextPolicyTests.cs`
- Modify: `src/BIMBaoGui.Stage01/Revit/Stage02RevitContextService.cs`
- Modify: `src/BIMBaoGui.Stage01/Stage02TaskPlanComponent.cs`
- Modify: test project compile links

- [ ] **Step 1: Write failing freshness tests**

Test matching file GUID/payload hash/workflow version; different payload hash; different file GUID; missing live initialization; and case-insensitive SHA comparison.

- [ ] **Step 2: Verify RED**

Expected: compile failure because the policy does not exist.

- [ ] **Step 3: Implement the live identity policy**

```csharp
public static IReadOnlyList<string> Validate(
  string contextFileGuid, string contextPayloadHash,
  bool liveInitialized, string liveFileGuid,
  string livePayloadHash, string liveWorkflowVersion)
```

Return deterministic blockers; compare GUID/hash case-insensitively and workflow version ordinally.

- [ ] **Step 4: Populate and enforce the snapshot**

`Stage02RevitContextSnapshot` receives `IsInitialized`, `StoredFileGuid`, `StoredPayloadHash`, and `StoredWorkflowVersion` from `Stage01Storage.Read(document)`. The component adds freshness blockers after fingerprint validation. Remove the read-only document blocker because Stage02 performs no write.

- [ ] **Step 5: Verify and commit**

Run full tests and commit as `fix: validate Stage02 against live Stage01 storage`.

### Task 8: Update versioning, deployment, docs, and CI governance

**Files:**
- Modify: `src/BIMBaoGui.Stage01/AssemblyInfo.cs`
- Modify: `src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj`
- Modify: `README.md`
- Modify: `.github/workflows/ci.yml`
- Create: `docs/revit2020-v090-acceptance-checklist.md`

- [ ] **Step 1: Write/update contract tests first**

Python tests must require assembly version `0.9.0.0`, fixed deployment filename `BIMBaoGui.Stage01.gha`, Stage01/02/03 documentation, and a CI artifact manifest containing SHA-256 and commit SHA.

- [ ] **Step 2: Verify RED**

Run only the updated Python tests and confirm they fail on v0.8.2/docs/workflow.

- [ ] **Step 3: Implement minimal release metadata**

Set assembly/file version to 0.9.0.0, document the actual `%APPDATA%\Grasshopper\Libraries\BIMbaogui` path, and add CI concurrency keyed by workflow/ref with cancellation. Package the fixed GHA name plus `artifact-manifest.json`.

- [ ] **Step 4: Verify and commit**

Run Python, .NET, Release build, `git diff --check`, and vulnerable-package scan. Commit as `build: prepare v0.9.0 Revit validation artifact`.

### Task 9: Build, deploy, and execute the golden acceptance chain

**Files:**
- Deploy: `C:\Users\2899\AppData\Roaming\Grasshopper\Libraries\BIMbaogui\BIMBaoGui.Stage01.gha`
- Create: `docs/reviews/2026-08-01-v090-revit-ifc-acceptance.md`
- Create: `D:\18_建模项目\2026.07_湖北银行报规\3D\20260731test02-v090-validation.ifc`

- [ ] **Step 1: Run the complete clean verification suite**

```powershell
$env:PYTEST_DISABLE_PLUGIN_AUTOLOAD='1'
C:\ProgramData\Anaconda3\python.exe -m pytest -q
dotnet test tests\BIMBaoGui.Stage01.Core.Tests\BIMBaoGui.Stage01.Core.Tests.csproj -c Release
dotnet build src\BIMBaoGui.Stage01\BIMBaoGui.Stage01.csproj -c Release --nologo
git diff --check
dotnet list src\BIMBaoGui.Stage01\BIMBaoGui.Stage01.csproj package --vulnerable --include-transitive
```

- [ ] **Step 2: Back up and deploy using the fixed filename**

Record SHA-256 and assembly metadata. Move the existing deployed v0.8.2 file to a timestamped backup outside the active Libraries folder, then atomically place `BIMBaoGui.Stage01.gha`. Verify there is exactly one active BIMBaoGui GHA.

- [ ] **Step 3: Restart Revit/GH and test execution arming**

Open the validation GH with Stage03 Toggle true and confirm no write journal/message occurs. Toggle false, then true, and confirm one enqueue/write sequence.

- [ ] **Step 4: Commit/readback on a safe RVT copy**

Use a copied RVT if the supplied test file contains unrelated unsaved work. Verify the Stage01 payload hash, shared parameters, exact official aliases, and Revit readback.

- [ ] **Step 5: Export and inspect a new IFC**

Use the recovered official H-IFC plugin to export `20260731test02-v090-validation.ifc`. Compare all non-empty compatible Stage01 fields with the IFC property sets. Record duplicate-source pairs, missing properties, unexpected properties, values, units, exporter log, journal, file hash, and timestamp.

- [ ] **Step 6: Commit the acceptance report**

```powershell
git add docs/reviews/2026-08-01-v090-revit-ifc-acceptance.md
git commit -m "test: record v0.9.0 Revit H-IFC acceptance"
```
