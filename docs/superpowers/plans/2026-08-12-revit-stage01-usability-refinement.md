# Revit Stage01 Usability Refinement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the first-initialization blank-model restriction, constrain long status reports to fixed-height scroll areas, and group all optional Stage01 fields into one collapsed section per directory while preserving Stage01/Stage02/MCP data semantics.

**Architecture:** Keep one Revit product line on `feat/revit-native-addin-mcp-v0.3`; do not create another branch. Modify the existing Stage01 preflight and WPF views directly, retain the current MCP request field for compatibility, and update the combined installer artifact only. After verification, retire the superseded non-MCP branch instead of maintaining two divergent implementations.

**Tech Stack:** C# 7.3, .NET Framework 4.8, Revit 2020 API, WPF, xUnit, Python pytest contract tests, GitHub Actions.

## Global Constraints

- Work only on `feat/revit-native-addin-mcp-v0.3`; do not create an additional feature branch.
- Do not change HBR RulePack identity, rule data, Stage01 canonical JSON, Extensible Storage schema, parameter GUIDs, coordinate semantics, Stage02 business logic, or MCP tool names.
- Keep `NativeStage01WriteRequest.ConfirmBlankProject` and MCP `confirm_blank_project` input for one compatibility cycle, but ignore them in business decisions.
- Existing initialized RVTs still require `AllowReinitialize = true` before overwrite.
- Detailed Stage01 and Stage02 status areas are fixed at 96 px and internally scrollable.
- Workspace-level status is a fixed 32 px single-line summary with ellipsis.
- Optional fields are grouped in exactly one `Expander` per active directory, collapsed by default, remembered per directory for the current Revit session, and auto-expanded when optional-field validation errors exist.
- The final deliverable is one combined Revit + MCP installer; do not publish a second non-MCP package.

---

## File Map

- `src/BIMBaoGui.RevitAddin/Stage01/NativeStage01WritePreflight.cs`: remove blank confirmation and model-content blockers while preserving all other preflight checks.
- `src/BIMBaoGui.RevitAddin/Stage01/NativeStage01RevitService.cs`: stop invoking `NativeStage01BlankModelGate`.
- `src/BIMBaoGui.RevitAddin/Stage01/NativeStage01ViewModel.cs`: expose optional-field counts and optional validation-error detection for the active organization record.
- `src/BIMBaoGui.RevitAddin/Stage01/NativeStage01View.cs`: remove the blank-model checkbox, render required fields first, render one remembered optional `Expander`, and constrain detailed status height.
- `src/BIMBaoGui.RevitAddin/Stage02/NativeStage02View.cs`: constrain detailed status height without changing Stage02 behavior.
- `src/BIMBaoGui.RevitAddin/WorkspaceControl.cs`: replace duplicated long status text with a fixed-height summarized status bar.
- `tests/BIMBaoGui.RevitAddin.Tests/NativeStage01WritePreflightTests.cs`: domain regression tests for the retired blank-model gate.
- `tests/BIMBaoGui.RevitAddin.Tests/NativeStage01ViewModelTests.cs`: tests for optional counts and error-driven expansion inputs.
- `tests/test_revit_addin_stage01_revit_contract.py`: verify production code no longer scans blank-model blockers.
- `tests/test_revit_addin_stage01_ui_contract.py`: verify checkbox removal, fixed-height status layout, and one optional expander.
- `tests/test_revit_addin_stage02_revit_contract.py`: verify Stage02 fixed-height status without changing workflow contracts.
- `tests/test_revit_addin_mcp_non_regression.py`: narrow the old non-regression lock so intentional Stage01 usability files may change while Stage02 and stable Stage01 contracts remain frozen.
- `specs/revit-addin/v0.3.1-functional-baseline.json`: record the new canonical Revit product baseline after verification.
- `.github/workflows/build-revit-mcp.yml`: package a single stable combined installer name and run all revised contracts.
- `docs/revit-addin/README.md`: document the changed first-initialization and optional-field behavior.

---

### Task 1: Write failing contracts for the retired blank-model gate

**Files:**
- Modify: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage01WritePreflightTests.cs`
- Modify: `tests/test_revit_addin_stage01_revit_contract.py`

**Interfaces:**
- Consumes: `NativeStage01WritePreflight.Evaluate(NativeStage01DocumentState, NativeStage01ValidationResult, bool, bool)`.
- Produces: tests requiring `NoRecord` initialization to succeed regardless of `BlockingElements` and `confirmBlankProject`.

- [ ] **Step 1: Add the failing xUnit behavior test**

Add a test equivalent to:

```csharp
[Fact]
public void FirstInitializationAllowsExistingModelWithoutBlankConfirmation()
{
  NativeStage01DocumentState state = ValidState(
    NativeStage01StorageState.NoRecord);
  state.BlockingElements = new[] { "Wall / Id=42" };

  NativeStage01PreflightDecision result =
    NativeStage01WritePreflight.Evaluate(
      state,
      ValidValidation(),
      confirmBlankProject: false,
      allowReinitialize: false);

  Assert.True(result.Accepted);
  Assert.DoesNotContain(result.Blockers, value =>
    value.Code == NativeStage01PreflightCodes.BlankConfirmationRequired
    || value.Code == NativeStage01PreflightCodes.ModelNotBlank);
}
```

- [ ] **Step 2: Add the failing source-contract assertion**

Require `NativeStage01RevitService.cs` not to contain:

```python
assert "NativeStage01BlankModelGate.FindBlockingElements" not in source
```

- [ ] **Step 3: Run the focused tests and confirm RED**

Run:

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj -c Release --filter FirstInitializationAllowsExistingModelWithoutBlankConfirmation
python -m pytest tests/test_revit_addin_stage01_revit_contract.py -q
```

Expected: both fail because the existing preflight still requires confirmation and scans model elements.

- [ ] **Step 4: Commit tests only**

```bash
git add tests/BIMBaoGui.RevitAddin.Tests/NativeStage01WritePreflightTests.cs tests/test_revit_addin_stage01_revit_contract.py
git commit -m "test: retire Stage01 blank-model gate"
```

### Task 2: Remove the blank-model restriction while preserving overwrite protection

**Files:**
- Modify: `src/BIMBaoGui.RevitAddin/Stage01/NativeStage01WritePreflight.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage01/NativeStage01RevitService.cs`

**Interfaces:**
- Consumes: existing `NativeStage01WriteRequest` including compatibility property `ConfirmBlankProject`.
- Produces: preflight that ignores model content and blank confirmation for `NoRecord`, but still blocks `Current` storage without `AllowReinitialize`.

- [ ] **Step 1: Remove only the two `NoRecord` blank-model blockers**

In `NativeStage01WritePreflight.Evaluate`, make `NativeStage01StorageState.NoRecord` perform no additional blocker creation. Keep all other cases unchanged.

- [ ] **Step 2: Stop collecting model blockers**

Set `BlockingElements = Array.Empty<string>()` in `NativeStage01RevitService` and remove the call to `NativeStage01BlankModelGate.FindBlockingElements(document)`.

- [ ] **Step 3: Run focused tests and confirm GREEN**

Run:

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj -c Release --filter NativeStage01WritePreflightTests
python -m pytest tests/test_revit_addin_stage01_revit_contract.py -q
```

Expected: all pass; existing reinitialization, storage-corruption, unsupported-version, unsaved, read-only, and family-document tests remain green.

- [ ] **Step 4: Commit the domain fix**

```bash
git add src/BIMBaoGui.RevitAddin/Stage01/NativeStage01WritePreflight.cs src/BIMBaoGui.RevitAddin/Stage01/NativeStage01RevitService.cs
git commit -m "fix: allow Stage01 initialization on modeled RVTs"
```

### Task 3: Write failing tests for optional-field grouping and status layout

**Files:**
- Modify: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage01ViewModelTests.cs`
- Modify: `tests/test_revit_addin_stage01_ui_contract.py`
- Modify: `tests/test_revit_addin_stage02_revit_contract.py`

**Interfaces:**
- Consumes: `NativeStage01ViewModel.FieldsForGroup`, `GetFieldValue`, and `ValidationMessagesForField`.
- Produces: expected APIs `GetOptionalFieldCount`, `GetFilledOptionalFieldCount`, and `HasOptionalValidationError`, plus WPF layout contracts.

- [ ] **Step 1: Add failing ViewModel tests**

Add tests proving:

```csharp
Assert.Equal(expectedOptionalCount, viewModel.GetOptionalFieldCount(group));
Assert.Equal(0, viewModel.GetFilledOptionalFieldCount(group));
viewModel.SetFieldValue(optionalField, "value");
Assert.Equal(1, viewModel.GetFilledOptionalFieldCount(group));
```

and after validation of an invalid optional value:

```csharp
Assert.True(viewModel.HasOptionalValidationError(group));
```

- [ ] **Step 2: Add failing UI source contracts**

Require:

```python
assert "确认当前文件尚未开始正式建模" not in stage01
assert "new Expander" in stage01
assert "选填项（共 " in stage01
assert "Height = new GridLength(96)" in stage01
assert "VerticalScrollBarVisibility = ScrollBarVisibility.Auto" in stage01
assert "Height = new GridLength(96)" in stage02
assert "Height = new GridLength(32)" in workspace
assert "TextTrimming = TextTrimming.CharacterEllipsis" in workspace
```

- [ ] **Step 3: Run focused tests and confirm RED**

Run:

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj -c Release --filter NativeStage01ViewModelTests
python -m pytest tests/test_revit_addin_stage01_ui_contract.py tests/test_revit_addin_stage02_revit_contract.py -q
```

Expected: failures for missing ViewModel methods, retained checkbox, absent Expander, and Auto-sized status rows.

- [ ] **Step 4: Commit tests only**

```bash
git add tests/BIMBaoGui.RevitAddin.Tests/NativeStage01ViewModelTests.cs tests/test_revit_addin_stage01_ui_contract.py tests/test_revit_addin_stage02_revit_contract.py
git commit -m "test: define compact Stage01 form and status layout"
```

### Task 4: Implement required-first form rendering and fixed-height status regions

**Files:**
- Modify: `src/BIMBaoGui.RevitAddin/Stage01/NativeStage01ViewModel.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage01/NativeStage01View.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage02/NativeStage02View.cs`
- Modify: `src/BIMBaoGui.RevitAddin/WorkspaceControl.cs`

**Interfaces:**
- Produces:
  - `int GetOptionalFieldCount(string group)`
  - `int GetFilledOptionalFieldCount(string group)`
  - `bool HasOptionalValidationError(string group)`
  - one `Dictionary<string, bool>` storing optional-section expansion by directory for the current view lifetime.

- [ ] **Step 1: Add ViewModel optional-field queries**

Implement the three methods using `NativeStage01Validator.IsRequired`, `FieldsForGroup`, `GetFieldValue`, and current validation messages. Optional validation errors must be detected only for non-required fields in the requested group.

- [ ] **Step 2: Remove the blank-model checkbox from Stage01 UI**

Delete `_confirmBlankProject`, its visual construction, busy-state handling, and reset logic. Continue constructing `NativeStage01WriteRequest` with `ConfirmBlankProject = false` solely for binary/source compatibility.

- [ ] **Step 3: Render required fields before one optional `Expander`**

In `RenderForm`:

```csharp
NativeStage01FieldDefinition[] required = fields
  .Where(NativeStage01Validator.IsRequired)
  .ToArray();
NativeStage01FieldDefinition[] optional = fields
  .Where(value => !NativeStage01Validator.IsRequired(value))
  .ToArray();
```

Render all required fields directly. If optional fields exist, create exactly one `Expander` with header:

```text
选填项（共 N 项，已填写 M 项）
```

Its `IsExpanded` value comes from the per-group dictionary, defaulting to false. `Expanded` and `Collapsed` events update the dictionary. Before rendering, set the group state to true when `HasOptionalValidationError(group)` is true.

- [ ] **Step 4: Keep optional header counts current without rebuilding editors**

Store the active optional `Expander` reference and update its header from all editor change handlers after calling `_viewModel.SetFieldValue`. Do not call `RenderAll` on each keystroke.

- [ ] **Step 5: Constrain Stage01 and Stage02 detailed statuses**

Change each stage root row 3 to `new GridLength(96)` and place `_statusText` inside a `ScrollViewer` with vertical scrolling and disabled horizontal scrolling.

- [ ] **Step 6: Convert Workspace status to a fixed summary bar**

Change row 3 to `new GridLength(32)`. Configure `_statusText` with no wrapping and `TextTrimming.CharacterEllipsis`. Route Stage01/Stage02 `StatusChanged` through a helper that normalizes whitespace, keeps the first `｜` segment, limits the summary to 120 characters, and stores the full status in `ToolTip`.

- [ ] **Step 7: Run focused tests and confirm GREEN**

Run:

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj -c Release --filter "NativeStage01ViewModelTests|NativeStage01WritePreflightTests"
python -m pytest tests/test_revit_addin_stage01_ui_contract.py tests/test_revit_addin_stage02_revit_contract.py tests/test_revit_addin_stage01_revit_contract.py -q
```

Expected: all pass.

- [ ] **Step 8: Commit UI implementation**

```bash
git add src/BIMBaoGui.RevitAddin/Stage01/NativeStage01ViewModel.cs src/BIMBaoGui.RevitAddin/Stage01/NativeStage01View.cs src/BIMBaoGui.RevitAddin/Stage02/NativeStage02View.cs src/BIMBaoGui.RevitAddin/WorkspaceControl.cs
git commit -m "fix: prioritize required Stage01 fields and constrain reports"
```

### Task 5: Replace the obsolete two-branch baseline and package one installer

**Files:**
- Modify: `tests/test_revit_addin_mcp_non_regression.py`
- Create: `specs/revit-addin/v0.3.1-functional-baseline.json`
- Modify: `.github/workflows/build-revit-mcp.yml`
- Modify: `docs/revit-addin/README.md`

**Interfaces:**
- Produces: one combined artifact named `BIMBaoGui-Revit2020-Native-MCP.zip` and one product baseline.

- [ ] **Step 1: Narrow the old non-regression lock**

Freeze all Stage02 `.cs` files against commit `35fa0ca6a8b07ba86231ee8305020fb23dcdb7c2`. For Stage01, freeze only unchanged contract files such as canonicalizer, payload codec, storage, parameter definitions, coordinate and transaction services; exclude the four intentionally modified usability/preflight files.

- [ ] **Step 2: Create the new baseline manifest**

Record the verified source commit and exact protected roots/files in `v0.3.1-functional-baseline.json`. Remove references in CI to the old two-product interpretation; the baseline protects one combined manual + MCP product.

- [ ] **Step 3: Update installer naming without stacking packages**

Change the uploaded artifact name to:

```text
BIMBaoGui-Revit2020-Native-MCP
```

and retain only the combined installer contents. Do not produce a separate non-MCP artifact.

- [ ] **Step 4: Update README behavior**

Document that modeled RVTs may be initialized, optional fields are collapsed by default, reports scroll within fixed areas, and the combined package remains fully usable manually when no MCP client is configured.

- [ ] **Step 5: Run the full verification matrix**

Run:

```powershell
python -m pytest tests/test_revit_addin_mcp_non_regression.py tests/test_revit_addin_scaffold_contract.py tests/test_revit_addin_installer_contract.py tests/test_revit_addin_stage01_storage_contract.py tests/test_revit_addin_stage01_revit_contract.py tests/test_revit_addin_stage01_ui_contract.py tests/test_revit_addin_stage02_revit_contract.py tests/test_revit_addin_mcp_contract.py tests/test_revit_addin_mcp_installer_contract.py -q
python -m pytest tests/test_hbr_rulepack_compiler.py tests/test_hbr_rules_manifest.py -q
dotnet test tests/BIMBaoGui.McpContracts.Tests/BIMBaoGui.McpContracts.Tests.csproj -c Release
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj -c Release
dotnet build src/BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build src/BIMBaoGui.McpServer/BIMBaoGui.McpServer.csproj -c Release -p:TreatWarningsAsErrors=true
```

Expected: all tests pass; both builds have zero errors and zero warnings.

- [ ] **Step 6: Commit packaging and baseline**

```bash
git add tests/test_revit_addin_mcp_non_regression.py specs/revit-addin/v0.3.1-functional-baseline.json .github/workflows/build-revit-mcp.yml docs/revit-addin/README.md
git commit -m "release: consolidate Revit manual and MCP product line"
```

### Task 6: Verify GitHub Actions, download the single installer, and retire the superseded branch

**Files:**
- No production source changes unless CI identifies a reproducible defect.

- [ ] **Step 1: Wait for the combined Revit MCP workflow**

Confirm every step succeeds, including contracts, HBR rules, xUnit, Release builds, single-file MCP publish, installer smoke, uninstall smoke, checksum validation, and artifact upload.

- [ ] **Step 2: Download and inspect the only installer artifact**

Verify the ZIP contains:

```text
Install.cmd
Uninstall.cmd
McpProbe.cmd
Install-Revit2020.ps1
BIMBaoGui.RevitAddin.addin
BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.dll
BIMBaoGui.RevitAddin/BIMBaoGui.McpContracts.dll
BIMBaoGui.McpServer/BIMBaoGui.McpServer.exe
README.md
SHA256SUMS.txt
```

- [ ] **Step 3: Verify all checksums locally**

Every entry in `SHA256SUMS.txt` must match the extracted bytes.

- [ ] **Step 4: Retire the superseded non-MCP branch**

Delete `feat/revit-native-addin-v1` after the combined branch build succeeds. Do not create a replacement branch. If the available GitHub connector cannot delete refs, move the superseded branch to the exact combined product commit and record the tooling limitation explicitly; do not allow divergent code to remain.

- [ ] **Step 5: Report one canonical product package**

Provide only the combined installer link and identify `feat/revit-native-addin-mcp-v0.3` as the sole Revit product branch.