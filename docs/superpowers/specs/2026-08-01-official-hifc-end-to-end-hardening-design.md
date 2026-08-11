# Official H-IFC End-to-End Hardening Design

**Status:** Approved by the user on 2026-08-01 after the repository-wide review.

## Goal

Make the `feat/gh-official-hifc-write-integration-v1` implementation safe and testable enough to produce a new Revit 2020 validation build, while separating three different claims: Revit initialization/readback, official parameter-protocol compatibility, and a real official H-IFC export verification.

## Scope and boundaries

This work has two independently verifiable tracks.

1. **Local H-IFC runtime recovery.** Diagnose the `Hust.XAR.Shell.App` startup failure, preserve every changed live file with a timestamped backup, restore the live object mapping only from the installed/side-by-side vendor baseline, restart Revit only after the open document is safe, and require a new journal with `API_SUCCESS` before calling the plugin recovered.
2. **BIMBaoGui v0.9.0 hardening.** Fix official-source alias identity, parameter semantic-type validation, Stage03 execution arming, atomic payload parsing, first-initialization versus migration confirmation, compatibility readiness, and Stage02 stale-context detection. Add deterministic unit tests for every extracted policy and keep the Revit transaction boundary unchanged.

No existing RVT or IFC is overwritten as part of unit-level work. A generated GHA is first staged beside the repository. Deployment to the Grasshopper Libraries directory uses a fixed filename and a timestamped backup. A new IFC must use a distinct validation filename until inspection passes.

## Alternatives considered

### A. Patch only the observed exceptions

This would remove `OFFICIAL_SOURCE_NAME_AMBIGUOUS`, relax the blank checkbox, and keep the current status model. It is small but unsafe: different values could silently overwrite each other, `Double` parameters could retain wrong unit semantics, and Stage02 could continue from stale or protocol-blocked context.

### B. Extract pure policies and bump the context contract (selected)

Pure net48 policies define alias identity, value-conflict resolution, semantic parameter types, execution arming, validation mode, compatibility readiness, and live-context comparison. Revit-specific services adapt Autodesk API objects into those policies. `HBR_FileContext` is bumped to `0.9.0` because compatibility becomes part of its hashed serialized contract.

This gives test-first coverage without pretending that mocks prove Revit behavior. It is the smallest design that fixes the cross-component contracts discovered in review.

### C. Replace the Revit layer with a complete adapter/fake architecture

This would make most Revit services unit-testable but is too broad for a remediation release. The design keeps this as a later architectural direction and only extracts the policy seams needed by the confirmed defects.

## Detailed design

### Official source alias identity and conflicts

The official shared-parameter GUID is derived from a stable identity:

```text
binding scope | Revit category | carrier | official exact source name
```

Property set is deliberately excluded because the official exporter reads an exact Revit parameter name, not a property-set-qualified identity. Two mappings on the same carrier with the same official name therefore share one official alias GUID.

Writes are grouped by target element plus alias GUID:

- identical normalized values collapse to one official write;
- different values fail before definitions, bindings, or values are mutated;
- canonical internal parameters remain distinct and keep their current property GUIDs.

The existing four duplicate Stage01 slots therefore work when their values agree and fail explicitly when they disagree.

### Parameter semantic types

An existing exact-name parameter is reusable only when both contracts match:

- `StorageType` class;
- full semantic type: `TEXT`, `INTEGER`, `YESNO`, `LENGTH`, `AREA`, `VOLUME`, `ANGLE`, or `NUMBER`.

The Revit adapter converts `Definition.ParameterType` to the pure contract. Value conversion and readback use the mapping-declared semantic type after compatibility has been established, preventing a wrong existing `Number` parameter from masquerading as `Length`.

### Explicit Stage03 execution

A non-persistent execution gate observes the first Solve without firing. Only a false-to-true transition observed after that initial sample may enqueue a Revit write. Opening or duplicating a GH component whose Toggle is already true does not write.

### Atomic payload parsing

`Stage01PayloadCodec.TryApply` parses into a temporary `Stage01Model`. Legacy planning-target restoration also runs on the temporary model. Only after the whole payload succeeds are the four data collections copied into the caller model. UI-only state on the caller is preserved.

### Initialization, migration, and readiness

Validation receives an explicit mode:

- `FirstInitialization` requires the blank-project confirmation and the Revit blank gate;
- `ExistingInitialization` does not require a false declaration about project blankness;
- overwrite permission remains separate from workflow migration permission.

`HBR_FileContext 0.9.0` contains both `InitializationPassed` and `OfficialProtocolCompatible`. `IsReady` and Stage02 task compilation require both. This does not claim that an IFC has been exported; official export verification remains a separate external acceptance result.

The compatibility policy currently blocks non-empty `IfcOrganization` Stage01 data because the official carrier/export contract is still unconfirmed. The same policy generates the message used by both the Revit write service and the context factory so the two layers cannot drift.

### Stage02 live freshness

The Stage02 Revit snapshot includes the current stored file GUID, payload hash, workflow version, and initialization presence. A pure freshness policy compares them with the connected context. A matching document path alone is insufficient. Read-only project documents remain valid for task-plan compilation because Stage02 performs no write.

### H-IFC runtime diagnosis

Current evidence narrows startup failure to code before deletion/rebuild of `HIFCToolExeclAttributeImport.json`: the file survives failed starts with its old timestamp, while a standalone invocation of `ReadAttrTxtFile` succeeds with 166 records. A diagnostic Revit add-in will invoke the vendor application startup under an exception boundary and write the complete exception/inner-exception chain to a timestamped log. It is temporary, user-level, and removed after diagnosis.

The 237-byte live object map is restored independently because it is inconsistent with both the installed 738-byte baseline and its live `_Bak` sibling. That repair is not presented as the startup root cause unless a post-restart journal proves it.

## Error handling and rollback

- All projection conflicts and semantic mismatches fail during preflight, before bindings or parameter values change.
- Existing Revit `TransactionGroup` rollback/readback remains the final write safety boundary.
- Live H-IFC files receive SHA-256 manifests before and after every replacement.
- GHA deployment uses `BIMBaoGui.Stage01.gha`; the prior deployed artifact is retained with a timestamp suffix until Revit/GH validation passes.
- A failed official export never overwrites the supplied `20260731test02.ifc`.

## Verification gates

1. Every new pure-policy regression test is observed failing before its implementation and passing afterward.
2. Full .NET and Python suites pass; Release build has zero warnings and errors.
3. Mapping data remains 166 rules and 166 bindings; generated manifests remain valid.
4. New GHA assembly metadata, size, and SHA-256 are recorded before deployment.
5. A fresh Revit journal shows the official vendor application and Rhino.Inside both starting successfully.
6. Opening the validation GH with Stage03 Toggle true produces no write until an observed false-to-true transition.
7. Stage01 commit/readback succeeds on a safe test copy, then the official exporter produces a new IFC.
8. The new IFC contains the expected project-level H-IFC property sets and values; an automated report records any missing, extra, or mismatched property.

## Out of scope

- Reverse-engineering or patching vendor H-IFC binaries.
- Claiming support for Revit versions other than 2020.
- Inventing an `IfcOrganization` carrier before the official vendor contract is confirmed.
- Merging or pushing the branch without a separate completion decision.
