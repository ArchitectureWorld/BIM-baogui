# Revit Native Planning Add-in v1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an independent Revit 2020 planning-approval add-in that consumes the same authoritative HBR rule database as the GHA product and can independently complete Stage01, Stage02 and Stage03.

**Architecture:** The native add-in uses a WPF DockablePane and an ExternalEvent request queue. It compiles its own embedded HBR rule pack from the repository’s single authoritative JSON, while all application logic, transactions and UI remain independent from Grasshopper and Rhino.Inside.

**Tech Stack:** C# / .NET Framework 4.8 / Autodesk Revit 2020 API / WPF / Python 3.13 contract tests / GitHub Actions on Windows.

## Global Constraints

- Target exactly Revit 2020 and `net48` for v1.
- Do not reference Grasshopper, RhinoCommon, Rhino.Inside.Revit or the GHA assembly.
- Use `specs/hbr-rules/v1/source/hbr_rule_source.v1.json` as the only editable business database.
- Every build must expose `packageId`, `packageVersion` and `rulePackageSha256`.
- X is Northing / north-south; Y is Easting / east-west.
- Revit Document reads and writes from modeless UI must run through ExternalEvent.
- Revit write operations must use explicit transactions and deterministic rollback semantics.
- Do not claim real-machine completion without Revit 2020 evidence.

---

## File Structure

```text
src/BIMBaoGui.RevitAddin/
  BIMBaoGui.RevitAddin.csproj
  App.cs
  WorkspaceControl.cs
  RevitExternalEventDispatcher.cs
  RulePackageIdentityReader.cs
  Stage01/
  Stage02/
  Stage03/
  Diagnostics/

tests/
  test_revit_addin_scaffold_contract.py
  BIMBaoGui.RevitAddin.Tests/

installer/
  BIMBaoGui.RevitAddin.addin
  Install-Revit2020.ps1

.github/workflows/
  build-revit-addin.yml

docs/revit-addin/
  README.md
  acceptance/
```

### Task 1: Bootstrap the native Revit host

**Files:**
- Create: `src/BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.csproj`
- Create: `src/BIMBaoGui.RevitAddin/App.cs`
- Create: `src/BIMBaoGui.RevitAddin/WorkspaceControl.cs`
- Create: `src/BIMBaoGui.RevitAddin/RevitExternalEventDispatcher.cs`
- Create: `src/BIMBaoGui.RevitAddin/RulePackageIdentityReader.cs`
- Create: `installer/BIMBaoGui.RevitAddin.addin`
- Create: `tests/test_revit_addin_scaffold_contract.py`
- Create: `.github/workflows/build-revit-addin.yml`

**Interfaces:**
- Produces: `App`, `ShowWorkspaceCommand`, `WorkspaceControl`, `RevitExternalEventDispatcher`, `RulePackageIdentityReader`.
- Consumes: authoritative HBR source, rule-pack compiler and Revit 2020 API package.

- [x] Write static contract tests that fail while the native project is absent.
- [x] Add the net48 WPF project with no GH/Rhino dependency.
- [x] Add Ribbon, DockablePane and ExternalEvent request queue.
- [x] Compile and embed the HBR rule pack from the shared authoritative database.
- [x] Display package identity and current Revit document state.
- [x] Add the `.addin` manifest and Windows CI lane.
- [ ] Run the Windows CI workflow and record the workflow run URL and artifact hash.
- [ ] Install the artifact in Revit 2020 and record Ribbon/DockablePane screenshots and Revit journal evidence.

### Task 2: Add installation and runtime identity evidence

**Files:**
- Create: `installer/Install-Revit2020.ps1`
- Create: `src/BIMBaoGui.RevitAddin/RuntimeIdentity.cs`
- Create: `src/BIMBaoGui.RevitAddin/Diagnostics/NativeStartupReportWriter.cs`
- Create: `tests/test_revit_addin_installer_contract.py`
- Create: `docs/revit-addin/acceptance/native-bootstrap-checklist.md`

**Interfaces:**
- Produces: `RuntimeIdentity.Capture(Assembly, RulePackageIdentity)` and a deterministic startup report.
- Consumes: Release DLL, `.addin` manifest, rule package identity.

- [ ] Write a failing Python contract requiring user-level Revit 2020 install and uninstall operations.
- [ ] Implement installation under `%APPDATA%\Autodesk\Revit\Addins\2020` with an absolute assembly path written into the installed manifest.
- [ ] Write a failing .NET test for product version, assembly SHA-256 and rule identity capture.
- [ ] Implement `RuntimeIdentity` without reading mutable UI state.
- [ ] Write startup diagnostics to `%LOCALAPPDATA%\BIMBaoGui\RevitAddin\Diagnostics` using atomic file publication.
- [ ] Run static contracts and Release build.
- [ ] Commit with `feat: add native add-in installer and runtime identity`.

### Task 3: Implement the Stage01 domain model and canonical payload

**Files:**
- Create: `src/BIMBaoGui.RevitAddin/Stage01/Stage01Model.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage01/Stage01FieldCatalog.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage01/Stage01Canonicalizer.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage01/Stage01Validator.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/Stage01CanonicalizerTests.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/Stage01ValidatorTests.cs`

**Interfaces:**
- Produces: `Stage01Model`, `Stage01ValidationResult`, `Stage01Canonicalizer.ToJson`, `Stage01Canonicalizer.Sha256`.
- Consumes: Stage01 fields and conditions projected from the HBR rule package.

- [ ] Create the native .NET test project with a normal ProjectReference.
- [ ] Write a failing test proving canonical JSON is stable regardless of dictionary insertion order.
- [ ] Implement ordinal sorting, invariant number formatting and strict boolean formatting.
- [ ] Write failing tests for X/Y semantics, required fields, model profile and condition applicability.
- [ ] Implement validation using the HBR package rather than a private UI registry.
- [ ] Run the targeted tests, then the full native test project.
- [ ] Commit with `feat: add native Stage01 domain contracts`.

### Task 4: Implement Stage01 Extensible Storage interoperability

**Files:**
- Create: `src/BIMBaoGui.RevitAddin/Stage01/Stage01Storage.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage01/Stage01StorageDecision.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage01/Stage01DocumentReader.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/Stage01StorageDecisionTests.cs`
- Create: `tests/test_revit_addin_stage01_storage_contract.py`

**Interfaces:**
- Produces: read/write interoperability for schema GUID `d17f35b6-f42a-4d8f-9592-c7639b8bd320` and storage name `HBR_BIMBAOGUI_STAGE01`.
- Consumes: canonical Stage01 payload and current Revit Document.

- [ ] Write failing tests locking the schema GUID, storage name and five field names.
- [ ] Implement `Read`, `Write` and corrupt/incomplete storage decisions.
- [ ] Write failing tests for current version, older migratable version and hash mismatch.
- [ ] Implement fail-closed decisions without mutating the Document during reads.
- [ ] Run static and .NET tests.
- [ ] Commit with `feat: add Stage01 storage interoperability`.

### Task 5: Implement Stage01 Revit write and readback

**Files:**
- Create: `src/BIMBaoGui.RevitAddin/Stage01/Stage01WriteRequest.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage01/Stage01RevitService.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage01/Stage01ReadbackVerifier.cs`
- Create: `src/BIMBaoGui.RevitAddin/Diagnostics/Stage01FailureReportWriter.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/Stage01WritePolicyTests.cs`

**Interfaces:**
- Produces: `Stage01RevitService.Execute(UIApplication, Stage01WriteRequest)` returning a terminal result.
- Consumes: valid model, canonical payload, active writable Revit project Document.

- [ ] Write failing policy tests for unsupported Revit version, unsaved document, family document, read-only document and corrupt storage.
- [ ] Implement preflight policies as pure methods.
- [ ] Add project units, project position, ProjectInformation, Storage and approved parameter projection in one TransactionGroup.
- [ ] Verify X writes to NorthSouth and Y writes to EastWest using unequal sentinel values.
- [ ] Add exact readback; roll back the group on any mismatch.
- [ ] Add atomic failure reporting with operation stage and rollback status.
- [ ] Run native unit tests, Release build and Revit 2020 save-close-reopen acceptance.
- [ ] Commit with `feat: complete native Stage01 initialization`.

### Task 6: Replace the Stage01 shell with a usable form

**Files:**
- Create: `src/BIMBaoGui.RevitAddin/Stage01/Stage01ViewModel.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage01/Stage01View.cs`
- Modify: `src/BIMBaoGui.RevitAddin/WorkspaceControl.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/Stage01ViewModelTests.cs`

**Interfaces:**
- Produces: a scrollable Stage01 form with left directory, inline validation and explicit write command.
- Consumes: `Stage01Model`, `Stage01Validator`, ExternalEvent dispatcher.

- [ ] Write failing ViewModel tests for group navigation, required count and edit invalidation.
- [ ] Implement one continuous vertical form with no paging.
- [ ] Mark required, optional and conditional fields distinctly.
- [ ] Show format examples and field-level validation messages.
- [ ] Wire “读取当前文件”, “校验” and “写入并回读” commands through ExternalEvent.
- [ ] Run tests and record Revit 2020 UI evidence.
- [ ] Commit with `feat: add native Stage01 workspace`.

### Task 7: Implement Stage02 full-model inventory and deterministic role matching

**Files:**
- Create: `src/BIMBaoGui.RevitAddin/Stage02/Stage02ElementSnapshot.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage02/Stage02InventoryService.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage02/Stage02RoleMatcher.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage02/Stage02Preview.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/Stage02RoleMatcherTests.cs`

**Interfaces:**
- Produces: stable element inventory and `MATCHED`, `NOT_APPLICABLE`, `NAME_NOT_MATCHED`, `NAME_AMBIGUOUS` results.
- Consumes: current FileContext, HBR carrier roles, categories, element kinds and approved exact aliases.

- [ ] Write failing tests for Unicode FormKC, trimmed/normalized whitespace and case-insensitive exact comparison.
- [ ] Write failing tests rejecting substring, edit-distance and ambiguous matches.
- [ ] Implement exact deterministic normalization and matching.
- [ ] Implement full-model inventory excluding types, annotations, view-specific items, imports and links.
- [ ] Freeze Document identity, UniqueId, ElementId, category, element kind, family and type names.
- [ ] Run unit tests and a representative Revit model scan.
- [ ] Commit with `feat: add native Stage02 model inventory`.

### Task 8: Implement Stage02 parameter and value preview

**Files:**
- Create: `src/BIMBaoGui.RevitAddin/Stage02/Stage02ParameterInspector.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage02/Stage02ValueSourcePolicy.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage02/Stage02PreviewCompiler.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/Stage02PreviewCompilerTests.cs`

**Interfaces:**
- Produces: stable preview rows and preview SHA-256 sorted by Document fingerprint, UniqueId and propertyId.
- Consumes: role matches, parameter GUID contracts, approved value sources and runtime capability.

- [ ] Write failing tests for stable sort/hash and runtime status inclusion.
- [ ] Implement binding, type, current-value and source inspection without transactions.
- [ ] Never use fixture values, names or examples as business defaults.
- [ ] Mark missing reliable sources as `PENDING_USER_VALUE` while still allowing parameter preparation.
- [ ] Run tests and verify preview generation does not modify the RVT.
- [ ] Commit with `feat: add native Stage02 preview`.

### Task 9: Implement Stage02 partial-success writes

**Files:**
- Create: `src/BIMBaoGui.RevitAddin/Stage02/Stage02ParameterPreparationService.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage02/Stage02ElementWriteService.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage02/Stage02WriteCoordinator.cs`
- Create: `src/BIMBaoGui.RevitAddin/Diagnostics/Stage02ReportWriter.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/Stage02WriteCoordinatorTests.cs`

**Interfaces:**
- Produces: parameter-level and element-level terminal results with accurate partial-success counts.
- Consumes: current preview and live Revit revalidation.

- [ ] Write failing tests proving one parameter failure affects only dependent fields.
- [ ] Write failing tests proving one element failure rolls back that element only.
- [ ] Implement one transaction per parameter preparation.
- [ ] Implement one transaction per element, with all fields of that element atomic.
- [ ] Revalidate global identity and element-level frozen identity before writes.
- [ ] Add idempotent rerun and partial-success reports.
- [ ] Run unit tests and Revit acceptance with an injected element failure.
- [ ] Commit with `feat: complete native Stage02 writes`.

### Task 10: Implement the Stage02 issue workspace

**Files:**
- Create: `src/BIMBaoGui.RevitAddin/Stage02/Stage02ViewModel.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage02/Stage02View.cs`
- Modify: `src/BIMBaoGui.RevitAddin/WorkspaceControl.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/Stage02ViewModelTests.cs`

**Interfaces:**
- Produces: filterable virtualized element/field tables and commands to select, zoom, isolate, preview, write and retry.
- Consumes: Stage02 inventory, preview and result DTOs.

- [ ] Write failing tests for status filters, selected-scope commands and stale preview handling.
- [ ] Implement category, role, level and status filters.
- [ ] Add select, zoom and temporary-isolate commands through ExternalEvent.
- [ ] Show separate counts for write success and Stage03 export readiness.
- [ ] Run tests and record usability evidence on the fixed RVT.
- [ ] Commit with `feat: add native Stage02 issue workspace`.

### Task 11: Implement Stage03 scan and gate

**Files:**
- Create: `src/BIMBaoGui.RevitAddin/Stage03/Stage03ModelScanner.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage03/Stage03GatePolicy.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage03/Stage03OutputPaths.cs`
- Create: `src/BIMBaoGui.RevitAddin/Diagnostics/Stage03FieldReportWriter.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/Stage03GatePolicyTests.cs`

**Interfaces:**
- Produces: carriers, fields, technical fatal codes, business blockers and Strict/Force gate decisions.
- Consumes: current RVT, FileContext and rule package.

- [ ] Write failing tests separating technical fatal codes from business blockers.
- [ ] Write failing tests for Strict, Force without reason, Force with reason and Force with technical fatal.
- [ ] Implement deterministic output naming and no-overwrite behavior.
- [ ] Implement full-model scan using the native Stage02 role matcher and parameter reader.
- [ ] Write fields JSON even when Strict blocks export.
- [ ] Run unit tests and Revit scan acceptance.
- [ ] Commit with `feat: add native Stage03 scan and gate`.

### Task 12: Implement robust IFC4 RAW export and H-IFC translation

**Files:**
- Create: `src/BIMBaoGui.RevitAddin/Stage03/AutodeskIfc4ExportService.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage03/IfcStepReader.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage03/HifcTranslator.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage03/HifcExactInspector.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage03/HifcPublisher.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/HifcTranslatorTests.cs`

**Interfaces:**
- Produces: immutable RAW evidence, candidate inspection and atomically published final H-IFC.
- Consumes: Stage03 enrichment values and exact owner strategies from the rule package.

- [ ] Add the latest failed real RAW IFC as a hashed regression fixture without private absolute paths.
- [ ] Write a failing test that reproduces the current translation failure at the exact substage.
- [ ] Split read, encoding, parse, structure, enrich, candidate write, reread, inspect and publish errors into stable subcodes.
- [ ] Implement RAW length/hash checks before and after translation.
- [ ] Implement candidate quarantine and atomic final publication.
- [ ] Implement exact owner/Pset/property/type/value reread.
- [ ] Run all tests and produce a successful Force three-piece artifact set in Revit 2020.
- [ ] Commit with `feat: complete native H-IFC translation`.

### Task 13: Complete native product acceptance and release

**Files:**
- Create: `docs/revit-addin/acceptance/revit2020-v1-checklist.md`
- Create: `docs/revit-addin/acceptance/evidence-schema.json`
- Create: `.github/workflows/release-revit-addin.yml`
- Modify: `installer/Install-Revit2020.ps1`

**Interfaces:**
- Produces: versioned install package, artifact manifest and machine-readable acceptance evidence.
- Consumes: all Stage01/02/03 artifacts and fixed RVT scenarios.

- [ ] Record commit SHA, DLL hash, rule identity, RVT hash and all output hashes.
- [ ] Complete Stage01 save-close-reopen acceptance.
- [ ] Complete Stage02 full-model, custom scope, partial success, retry and persistence acceptance.
- [ ] Complete Stage03 Strict blocked, Force empty reason, Force business bypass and Force technical fatal scenarios.
- [ ] Complete IFCFlux and official checker verification.
- [ ] Build signed or hash-pinned install ZIP and rollback instructions.
- [ ] Publish only after every required evidence field is populated.
- [ ] Commit with `release: prepare native Revit add-in v1`.
