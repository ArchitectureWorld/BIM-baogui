# Revit Native MCP v0.4.2 Completion Design

- Date: 2026-08-14
- Status: approved
- Target branch: `feat/revit-stage02-manual-semantic-v0.4.2`
- Starting commit: `2d45d17298bbf988086bf4b0fa8d572ae8b4dec1`
- Upstream baseline: `df4a86a63fd2f0fd6a4b3b2ccd324babed4655f3`
- Product target: Autodesk Revit 2020, .NET Framework 4.8

## 1. Goal

Complete the existing v0.4.2 development branch without rewriting its 42
commits. The finished branch must provide deterministic Stage02 Preview V2,
RVT-persisted semantic assignments with atomic readback, one shared manual/MCP
workflow, Stage03 object-owner resolution by Revit export GUID, unified 0.4.2
product identity, green Windows CI, installer smoke evidence, and the artifact
`BIMBaoGui-Revit2020-Native-MCP-v0.4.2`.

## 2. Repository and recovery boundary

The development continues in an isolated worktree tracking the existing remote
branch. The dirty `main` checkout is out of scope and must remain untouched.
The existing v0.4.2 commits are retained in order; missing behavior is added as
new reviewable commits. No history rewrite, squash, force-push, or transplant
onto the mislabeled PR #5 merge is part of this work.

The branch baseline is intentionally red: the native domain suite has 149 tests,
with 148 passing and one stale carrier-role count assertion expecting 14 instead
of the v0.4.2 rule pack's 15. All later completion claims require fresh local and
GitHub evidence.

## 3. Stage02 Preview V2 canonical contract

`HBR_NATIVE_STAGE02_PREVIEW_V2` is the only preview schema emitted by the
completed branch. Its canonical JSON freezes all state that can change the
write result:

- rule package ID, version, and SHA-256;
- Revit document fingerprint and model profile;
- identification mode;
- normalized bulk role ID;
- per-element overrides sorted by element UniqueId;
- project conditions sorted by ordinal key;
- element plans sorted by UniqueId;
- automatic and effective role evidence;
- assignment mode, source, action, and manual carrier evidence;
- parameter GUID, binding action, value action, current value, proposed value,
  value source, runtime status, and blocker state.

Canonicalization uses invariant values, ordinal ordering, UTF-8 without BOM,
and SHA-256 lowercase hexadecimal output. Presentation-only text, row order,
timestamps, and WPF selection state do not influence the hash.

Before any confirmed write, the service rebuilds the preview from live Revit
state and compares its hash with the confirmed hash. A mismatch returns
`REQUIRES_NEW_PREVIEW` and performs no write.

## 4. Assignment persistence and transaction boundary

Manual semantic assignments belong to the RVT and use document-level
`DataStorage + Extensible Storage`. The payload schema remains
`HBR_STAGE02_ASSIGNMENTS_V1`; records are keyed by Revit element UniqueId and
canonicalized in ordinal UniqueId order. The storage entity contains canonical
JSON and its SHA-256. Reads validate schema, canonical form, duplicate identity,
and hash before exposing records.

The service supports these explicit operations:

- create a manual assignment when no record exists;
- update the role or carrier evidence for an existing record;
- delete the manual record when the element is restored to automatic mode;
- preserve an unchanged verified manual record without rewriting it.

Atomicity is per Revit element, not across the whole batch. For one element,
parameter changes and assignment create/update/delete happen in the same Revit
transaction. The service then regenerates the document, reads parameter values
back by fixed GUID, rereads the assignment payload, revalidates its canonical
hash, and compares the effective record with the preview plan. Any mismatch or
storage failure rolls back that element transaction. Other elements may proceed,
and the batch result reports partial success with stable error codes.

No assignment-only action may be skipped merely because the element has no
parameter value write. Conversely, parameter binding or value failure prevents
the assignment change from committing.

## 5. Manual workbench and shared application service

The Stage02 workbench exposes two identification modes:

- Automatic: strict existing recognition; no current bulk or row override is
  applied.
- Manual: one optional bulk semantic role plus per-element overrides.

The selected-element table displays category, family/type, automatic role,
saved role, effective role, assignment source, assignment action, field status,
and blocker reason. A bulk choice applies to all eligible selected rows. A row
override wins over the bulk role. Choosing the automatic sentinel for a row
removes its saved manual assignment after confirmation. Any edit invalidates the
old preview and requires regeneration before confirmation.

The WPF view is an adapter only. Request validation, role precedence, carrier
authorization, preview compilation, write orchestration, and readback live in
shared services that are also used by MCP.

## 6. MCP controlled surface

MCP adds controlled Stage02 semantic-assignment inputs to the existing preview
and confirm/write flow. It accepts identification mode, bulk role, and explicit
per-element overrides identified by UniqueId. It returns Preview V2 canonical
identity, hash, element plans, and stable blockers.

MCP cannot bypass carrier policy, Stage01 conditions, rule identity, document
identity, preview freshness, parameter contracts, Revit transactions, or atomic
readback. Mutating calls require the same explicit confirmation and matching
preview hash as the manual workbench. No independent MCP-only assignment store
or write path is allowed.

## 7. Stage03 green-object owner consumption

Stage03 reads only validated saved assignments whose effective role is
`SITE_GREEN_OBJECT`. Each assigned Revit element is projected to its Revit
export GUID and encoded as an IFC GlobalId using the existing approved codec.
The RAW IFC graph must contain exactly one compatible owner for that GlobalId.

The resolved owner is carried through RAW inspection, H-IFC translation, final
inspection, and field evidence. Missing owners, duplicate owners, malformed
GUIDs, entity incompatibility, or RAW/final owner drift fail closed with stable
technical statuses. Stage03 must not collapse multiple green objects into the
single site-level `IfcSite`, guess by name, or use owner-by-entity fallback.

The existing site-level summary data remains independent from object-level green
records.

## 8. Product identity and installer

All shipped assemblies and executable surfaces use product version `0.4.2`:

- `BIMBaoGui.RevitAddin`;
- `BIMBaoGui.McpContracts`;
- `BIMBaoGui.McpServer`;
- `BIMBaoGui.HifcCore` where product identity is emitted;
- installer paths, evidence, examples, probes, README, workflow assertions, and
  artifact names.

Assembly versions are `0.4.2.0`. Informational versions retain the existing
build-number and commit-SHA metadata. The installer writes the MCP server to the
versioned `0.4.2` directory, removes superseded version directories under its
controlled root, and records source and installed SHA-256 values. Uninstall
removes only the product manifest, controlled add-in directory, 0.4.2 MCP
directory, generated MCP config, and empty controlled parent directories.

## 9. Error behavior

All technical ambiguity fails closed before mutation. Required stable outcomes
include invalid assignment payload/hash, stale preview, unauthorized carrier,
inactive condition, element/document identity drift, parameter contract drift,
assignment readback mismatch, export GUID failure, IFC owner not found,
duplicate IFC owner, and final owner drift.

Reports retain element UniqueId, ElementId when available, role identity,
assignment action, preview hash, rule package identity, and redacted evidence.
No exception message or report may expose local credentials or unrelated user
paths.

## 10. Verification and release evidence

Implementation follows test-first red-green-refactor cycles. The final gate is:

1. Python contract and rule-pack suites pass.
2. H-IFC core, MCP contracts, native Revit add-in, MCP server, SDK smoke, and
   all .NET test projects pass in Release.
3. Release builds produce zero warnings and zero errors.
4. Preview V2 canonical/hash mutation cases, assignment CRUD/readback rollback,
   UI batch/row behavior, MCP parity, and Stage03 export-GUID owner cases are
   covered by behavior tests.
5. The installer is exercised with isolated `APPDATA` and `LOCALAPPDATA`, the
   installed MCP probe is run, assembly versions and hashes are checked, and
   uninstall leaves no controlled product state.
6. GitHub `Build BIMBaoGui Revit MCP` succeeds on Windows through artifact
   upload.
7. The published directory is named
   `BIMBaoGui-Revit2020-Native-MCP-v0.4.2` and contains the installer entry
   points, add-in, MCP server, contracts, H-IFC core, documentation,
   `SHA256SUMS.txt`, and install evidence.

Real Revit 2020 model interaction and IFCFlux acceptance remain separately
reported host gates unless concrete host evidence is produced during this work.
Automated CI and isolated installer smoke are not described as official IFCFlux
acceptance.

## 11. Non-goals

- Rewriting or cleaning unrelated Stage01/GHA history.
- Changing the existing site-level aggregation model.
- Adding arbitrary user-defined semantic roles or free-form Pset composition.
- Guessing IFC owners by display name, category, entity count, or spatial site.
- Modifying the dirty main checkout or unrelated local worktrees.
