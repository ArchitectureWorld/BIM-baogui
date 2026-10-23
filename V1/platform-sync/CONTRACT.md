# Platform sync preview 0.1.0 wire contract

This preview adds source capability to V1 0.4.16 without installing it. Real platform adapter is read-only in this release. Local fixture supports full apply/recovery.

Frozen export directory is unique per export. `handoff.json` is atomically published last. UTF-8 JSON:

```json
{
  "schema": "hifc.platform-sync-handoff/1",
  "export_id": "UUID",
  "created_utc": "ISO8601 UTC",
  "document_path": "absolute saved RVT path",
  "project_unique_id": "Revit ProjectInformation.UniqueId",
  "original_name": "OUT_project_stage_type_date_V1.ifc",
  "ifc_file": "model.ifc",
  "receipt_file": "native.receipt.json",
  "ifc_sha256": "lowercase SHA256",
  "receipt_sha256": "lowercase SHA256"
}
```

Receipt must be `hifc.native-export-receipt/1.5` (or /1.6), include matching `export_id`, `ifc_sha256`, source snapshots equal/nonempty, `model_transaction=RolledBack`, `export_scope=CURRENT_PROJECT`, `doc_export_calls=1`, `ifc_postprocessed=false`, `original_export_server_restored=true`, `temporary_type_schedules_rolled_back=true`. Revit registers completed export only AFTER receipt write and all checks return. No queue after failed exports. Freeze then re-read/verify both hashes. Frozen files stay inside manifest directory, no traversal or links. Worker copies again into its private spool and verifies on use.

Binding: `{id, project_id, project_code, project_name, remote_model_id, remote_model_name, baseline_revision, document_path, project_unique_id, last_sha256?}`. Created from inspected model list, exact names/IDs and project; path plus unique ID must match the handoff. Renamed IFCs don't affect binding. Save As/copy needs explicit rebind. Never auto-create a remote model on missing binding.

CLI arguments use paths/IDs only, no secrets:

```
node dist/cli.js setup --config config.json
node dist/cli.js inspect --config config.json
node dist/cli.js bind --config config.json --model MODEL_ID --handoff handoff.json
node dist/cli.js plan --config config.json --binding BINDING_ID --handoff handoff.json
node dist/cli.js run --config config.json --run RUN_ID --apply
node dist/cli.js status --config config.json
node dist/cli.js resume --config config.json --run RUN_ID
node dist/cli.js rollback --config config.json --run RUN_ID --apply
```

`setup` writes a local config; credentials are never in JSON config. `inspect`, `bind`, `plan`, `status`, `resume` (reconcile only) cannot initiate platform writes. `run` and `rollback` reject real adapter in preview. Config-relative paths resolve relative to config file, never cwd. All successful CLI replies are UTF-8 JSON; failures use sanitized messages and documented exit codes. `status` returns `{bindings:[],runs:[]}`. `inspect` returns `{project_id,project_code,project_name,scene_project_id,revision,models:[{id,name,generation,sha256?}]}`. `plan` returns a persisted run with `id,state,reason,calculation_status,regulation_status`.

UI uses saved config path + worker node/entry paths; only `plan` from “同步本次 IFC”, clearly labeled preview/read-only. Normal export remains independent. UI selects bindings from worker `status`; no typed internal IDs. A failed CLI/freeze means export succeeded but sync not queued. Source must match binding; stale completed export from another active document is blocked.

States: QUEUED PRECHECK PLANNED DELETING UPLOADING SUBMITTING CONVERTING CALCULATING VERIFYING SUCCEEDED NO_CHANGE BLOCKED NEEDS_LOGIN FAILED UNKNOWN_RESULT PAUSED CANCELLED ROLLED_BACK. Separate calculation_status and regulation_status. Locks per project in SQLite, never TTL-steal unknown tasks. Intent before each side-effect, uncertain write outcome stays UNKNOWN_RESULT until observed. Baseline advances only on verified success.
