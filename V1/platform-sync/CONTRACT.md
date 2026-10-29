# Platform sync candidate 0.2.0-candidate.1 contract

This describes candidate source, not an installed release. Installed Revit remains 0.4.19; its published sync program is 0.1.0 and real-platform read-only. The candidate adds explicit real mode and background execution. On 2026-10-26, real project/model reads, old IFC download, binding and planning passed. The project is under review; no real deletion, upload, replacement or calculation was performed.

## Export handoff

A frozen directory is unique per export; handoff.json is published atomically last. UTF-8 JSON:

```json
{
  "schema": "hifc.platform-sync-handoff/1",
  "export_id": "UUID",
  "created_utc": "ISO8601 UTC",
  "document_path": "absolute saved RVT path",
  "project_unique_id": "Revit ProjectInformation.UniqueId",
  "original_name": "OUT_project_stage_building-or-site_YYYYMMDD_V1.ifc",
  "ifc_file": "model.ifc",
  "receipt_file": "native.receipt.json",
  "ifc_sha256": "lowercase SHA256",
  "receipt_sha256": "lowercase SHA256"
}
```

Receipt must be hifc.native-export-receipt/1.5 or /1.6, with matching export_id and ifc_sha256, equal/nonempty source snapshots, model_transaction=RolledBack, export_scope=CURRENT_PROJECT, doc_export_calls=1, ifc_postprocessed=false, original_export_server_restored=true, and temporary_type_schedules_rolled_back=true.

Revit registers success only after all export checks and receipt writing return. File existence alone is insufficient. Frozen files stay within the manifest directory without traversal or links. The worker copies into its private spool, verifies both hashes, and rechecks before use. A failed handoff never invalidates an otherwise successful ordinary IFC export.

## Identity and planning

Binding fields: id, project_id, project_code, project_name, remote_model_id, remote_model_name, baseline_revision, document_path, project_unique_id, last_sha256?.

The exact remote model ID/full name, project identity and source path/unique ID must match. Dates and filename version increments do not change the binding. Save As/copy requires explicit rebinding. Missing or duplicate models never trigger automatic creation.

Snapshot contains authenticated, project_id, project_code, project_name, scene_project_id, revision, models[], plus writable and approval_state for verified adapters. Each model contains id, name, generation, sha256?. Real identity reconciles the visible project form, QueryProjectSign and current scene. Real generation is record-sha256:<metadata fingerprint>, not a server revision. Only the selected target is downloaded for its hash; non-target protection compares complete identity/metadata inventory.

A Run persists the frozen handoff, target baseline, non_target_models, expected project and idempotency key. Legacy plans without a non-target baseline cannot write. Real writes require fresh writable===true; approval states 1/3/4 and unknown states are rejected. Each write boundary checks identity and inventory again.

## CLI and window

Arguments carry paths and IDs, never credentials:

```text
node dist/src/cli.js setup --config config.json
node dist/src/cli.js inspect --config config.json
node dist/src/cli.js bind --config config.json --model MODEL_ID --handoff handoff.json
node dist/src/cli.js plan --config config.json --binding BINDING_ID --handoff handoff.json
node dist/src/cli.js start --config config.json --run RUN_ID --apply
node dist/src/cli.js run --config config.json --run RUN_ID --apply
node dist/src/cli.js status --config config.json
node dist/src/cli.js resume --config config.json --run RUN_ID
node dist/src/cli.js rollback --config config.json --run RUN_ID --apply
```

- setup defaults to real-readonly. Credential initialization/refresh uses stdin and Windows DPAPI while configured read-only; no plaintext secrets in config or process arguments. Relative paths resolve against the config directory.
- inspect/bind/plan/status/resume never initiate platform model writes. status needs no browser and returns {bindings:[],runs:[]}. resume only reconciles persisted intent/evidence.
- start/run require explicit --apply, a saved plan and either fixture or explicit candidate real mode. Read-only mode rejects both. start returns {started,run_id,pid,created_utc} after launching a detached worker; it is not a success receipt.
- The apply worker retains the browser during uncertain outcomes and reads evidence before proceeding to a previously unexecuted next step. It never repeats an uncertain delete/upload/submit. Closing the launcher/window does not kill the worker.
- Real rollback, batch, file watching and unattended use remain disabled. Fixture rollback requires explicit invocation, a verified completed run and old-file backup.
- Success replies are UTF-8 JSON. Errors are sanitized with exit codes 2 config, 3 login, 4 blocked/paused, 5 unknown, 6 failed, 7 busy. Code 0 also includes planned/no-change; inspect state before reporting completion.

The candidate window first previews a plan. “确认并开始同步” rechecks active document, ProjectInformation.UniqueId, current export ID and fresh project identity/editability, then shows the exact replacement for confirmation. Configuration/binding changes invalidate the pending plan. Status reads do not run browser mutations. Ordinary export remains independent.

## Writes and recovery

sync.sqlite holds runs, bindings, events and a project lock. real-evidence.sqlite uses WAL/FULL and stores bounded local request/response evidence, without raw authentication headers or request bodies. Worker metadata/logs are under state_dir/workers. Locks cover only that state directory, not other computers or other state directories.

Delete requires a real downloaded IFC backup, matched to target generation/hash, validated as STEP/IFC and fsynced before mutation. Each write has a persisted intent. The browser uses normal delete confirmation, file chooser and add confirmation. Routes restrict writes to the expected operation, project, model, name and uploaded path. Upload success includes an independent download of the platform file and hash verification.

Submitted evidence must correlate the uploaded file and returned new model ID. If the process fails before Run.new_model_id is saved, recovery selects that proven model and downloads its actual bytes before accepting the snapshot. It does not substitute the input hash for a remote hash. If the original dialog or conclusive evidence is lost, the run remains unknown; automatic recovery is deliberately incomplete.

Transient read failures can be retried without poisoning the browser permanently. Uncertain or unmatched writes retain their error/evidence and are not replayed. No TTL-based lock stealing is permitted.

## Calculation evidence and completion

The normal page must request new-model geometry and submit BindProject_IFCThree with verified project identity and unambiguous model prefixes in 1000/1001.json. A response of "1" means accepted only. Subsequent GetCadRecordInfo subtype 5 must observe running then completed. Queries sent before acceptance are excluded; completion queries must also start after the running observation. Persisted evidence version 2 records this ordering. Old evidence is not promoted to proof.

result_id=observed-save:<hash> is a local observation fingerprint, not a server-issued task ID. The real service has not yet exposed a proven per-run result ID/timestamp. A concurrent calculation from another computer cannot be fully excluded by local ordering. If running is too brief to observe, retain an incomplete state; do not infer success from old text.

Success requires the unique new model, absent old target, unchanged non-target inventory, downloaded input hash match, conversion success, current calculation proof and no visible processing overlay. Calculation and regulation are separate fields; regulation remains unknown unless independently proven.

States: QUEUED PRECHECK PLANNED DELETING UPLOADING SUBMITTING CONVERTING CALCULATING VERIFYING SUCCEEDED NO_CHANGE BLOCKED NEEDS_LOGIN FAILED UNKNOWN_RESULT PAUSED CANCELLED ROLLED_BACK. Only verified success advances the binding baseline. Local browser simulation is not real-platform acceptance.

## Comprehensive report delivery

After a verified successful run/resume, ReportService may generate and download the native comprehensive PDF. This does not repeat any model mutation or calculation. `report-start` launches the independent report worker; `report` downloads or recovers a frozen pair. A live original apply worker blocks a second report worker. UI retry remains available after interrupted PENDING/DOWNLOADING/PUBLISHING states; the worker and project lock enforce exclusion.

Run.report is independent of Run.state. It records state, reason, generated/downloaded/calculation times, actual calculation model identities, PDF/TXT paths, SHA-256 and frozen artifacts. Publication intent is durable before either output appears. Each destination uses a no-overwrite atomic single-file publication; a crash between PDF and TXT is recovered from the frozen pair. Modified destination bytes are not replaced. Report failure does not demote a successful model sync.

The current project, result fingerprint and full model inventory must still match persisted completion evidence. The report's listed models come from calculation_models, not an arbitrary current inventory. Only the verified project-specific native PDF path is accepted, with recordType 3 and a PDF creation timestamp consistent with this calculation. Completion re-reads do not move the original completion time. These checks do not provide a distributed platform lock.

Destination is the task's source RVT directory, not the currently active document. DOC_<project>_<local YYYYMMDD>_Vn.pdf and .txt share one version; either existing extension reserves that version. New archive dates restart at V1; retries reuse frozen paths. Missing source documents block delivery instead of guessing a new path. The TXT uses UTF-8 BOM and records all actual IFC model names and times. Calculation success, regulation outcome and report delivery are distinct.
