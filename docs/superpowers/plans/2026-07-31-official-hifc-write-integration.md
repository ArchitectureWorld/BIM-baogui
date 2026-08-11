# Official H-IFC Write Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a new Revit 2020 / Rhino 8 Grasshopper `.gha` that keeps the current Stage 01/02 product and adds official H-IFC-compatible shared-parameter installation, binding, value writing, read-back verification, and diagnostics.

**Architecture:** Mapping and shared-parameter resources remain the immutable compatibility contract. A focused catalog parses bindings, a Revit service owns transactions and conversions, and a thin GH component only gathers inputs and displays results.

**Tech Stack:** C# net48, Grasshopper 8 SDK, Revit 2020 API, `JavaScriptSerializer`, GitHub Actions.

## Global Constraints

- IFC export remains the official plugin's responsibility.
- Do not implement IFC export or IFC post-processing.
- Revit 2020 and Rhino 8 are the supported runtime.
- All writes must use TransactionGroup, regenerate, read-back, and rollback on failure.
- Inputs accept propertyId, parameterGuid, or full parameterName.

---

### Task 1: Embed mapping resources and bump plugin version

**Files:**
- Modify: `src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj`

- [ ] Embed the generated bindings and shared-parameter files with stable logical names.
- [ ] Bump assembly/package version to `0.6.0`.
- [ ] Build and confirm both resources are present.

### Task 2: Add mapping catalog

**Files:**
- Create: `src/BIMBaoGui.Stage01/Hifc/OfficialHifcMapping.cs`
- Create: `src/BIMBaoGui.Stage01/Hifc/OfficialHifcMappingCatalog.cs`

- [ ] Parse `GH_HIFC_ParameterBindings.json`.
- [ ] Index each binding by propertyId, GUID, and full parameter name.
- [ ] Reject malformed GUIDs, duplicate aliases, and unsupported categories with actionable diagnostics.

### Task 3: Add Revit shared-parameter installer and writer

**Files:**
- Create: `src/BIMBaoGui.Stage01/Revit/OfficialHifcWriteModels.cs`
- Create: `src/BIMBaoGui.Stage01/Revit/OfficialHifcWriteService.cs`

- [ ] Resolve target elements, defaulting to ProjectInformation.
- [ ] Install missing shared parameter definitions from the embedded official compatibility file.
- [ ] Bind each parameter to its mapped Revit category and instance/type scope.
- [ ] Convert TEXT, INTEGER, YESNO, LENGTH, AREA, VOLUME and numeric values.
- [ ] Write inside a TransactionGroup.
- [ ] Regenerate and verify each value by parameter GUID.
- [ ] Roll back the full group on any failure.

### Task 4: Add Stage 03 GH component

**Files:**
- Create: `src/BIMBaoGui.Stage01/Stage03OfficialHifcWriteComponent.cs`

- [ ] Register execute, element IDs, property keys, and value inputs.
- [ ] Support one-value broadcasting.
- [ ] Enqueue Revit write through the existing RevitHost external-event bridge.
- [ ] Output success, status, messages and write count.
- [ ] Keep `SolveInstance()` free of mapping and transaction logic.

### Task 5: Add contract tests and build artifact

**Files:**
- Create: `tests/test_official_hifc_write_contract.py`

- [ ] Assert the component and service files exist.
- [ ] Assert the project embeds both mapping resources.
- [ ] Assert the service contains TransactionGroup, Regenerate, GUID read-back and RollBack.
- [ ] Run repository contract tests.
- [ ] Run .NET unit tests.
- [ ] Build the GHA in GitHub Actions.
- [ ] Download the single-file `.gha` artifact.