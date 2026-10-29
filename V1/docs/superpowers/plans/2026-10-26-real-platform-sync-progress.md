# Execution ledger — plan: 2026-10-26-real-platform-sync.md

- Existing checkout/main retained under the user's prior main integration authorization; production installation remains unchanged. No source push is part of this request.
- Pre-flight: Task 1 produces verified remote identities and hashes consumed by Task 2; Task 3 must launch Task 2 without a UI timeout terminating the browser. Task 4 requires the pending source-data decision and actual platform evidence.
- Task 2 prerequisite: eight regression tests added. Four original tests and three additional tests failed as expected; backup-tampering recovery already passed through existing input verification. Implemented non-target model inventory persistence/checks, strict submit/upload receipt association, duplicate/old identity rejection, backup fsync and IFC schema/data validation. Engine suite: 29/29 passed on 2026-10-26. Fixture receipts and synthetic IFC headers updated to the same contract.
- New user-supplied entry stored through stdin/DPAPI. Read-only independent Edge probe observed TokenUserLogin ERRORTYPE=1. No platform model writes performed.
- Pending: verified actual project/scene/model reads and old IFC download, real actions and current calculation evidence, background UI integration, gate export-data decision, real single-model acceptance.

## Final candidate delivery — 2026-10-26

- The user resolved the data choice: only the export copy receives School / Middle school usage / primary-secondary school category / civil=false / 37.6 square metres, retaining Gate as the subfunction. The user confirmed the real project still remains under review and replacement is unavailable. No real model mutations or calculation submissions were made.
- Task 1 complete: independent Edge read the real project, scene and five models; verified the gate download/hash, binding and PLANNED. The authoritative local record is outputs/platform-sync-live-20261026/readonly-acceptance.json. Config remains real-readonly.
- Task 2 candidate code complete: scoped normal browser actions, persisted evidence, non-target inventory checks, true remote hashes and current calculation observation. Local browser tests cover writes/recovery; real writes remain unaccepted.
- Task 3 candidate code complete: background start --apply, durable worker status, candidate WPF start confirmation and document/export recheck. WPF RED 2 failures -> GREEN 144/144; final Revit Release build 0 warnings/errors. Installed production remains 0.4.19.
- Task 4 partial: gate copy write/save/reopen/export and freeze complete (54 host checks; 12 explicit properties; 22 nonempty mappings; 202 geometry objects). The comparison against previous delivery only changes subfunction Middle school to Gate. Real platform acceptance is deferred because the project is not editable.
- Final: fixed Important recovery-focus finding — persisted submit evidence selects actual new-model download before snapshot; same-process and restarted-browser regression RED -> GREEN. No input hash substituted for remote evidence.
- Final: fixed Important historical-response finding — request sequence must follow current calculation acceptance and completion queries must follow observed running; late-old-query regression RED -> GREEN. Evidence version 2 requires running before proof.
- Final: fixed Important permanent-read-error finding — transient HTTP 503 no longer poisons all subsequent browser reads; same-browser retry with one delete/submit only RED -> GREEN.
- Additional integration correction: local fixture declares writable=true so the candidate window can start; fixture-start assertion RED -> GREEN. Restored DOM evidence helper still required by the fixture after removing the obsolete real-readonly adapter.
- Final suite: npm test 62/62, 0 failed, 0 skipped. Log: outputs/platform-sync-live-20261026/all-candidate-tests-final.log. Previous WPF 144/144 remains applicable; no later C# edits. Final Release build log: outputs/platform-sync-live-20261026/revit-candidate-build.log.
- Documented limits: project-level calculation has no proven server task ID/timestamp; missing running observation stays incomplete; cross-machine concurrency cannot be excluded; lost dialogs may require manual recovery; real rollback remains disabled. No minor findings deferred beyond these explicit candidate acceptance limits.
- Handoff: V1/docs/HANDOFF-platform-sync-20261026.md. Updated contract, README and historical snapshot links. No commit, push, new official package or installation in this request. Preserve local evidence and the plan for later editable-project acceptance.

## Editable-project acceptance — 2026-10-26 14:47 local

- User authorized upload after the project became editable. Fresh identity/model checks and old/new IFC hashes passed.
- Live defect: visible filenames are truncated. Exact-tooltip targeting regression failed first, then passed after the selector fix.
- Live defect: upload dialogs are nested inside project windows. A nested real-browser regression failed first, then passed using the visible heading's nearest dialog.
- First attempt had no writes. The second deleted the target once but failed before file selection. Ruling: continue only the remaining upload after fresh read-only verification, retaining all old evidence and the verified backup; do not repeat deletion. This required manual debugging and is not unattended recovery evidence.
- Final run uploaded and added the new gate, completed FRAG and automatically triggered calculation. Acceptance/running/completed observed at 06:47:04/08/23 UTC. A separate browser downloaded the new IFC with the expected hash; all four non-target model identities/metadata remain unchanged.
- Final suite 64/64, 0 failures/skips. No C# changes or deployment. Original RVT, original management state and installed 0.4.19 DLL hashes unchanged.
- Evidence: outputs/platform-sync-upload-20261026-143117/. Report: V1/docs/platform-sync-live-acceptance-20261026.md. Remaining boundaries: regulation conclusion, real network-loss/unattended recovery, cross-machine concurrency, and candidate Revit-host launch acceptance.
