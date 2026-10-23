export type State = 'QUEUED'|'PRECHECK'|'PLANNED'|'DELETING'|'UPLOADING'|'SUBMITTING'|'CONVERTING'|'CALCULATING'|'VERIFYING'|'SUCCEEDED'|'NO_CHANGE'|'BLOCKED'|'NEEDS_LOGIN'|'FAILED'|'UNKNOWN_RESULT'|'PAUSED'|'CANCELLED'|'ROLLED_BACK';
export interface Handoff {
  schema: 'hifc.platform-sync-handoff/1'; export_id: string; created_utc: string;
  document_path: string; project_unique_id: string; original_name: string;
  ifc_file: string; receipt_file: string; ifc_sha256: string; receipt_sha256: string;
}
export interface Model { id: string; name: string; generation: string; sha256?: string }
export interface Snapshot {
  authenticated: boolean; project_id: string; project_code: string; project_name: string;
  scene_project_id: string; revision: string; models: Model[];
}
export interface Binding {
  id: string; project_id: string; project_code: string; project_name: string;
  remote_model_id: string; remote_model_name: string; baseline_revision: string;
  document_path: string; project_unique_id: string; last_sha256?: string;
}
export interface Backup { bytes: Buffer; model_id: string; generation: string; sha256: string }
export interface Progress {
  operation_id: string; model_id: string; generation: string; input_sha256: string;
  conversion: 'pending'|'completed'|'failed'; calculation: 'not_started'|'running'|'completed'|'failed';
  regulation: 'unknown'|'passed'|'failed'; result_id?: string;
}
export interface Operation {
  id: string; kind: 'delete'|'upload'|'submit'; status: 'absent'|'pending'|'completed'|'failed';
  model_id?: string; generation?: string; upload_id?: string; sha256?: string;
}
export interface PlatformAdapter {
  readonly mode: 'fixture'|'real-readonly';
  snapshot(): Promise<Snapshot>;
  backup(model: Model): Promise<Backup>;
  deleteModel(model: Model, operationId: string): Promise<void>;
  upload(file: string, sha256: string, operationId: string): Promise<{upload_id: string}>;
  submit(uploadId: string, name: string, operationId: string): Promise<{model_id: string; generation: string}>;
  progress(operationId: string): Promise<Progress>;
  operation(operationId: string): Promise<Operation>;
  close(): Promise<void>;
}
export interface Run {
  id: string; binding_id: string; project_id: string; state: State; reason: string;
  created_utc: string; updated_utc: string; handoff: Handoff; manifest_path: string;
  baseline: Model; expected_project: Pick<Snapshot,'project_id'|'project_code'|'project_name'>;
  idempotency_key: string; upload_id?: string; new_model_id?: string; new_generation?: string;
  calculation_status: Progress['calculation']; regulation_status: Progress['regulation']; result_id?: string;
  backup_path?: string; backup_sha256?: string; intent?: {kind: Operation['kind'];operation_id: string;acknowledged?: boolean};
  rollback_of?: string;
}
export interface Config {
  schema: 'hifc.platform-sync-config/1'; adapter: 'fixture'|'real-readonly';
  state_dir: string; browser_profile: string; entry_secret_file?: string; fixture_url?: string;
  expected_project?: {project_id: string;project_code: string;project_name: string};
  headless?: boolean;
}
export class SyncError extends Error {
  constructor(public readonly code: 'CONFIG'|'LOGIN'|'BLOCKED'|'UNKNOWN'|'FAILED'|'BUSY', message: string) { super(message); }
}
