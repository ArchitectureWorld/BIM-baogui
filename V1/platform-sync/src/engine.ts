import {randomUUID} from 'node:crypto';
import {readFileSync,writeFileSync,mkdirSync,existsSync} from 'node:fs';
import {join} from 'node:path';
import {Store} from './store.js';
import {verifyHandoff,freezeHandoff,documentKey,assertIfc} from './handoff.js';
import {hash,redact} from './security.js';
import {SyncError,type Binding,type Model,type PlatformAdapter,type Run,type Snapshot,type State} from './types.js';

type Expected=Pick<Snapshot,'project_id'|'project_code'|'project_name'>;
const done=new Set<State>(['SUCCEEDED','ROLLED_BACK','NO_CHANGE']);
const wait=(ms:number)=>new Promise<void>(resolve=>setTimeout(resolve,ms));
export class SyncEngine {
 constructor(readonly store:Store,readonly adapter:PlatformAdapter,private options={pollMs:2000,timeoutMs:60*60*1000}){}
 private project(s:Snapshot,e:Expected){
  if(!s.authenticated)throw new SyncError('LOGIN','Platform needs login.');
  if(s.project_id!==e.project_id||s.project_code!==e.project_code||s.project_name!==e.project_name)throw new SyncError('BLOCKED','Platform project identity mismatch.');
  if(s.scene_project_id!==e.project_id)throw new SyncError('BLOCKED','Active scene project identity mismatch.');
 }
 private match(s:Snapshot,b:Binding):Model {
  const rows=s.models.filter(m=>m.id===b.remote_model_id&&m.name===b.remote_model_name);
  if(rows.length!==1||s.models.filter(m=>m.id===b.remote_model_id).length!==1||s.models.filter(m=>m.name===b.remote_model_name).length!==1)throw new SyncError('BLOCKED','Remote model is missing, renamed or ambiguous; inspect and rebind.');
  const m=rows[0]!;if(m.generation!==b.baseline_revision)throw new SyncError('BLOCKED','Remote model generation changed; inspect and rebind.');if(b.last_sha256&&m.sha256!==b.last_sha256)throw new SyncError('BLOCKED','Remote hash no longer matches the successful baseline.');return m;
 }
 async bind(modelId:string,manifest:string,expected:Expected):Promise<Binding>{
  const {handoff:h}=verifyHandoff(manifest),s=await this.adapter.snapshot();this.project(s,expected);
  const matches=s.models.filter(m=>m.id===modelId);if(matches.length!==1)throw new SyncError('BLOCKED','Model selection is ambiguous or missing.');
  const m=matches[0]!;if(!m.generation||s.models.filter(x=>x.name===m.name).length!==1)throw new SyncError('BLOCKED','Full model name/generation must be unique and verified.');
  if(this.store.runs().some(r=>r.project_id===expected.project_id&&['DELETING','UPLOADING','SUBMITTING','CONVERTING','CALCULATING','VERIFYING','UNKNOWN_RESULT','PAUSED'].includes(r.state)))throw new SyncError('BUSY','Resolve the pending project run before rebinding.');
  const b:Binding={id:randomUUID(),...expected,remote_model_id:m.id,remote_model_name:m.name,baseline_revision:m.generation,document_path:h.document_path,project_unique_id:h.project_unique_id};this.store.putBinding(b);return b;
 }
 async plan(bindingId:string,manifest:string):Promise<Run>{
  const b=this.store.binding(bindingId),v=verifyHandoff(manifest),s=await this.adapter.snapshot();this.project(s,b);
  if(documentKey(b.document_path)!==documentKey(v.handoff.document_path)||b.project_unique_id!==v.handoff.project_unique_id)throw new SyncError('BLOCKED','Source document changed (Save As/copy). Explicit rebind required.');
  const baseline=this.match(s,b);
  if(s.models.some(m=>m.id!==baseline.id&&m.name===v.handoff.original_name))throw new SyncError('BLOCKED','Target filename belongs to another model.');
  const key=hash([bindingId,b.project_id,b.baseline_revision,v.handoff.ifc_sha256].join('|'));
  const existing=this.store.runs().find(r=>r.idempotency_key===key&&!['FAILED','BLOCKED','CANCELLED','SUCCEEDED','ROLLED_BACK','NO_CHANGE'].includes(r.state));if(existing)return existing;
  const frozen=freezeHandoff(manifest,join(this.store.directory,'spool'));
  const now=new Date().toISOString(),r:Run={id:randomUUID(),binding_id:bindingId,project_id:b.project_id,state:b.last_sha256===v.handoff.ifc_sha256?'NO_CHANGE':'PLANNED',reason:'Plan only; no platform writes.',created_utc:now,updated_utc:now,handoff:v.handoff,manifest_path:frozen,baseline,expected_project:{project_id:b.project_id,project_code:b.project_code,project_name:b.project_name},idempotency_key:key,calculation_status:'not_started',regulation_status:'unknown'};
  this.store.save(r);return r;
 }
 private input(r:Run):string {
  if(r.backup_path||r.backup_sha256)this.verifiedBackup(r);
  if(r.rollback_of){const previous=this.store.run(r.rollback_of);if(!previous.backup_path||!previous.backup_sha256||previous.backup_sha256!==r.handoff.ifc_sha256)throw new SyncError('BLOCKED','Verified rollback backup missing.');const bytes=readFileSync(previous.backup_path);assertIfc(bytes);if(hash(bytes)!==previous.backup_sha256)throw new SyncError('BLOCKED','Rollback backup hash changed.');return previous.backup_path;}
  const v=verifyHandoff(r.manifest_path);if(v.handoff.export_id!==r.handoff.export_id||v.handoff.ifc_sha256!==r.handoff.ifc_sha256||v.handoff.receipt_sha256!==r.handoff.receipt_sha256||v.handoff.original_name!==r.handoff.original_name||documentKey(v.handoff.document_path)!==documentKey(r.handoff.document_path)||v.handoff.project_unique_id!==r.handoff.project_unique_id)throw new SyncError('BLOCKED','Run handoff identity/hash changed.');return v.ifcPath;
 }
 private transition(r:Run,state:State,reason:string){r.state=state;r.reason=redact(reason);this.store.save(r);}
 private intent(r:Run,kind:'delete'|'upload'|'submit',state:State){r.state=state;r.intent={kind,operation_id:r.id+':'+kind,acknowledged:false};r.reason='Action intent persisted before request.';this.store.save(r,'intent');return r.intent.operation_id;}
 private verifiedBackup(r:Run){
  if(!r.backup_path||!r.backup_sha256||!r.baseline.sha256||r.backup_sha256!==r.baseline.sha256)throw new SyncError('BLOCKED','Verified previous IFC backup metadata is missing or mismatched.');
  const bytes=readFileSync(r.backup_path);assertIfc(bytes);if(hash(bytes)!==r.backup_sha256)throw new SyncError('BLOCKED','Previous IFC backup hash changed; further writes are blocked.');
 }
 private async backup(r:Run){
  if(r.backup_path||r.backup_sha256){this.verifiedBackup(r);return;}
  const backup=await this.adapter.backup(r.baseline);assertIfc(backup.bytes);
  if(!r.baseline.sha256||backup.model_id!==r.baseline.id||backup.generation!==r.baseline.generation||backup.sha256!==r.baseline.sha256||hash(backup.bytes)!==backup.sha256)throw new SyncError('BLOCKED','Old IFC backup cannot be verified against the current remote generation.');
  const dir=join(this.store.directory,'backups',r.id);mkdirSync(dir,{recursive:true});r.backup_path=join(dir,'previous.ifc');r.backup_sha256=backup.sha256;
  // A crash may leave bytes published just before the database record. Never overwrite
  // them: re-download proves current correspondence, then accept only the same hash.
  if(!existsSync(r.backup_path))writeFileSync(r.backup_path,backup.bytes,{flag:'wx'});
  this.verifiedBackup(r);this.store.save(r,'backup');
 }
 private async beforeWrite(r:Run,expectOld:boolean){
  this.input(r);if(!expectOld)this.verifiedBackup(r);const s=await this.adapter.snapshot();this.project(s,r.expected_project);
  if(expectOld){const rows=s.models.filter(m=>m.id===r.baseline.id);if(rows.length!==1||rows[0]!.generation!==r.baseline.generation||rows[0]!.name!==r.baseline.name||rows[0]!.sha256!==r.baseline.sha256)throw new SyncError('BLOCKED','Remote generation changed since plan.');}
  else if(s.models.some(m=>m.id===r.baseline.id))throw new SyncError('BLOCKED','Old model unexpectedly reappeared; reconcile first.');
  if(s.models.some(m=>m.id!==r.baseline.id&&m.name===r.handoff.original_name))throw new SyncError('BLOCKED','Target filename collision appeared before write.');
 }
 async run(runId:string):Promise<Run>{
  if(this.adapter.mode!=='fixture')throw new SyncError('BLOCKED','Real platform is read-only in this preview; apply is disabled.');
  let r=this.store.run(runId);if(done.has(r.state))return r;
  if(r.intent&&!r.intent.acknowledged)throw new SyncError('BLOCKED','An in-flight request requires readonly reconciliation before continuing.');
  if(!['PLANNED','UPLOADING','SUBMITTING','CONVERTING','CALCULATING','VERIFYING'].includes(r.state))throw new SyncError('BLOCKED','Run requires readonly reconciliation before continuing.');
  this.store.acquire(r.project_id,r.id,true);let destructive=!!r.intent;
  try{
   this.input(r);
   if(r.state==='PLANNED'){
    this.transition(r,'PRECHECK','Verify source, remote identity and old IFC backup.');await this.beforeWrite(r,true);await this.backup(r);await this.beforeWrite(r,true);
    if(hash(readFileSync(r.backup_path!))!==r.backup_sha256)throw new SyncError('BLOCKED','Backup changed before delete.');
    const op=this.intent(r,'delete','DELETING');destructive=true;await this.adapter.deleteModel(r.baseline,op);r.intent!.acknowledged=true;this.transition(r,'UPLOADING','Old model deletion acknowledged.');
   }
   if(r.state==='UPLOADING'){
    await this.beforeWrite(r,false);const op=this.intent(r,'upload','UPLOADING');const reply=await this.adapter.upload(this.input(r),r.handoff.ifc_sha256,op);
    if(!reply.upload_id)throw new SyncError('UNKNOWN','Upload receipt missing.');r.upload_id=reply.upload_id;r.intent!.acknowledged=true;this.transition(r,'SUBMITTING','Upload acknowledged; submit has not been sent.');
   }
   if(r.state==='SUBMITTING'){
    await this.beforeWrite(r,false);if(!r.upload_id)throw new SyncError('UNKNOWN','Upload identity missing.');const op=this.intent(r,'submit','SUBMITTING');const reply=await this.adapter.submit(r.upload_id,r.handoff.original_name,op);
    if(!reply.model_id||!reply.generation||reply.model_id===r.baseline.id&&reply.generation===r.baseline.generation)throw new SyncError('UNKNOWN','New model generation was not proven.');r.new_model_id=reply.model_id;r.new_generation=reply.generation;r.intent!.acknowledged=true;this.transition(r,'CONVERTING','Submitted; keep browser open during conversion.');
   }
   await this.observe(r,true);
  }catch(e){const error=e as Error;this.transition(r,destructive?'UNKNOWN_RESULT':e instanceof SyncError&&e.code==='LOGIN'?'NEEDS_LOGIN':'BLOCKED',redact(error.message));}
  finally{if(done.has(r.state)||!destructive)this.store.release(r.project_id,r.id);else this.store.retain(r.project_id,r.id);}
  return this.store.run(r.id);
 }
 private async observe(r:Run,waitForFinish:boolean){
  const start=Date.now();do{
   this.input(r);const s=await this.adapter.snapshot();this.project(s,r.expected_project);
   const p=await this.adapter.progress(r.id+':submit');
   if(p.operation_id!==r.id+':submit'||p.model_id!==r.new_model_id||p.generation!==r.new_generation||p.input_sha256!==r.handoff.ifc_sha256){this.transition(r,'UNKNOWN_RESULT','Completion belongs to another run/model/generation.');return;}
   r.calculation_status=p.calculation;r.regulation_status=p.regulation;
   if(p.conversion==='failed'||p.calculation==='failed'){this.transition(r,'PAUSED','Conversion/calculation failed; no automatic rollback or deletion.');return;}
   if(p.conversion==='completed'&&p.calculation==='completed'&&p.result_id){
    const rows=s.models.filter(m=>m.id===r.new_model_id&&m.generation===r.new_generation&&m.name===r.handoff.original_name&&m.sha256===r.handoff.ifc_sha256);
    if(rows.length!==1||s.models.filter(m=>m.name===r.handoff.original_name).length!==1){this.transition(r,'UNKNOWN_RESULT','Final model identity does not match this run.');return;}
    r.result_id=p.result_id;r.state=r.rollback_of?'ROLLED_BACK':'SUCCEEDED';r.reason='Current-run conversion and calculation verified. Regulation status is separate.';r.updated_utc=new Date().toISOString();
    const b=this.store.binding(r.binding_id);b.remote_model_id=r.new_model_id!;b.remote_model_name=r.handoff.original_name;b.baseline_revision=r.new_generation!;b.last_sha256=r.handoff.ifc_sha256;this.store.succeed(r,b);return;
   }
   this.transition(r,p.conversion==='completed'?'CALCULATING':'CONVERTING','Waiting for current-run result. Historical completion is ignored.');
   if(!waitForFinish)return;
   if(Date.now()-start>=this.options.timeoutMs){this.transition(r,'PAUSED','Timed out awaiting current-run conversion/calculation. Keep evidence and reconcile.');return;}
   await wait(this.options.pollMs);
  }while(true);
 }
 async resume(runId:string):Promise<Run>{
  const r=this.store.run(runId);if(done.has(r.state))return r;this.store.acquire(r.project_id,r.id,true);
  try{
   this.input(r);const s=await this.adapter.snapshot();this.project(s,r.expected_project);
   if(!r.intent){this.transition(r,'PLANNED','No action intent exists; safe to re-plan.');this.store.release(r.project_id,r.id);return r;}
   if(r.new_model_id){await this.observe(r,false);}
   else{
    const op=await this.adapter.operation(r.intent.operation_id);
    if(op.id!==r.intent.operation_id||op.kind!==r.intent.kind||op.status!=='completed')this.transition(r,'UNKNOWN_RESULT','No conclusive current-operation acknowledgment; no write replay.');
    else if(op.kind==='delete'&&op.model_id===r.baseline.id&&op.generation===r.baseline.generation&&!s.models.some(m=>m.id===r.baseline.id)){r.intent.acknowledged=true;this.transition(r,'UPLOADING','Deletion reconciled; no repeated delete.');}
    else if(op.kind==='upload'&&op.sha256===r.handoff.ifc_sha256&&op.upload_id){r.upload_id=op.upload_id;r.intent.acknowledged=true;this.transition(r,'SUBMITTING','Upload reconciled; no repeated upload.');}
    else if(op.kind==='submit'&&op.sha256===r.handoff.ifc_sha256&&op.model_id&&op.generation){r.new_model_id=op.model_id;r.new_generation=op.generation;r.intent.acknowledged=true;this.transition(r,'CONVERTING','Submission reconciled; no repeated submit.');await this.observe(r,false);}
    else this.transition(r,'UNKNOWN_RESULT','Operation evidence does not match expected model/hash.');
   }
  }catch(e){this.transition(r,'UNKNOWN_RESULT',redact((e as Error).message));}
  finally{if(done.has(r.state))this.store.release(r.project_id,r.id);else this.store.retain(r.project_id,r.id);}
  return this.store.run(r.id);
 }
 async rollback(runId:string):Promise<Run>{
  if(this.adapter.mode!=='fixture')throw new SyncError('BLOCKED','Real rollback is disabled in this read-only preview.');
  const source=this.store.run(runId);if(!['SUCCEEDED','PAUSED'].includes(source.state)||!source.backup_path||!source.backup_sha256||!source.new_model_id||!source.new_generation)throw new SyncError('BLOCKED','Rollback requires verified backup and known current model generation.');
  const s=await this.adapter.snapshot();this.project(s,source.expected_project);const rows=s.models.filter(m=>m.id===source.new_model_id&&m.generation===source.new_generation&&m.sha256===source.handoff.ifc_sha256);if(rows.length!==1)throw new SyncError('BLOCKED','Rollback target changed; no deletion permitted.');
  if(source.state==='PAUSED')throw new SyncError('BLOCKED','Resolve the paused run before rollback; preview supports verified completed-run rollback only.');
  const r:Run={...source,id:randomUUID(),state:'PLANNED',reason:'Explicit rollback of verified previous bytes.',baseline:rows[0]!,handoff:{...source.handoff,original_name:source.baseline.name,ifc_sha256:source.backup_sha256},rollback_of:source.id,intent:undefined,upload_id:undefined,new_model_id:undefined,new_generation:undefined,backup_path:undefined,backup_sha256:undefined,result_id:undefined,calculation_status:'not_started',regulation_status:'unknown',created_utc:new Date().toISOString(),idempotency_key:hash('rollback|'+source.id+'|'+source.new_generation)};
  this.input(r);this.store.save(r);return this.run(r.id);
 }
}
