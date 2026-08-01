# Official Plugin Compatible Revit Write Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Grasshopper plugin write guide- and software-compliant data into Revit so the official H-IFC plugin exports it and the official checker recognizes it correctly.

## 中文验收摘要

本计划的唯一最终验收链路是：

```text
Golden RVT
→ 官方插件导出
→ Golden IFC
→ 检查软件正确识别
```

Revit 内参数写入与回读只属于开发侧中间验证。只有官方插件导出的 Golden IFC 中实体、属性集、属性名、数据类型、单位和值均正确，并且检查软件能够正确识别，才能把对应字段标记为正式兼容。

**Architecture:** Treat official property rules and official object mappings as evidence, not interchangeable with our implementation decisions. Stage 01 and the generic writer resolve fields through a shared compatibility catalog, enforce entity-specific targets, write and read back Revit data atomically, then require a Golden RVT → official plugin → Golden IFC → checker roundtrip before declaring compatibility.

**Tech Stack:** C# net48, Grasshopper 8 SDK, Revit 2020 API, embedded JSON/shared-parameter resources, Python pytest contract tests, GitHub Actions, official H-IFC exporter and checker for integration acceptance.

## Global Constraints

- IFC export is performed only by the official H-IFC plugin.
- Do not implement self-developed IFC export or IFC post-processing.
- Revit parameter readback is not final compatibility evidence.
- X means north/south coordinate; Y means east/west coordinate.
- Do not default non-IfcProject/IfcBuilding properties to ProjectInformation.
- Entities without official object-carrier evidence are blocked from compatibility claims.
- Planning targets and calculated actual values remain separate.
- Revit 2020 + Rhino 8 + Rhino.Inside.Revit are the supported runtime.

---

## File Structure

### Compatibility evidence and documentation

- Create: `specs/hifc-mapping/v1/data/official_plugin_compatibility_status.v1.json`
  - Records evidence level, entity write policy, coordinate semantics and final acceptance state.
- Create: `docs/reviews/2026-08-01-official-plugin-write-deep-review.md`
  - Records root causes, conflicts, fixes and remaining evidence gaps.
- Create: `docs/superpowers/specs/2026-08-01-official-plugin-compatible-write-design.md`
  - Active design replacing the post-export-enrichment path.
- Create: `docs/superpowers/plans/2026-08-01-official-plugin-compatible-write.md`
  - This implementation plan.
- Modify: `specs/hifc-mapping/v1/README.md`
  - Mark the official-plugin-only product path as active.
- Modify: `specs/hifc-mapping/v1/docs/01_稳定架构与开发逻辑.md`
  - Add a superseded-path warning for self-export/post-processing sections.
- Modify: `specs/hifc-mapping/v1/docs/03_实施顺序与验收门槛.md`
  - Replace post-processing milestones with official-export roundtrip milestones.

### Runtime

- Modify: `src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj`
  - Embed compatibility status resource and bump version.
- Modify: `src/BIMBaoGui.Stage01/Hifc/OfficialHifcMapping.cs`
  - Add IFC entity, persistence/evidence and official write policy fields.
- Modify: `src/BIMBaoGui.Stage01/Hifc/OfficialHifcMappingCatalog.cs`
  - Resolve canonical Stage 01 field keys and load entity policies.
- Create: `src/BIMBaoGui.Stage01/Hifc/OfficialPluginCompatibilityCatalog.cs`
  - Parse compatibility status resource and expose entity policy/exception lookup.
- Modify: `src/BIMBaoGui.Stage01/Revit/Stage01OfficialHifcProjectionService.cs`
  - Replace ten-field hardcoding with registry-driven project-field projection; report blocked organization data.
- Modify: `src/BIMBaoGui.Stage01/Revit/Stage01Storage.cs`
  - Remove projection side effects.
- Modify: `src/BIMBaoGui.Stage01/Revit/Stage01RevitService.cs`
  - Fix X/Y semantics, explicitly call projection service, aggregate diagnostics and preserve atomic rollback.
- Modify: `src/BIMBaoGui.Stage01/Revit/OfficialHifcWriteService.cs`
  - Resolve targets per mapping, enforce entity policy/category and support angle conversion.
- Modify: `src/BIMBaoGui.Stage01/Stage03OfficialHifcWriteComponent.cs`
  - Correct input copy and compatibility status wording.

### Tests

- Create: `tests/test_official_export_contract_review.py`
- Modify: `tests/test_stage01_official_hifc_projection.py`
- Modify: `tests/test_official_hifc_write_contract.py`
- Modify: `tests/test_plugin_contract.py`

---

### Task 1: Capture the conflicting behavior as failing tests

**Files:**
- Create: `tests/test_official_export_contract_review.py`

**Interfaces:**
- Consumes: repository source and JSON resources.
- Produces: regression assertions for coordinate semantics, Stage 01 coverage, target safety, evidence status and official-export acceptance.

- [x] **Step 1: Write failing tests for all identified conflicts**

Assertions cover:

```text
X → NorthSouth
Y → EastWest
Stage01Storage has no projection side effect
Stage01 projection is registry-driven
Organization is explicitly blocked pending official contract
Non-project properties never default to ProjectInformation
Active design requires Golden RVT/Golden IFC/checker roundtrip
```

- [x] **Step 2: Run CI and verify RED**

Run: GitHub Actions `Build BIMBaoGui GHA`

Expected: contract test failure on the existing implementation.

---

### Task 2: Establish the evidence-status contract

**Files:**
- Create: `specs/hifc-mapping/v1/data/official_plugin_compatibility_status.v1.json`
- Create: `src/BIMBaoGui.Stage01/Hifc/OfficialPluginCompatibilityCatalog.cs`
- Modify: `src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj`

**Interfaces:**
- Produces: `OfficialPluginCompatibilityCatalog.Instance.GetEntityPolicy(string ifcEntity)` and `IsStage01ProjectFieldException(string fieldKey)`.

- [x] **Step 1: Add the compatibility JSON**

Record all nine entities and distinguish official extracted object mappings from implementation assumptions.

- [x] **Step 2: Embed the JSON resource**

Logical resource name:

```text
BIMBaoGui.Stage01.Resources.official_plugin_compatibility_status.v1.json
```

- [x] **Step 3: Implement the catalog**

Required API:

```csharp
internal sealed class OfficialPluginEntityPolicy
{
  public string IfcEntity { get; set; }
  public string OfficialObjectMappingEvidence { get; set; }
  public string RevitCarrier { get; set; }
  public string WritePolicy { get; set; }
  public bool OfficialExportVerified { get; set; }
}

internal sealed class OfficialPluginCompatibilityCatalog
{
  public static OfficialPluginCompatibilityCatalog Instance { get; }
  public OfficialPluginEntityPolicy GetEntityPolicy(string ifcEntity);
  public bool IsStage01ProjectFieldException(string fieldKey);
}
```

- [x] **Step 4: Run contract tests**

Expected: status-file tests pass; production-code tests remain red.

---

### Task 3: Fix coordinate semantics and initialization orchestration

**Files:**
- Modify: `src/BIMBaoGui.Stage01/Revit/Stage01RevitService.cs`
- Modify: `src/BIMBaoGui.Stage01/Revit/Stage01Storage.cs`
- Modify: `tests/test_stage01_official_hifc_projection.py`

**Interfaces:**
- Consumes: `Stage01OfficialHifcProjectionService.WriteAndVerify(Document, string)`.
- Produces: one atomic initialization transaction with explicit projection diagnostics.

- [x] **Step 1: Fix Revit read semantics**

```csharp
BaseX = position.NorthSouth;
BaseY = position.EastWest;
```

- [x] **Step 2: Fix Revit write semantics**

```csharp
double northMeters = ParseRequiredNumber(model, Stage01Keys.BaseX);
double eastMeters = ParseRequiredNumber(model, Stage01Keys.BaseY);
new ProjectPosition(eastFeet, northFeet, elevationFeet, angleRadians);
```

- [x] **Step 3: Fix readback labels and comparisons**

```text
基点坐标 X（南北） ↔ NorthSouth
基点坐标 Y（东西） ↔ EastWest
```

- [x] **Step 4: Remove projection call from Stage01Storage**

`Stage01Storage.Write` must only write Extensible Storage.

- [x] **Step 5: Explicitly invoke projection in Stage01RevitService**

Collect projection messages and include them in `CommitResult.Messages`.

- [ ] **Step 6: Run tests**

Expected: coordinate and storage-orchestration assertions pass.

---

### Task 4: Replace Stage 01 hardcoding with registry-driven projection

**Files:**
- Modify: `src/BIMBaoGui.Stage01/Hifc/OfficialHifcMapping.cs`
- Modify: `src/BIMBaoGui.Stage01/Hifc/OfficialHifcMappingCatalog.cs`
- Modify: `src/BIMBaoGui.Stage01/Revit/Stage01OfficialHifcProjectionService.cs`
- Modify: `tests/test_stage01_official_hifc_projection.py`

**Interfaces:**
- Produces: `TryResolveStage01FieldKey(string fieldKey, out OfficialHifcMapping mapping)`.

- [x] **Step 1: Parse canonical Stage 01 field keys**

Input:

```text
IfcProject|Pset_申报信息属性集|项目名称
```

Candidate parameter alias:

```text
HIFC.申报信息属性集.项目名称
```

- [x] **Step 2: Project every non-empty IfcProject field**

Skip internal HBR fields and structured planning targets. Missing mappings must fail unless explicitly listed in the compatibility-status exceptions.

- [x] **Step 3: Parse organizations separately**

If any organization field is non-empty, preserve it in HBR storage and emit `BLOCK_PENDING_OFFICIAL_PLUGIN_CONTRACT`; do not write it to ProjectInformation.

- [x] **Step 4: Support all Stage 01 parameter types**

String, Integer, YesNo, Length, Area, Volume, Angle and Number.

- [ ] **Step 5: Run mapping coverage tests**

Expected: every writable IfcProject field has a mapping or explicit documented exception.

---

### Task 5: Enforce entity-specific targets in the generic writer

**Files:**
- Modify: `src/BIMBaoGui.Stage01/Revit/OfficialHifcWriteService.cs`
- Modify: `src/BIMBaoGui.Stage01/Revit/OfficialHifcWriteModels.cs`
- Modify: `src/BIMBaoGui.Stage01/Stage03OfficialHifcWriteComponent.cs`
- Modify: `tests/test_official_hifc_write_contract.py`

**Interfaces:**
- Produces: `ResolveTargetsForMapping(Document, OfficialHifcMapping, IReadOnlyList<int>)`.

- [x] **Step 1: Resolve each mapping independently**

Do not resolve one target list and apply all mappings to all elements.

- [x] **Step 2: Apply entity rules**

```text
IfcProject/IfcBuilding → ProjectInformation allowed without IDs
IfcBuildingStorey → explicit Level IDs only
IfcSpace → explicit Room IDs only
all blocked entities → actionable error
```

- [x] **Step 3: Validate target category before write**

Reject mismatched ElementIds before starting value changes.

- [x] **Step 4: Add Angle conversion**

Degrees → Revit internal angle units.

- [x] **Step 5: Correct UI copy**

The ElementId input states that leaving it empty is only valid for IfcProject/IfcBuilding.

- [ ] **Step 6: Run tests**

Expected: target-safety assertions pass and no previous tests regress.

---

### Task 6: Reconcile conflicting baseline documentation

**Files:**
- Modify: `specs/hifc-mapping/v1/README.md`
- Modify: `specs/hifc-mapping/v1/docs/01_稳定架构与开发逻辑.md`
- Modify: `specs/hifc-mapping/v1/docs/03_实施顺序与验收门槛.md`
- Create: `docs/reviews/2026-08-01-official-plugin-write-deep-review.md`

- [x] **Step 1: Mark post-export architecture as superseded**

Historical data remains available, but it is not the active GHA product path.

- [x] **Step 2: Publish the conflict matrix and resolution**

Cover the 166-property rules, 4 official object mappings, 102 Stage 01 fields, 10-field implementation, custom GUID evidence and official-export gap.

- [x] **Step 3: Replace acceptance gates**

Require Golden RVT → official plugin export → Golden IFC → checker recognition.

---

### Task 7: Build and package the reviewed GHA

**Files:**
- Modify: `src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj`
- Modify: `.github/workflows/build-stage01-gha.yml`
- Modify: `tests/test_plugin_contract.py`

- [x] **Step 1: Bump version**

Set assembly/package version to `0.7.0` because compatibility semantics and write behavior change materially.

- [ ] **Step 2: Run repository contract tests**

Run:

```text
python -m pytest tests -q
```

Expected: all pass.

- [ ] **Step 3: Run .NET core tests**

Run:

```text
dotnet test tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj -c Release
```

Expected: all pass.

- [ ] **Step 4: Build GHA**

Run:

```text
dotnet build src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj -c Release
```

Expected: `BIMBaoGui.Stage01.gha` with assembly version `0.7.0.0`.

- [ ] **Step 5: Upload the single-file artifact**

Artifact name:

```text
BIMBaoGui-Revit2020-Rhino8-v0.7.0-official-plugin-write-review
```

---

### Task 8: Execute the official software acceptance loop

**External fixtures:**
- Create in Revit environment: `fixtures/golden/official-plugin-write-v1/Golden-OfficialWrite.rvt`
- Export with official plugin: `fixtures/golden/official-plugin-write-v1/Golden-OfficialWrite.ifc`
- Save checker report: `fixtures/golden/official-plugin-write-v1/checker-report.json`
- Save manifest: `fixtures/golden/official-plugin-write-v1/export-manifest.json`

- [ ] **Step 1: Populate distinct sentinel values**

Use values that expose axis, unit and object-placement errors.

- [ ] **Step 2: Export only with the official plugin**

Record official plugin version and settings.

- [ ] **Step 3: Inspect IFC values**

Compare entity, Pset, property, data type, unit and value.

- [ ] **Step 4: Run official checker**

Record whether every target property is recognized.

- [ ] **Step 5: Promote verified fields**

Only after the roundtrip, change `officialExportVerified`/checker status for the tested fields or entity profile.

---

## Plan Self-Review

- The active path contains no self-developed IFC export or post-export enrichment.
- Coordinate semantics are explicit and testable.
- Stage 01 standard-field coverage is data-driven.
- Organization and other unsupported entities are blocked rather than silently miswritten.
- Revit readback and official recognition are separate gates.
- Every production behavior change has a corresponding failing regression test.
