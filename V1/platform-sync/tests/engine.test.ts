import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtempSync,writeFileSync,readFileSync,mkdirSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {randomUUID,createHash} from 'node:crypto';
import {spawnSync} from 'node:child_process';
import {Store} from '../src/store.js';
import {SyncEngine} from '../src/engine.js';
import {verifyHandoff,freezeHandoff} from '../src/handoff.js';
import {redact} from '../src/security.js';
import type {PlatformAdapter,Model,Snapshot,Operation,Progress,Handoff} from '../src/types.js';

const sha=(v:string|Buffer)=>createHash('sha256').update(v).digest('hex');
const ifc=(name:string)=>Buffer.from(`ISO-10303-21;\nHEADER;FILE_DESCRIPTION(('${name}'),'2;1');ENDSEC;\nDATA;#1=IFCPROJECT($);ENDSEC;\nEND-ISO-10303-21;`);
function handoff(root:string,name='new.ifc',doc='C:\\Models\\school.rvt') {
  const dir=join(root,randomUUID()); mkdirSync(dir,{recursive:true});
  const bytes=ifc(name); const id=randomUUID();
  const receipt=Buffer.from(JSON.stringify({schema_version:'hifc.native-export-receipt/1.5',export_id:id,ifc_sha256:sha(bytes),
    source_snapshot_before:'abc',source_snapshot_after:'abc',model_transaction:'RolledBack',export_scope:'CURRENT_PROJECT',
    doc_export_calls:1,ifc_postprocessed:false,original_export_server_restored:true,temporary_type_schedules_rolled_back:true}));
  const h:Handoff={schema:'hifc.platform-sync-handoff/1',export_id:id,created_utc:new Date().toISOString(),document_path:doc,project_unique_id:'project-guid',
    original_name:name,ifc_file:'model.ifc',receipt_file:'native.receipt.json',ifc_sha256:sha(bytes),receipt_sha256:sha(receipt)};
  writeFileSync(join(dir,'model.ifc'),bytes);writeFileSync(join(dir,'native.receipt.json'),receipt);writeFileSync(join(dir,'handoff.json'),JSON.stringify(h));
  return join(dir,'handoff.json');
}
class Remote implements PlatformAdapter {
  mode='fixture' as const; old=ifc('old'); uploaded=Buffer.alloc(0); writes:string[]=[];
  snap:Snapshot={authenticated:true,project_id:'p1',project_name:'School',project_code:'C1',scene_project_id:'p1',revision:'r1',models:[{id:'m1',name:'old.ifc',generation:'g1',sha256:sha(this.old)}]};
  operations=new Map<string,Operation>(); fault=''; status:Progress|undefined;
  async snapshot(){return structuredClone(this.snap);}
  async backup(m:Model){return {bytes:this.fault==='bad-backup'?ifc('wrong'):this.old,model_id:m.id,generation:m.generation,sha256:m.sha256!};}
  async deleteModel(m:Model,id:string){this.writes.push('delete');this.snap.models=this.snap.models.filter(x=>x.id!==m.id);this.operations.set(id,{id,kind:'delete',status:'completed',model_id:m.id,generation:m.generation});if(this.fault==='delete-lost')throw Error('connection lost');}
  async upload(file:string,hash:string,id:string){this.writes.push('upload');this.uploaded=readFileSync(file);this.operations.set(id,{id,kind:'upload',status:'completed',upload_id:'u1',sha256:hash});return {upload_id:'u1'};}
  async submit(_upload:string,name:string,id:string){this.writes.push('submit');const n=this.writes.filter(x=>x==='submit').length+1;const m={id:'m'+n,name,generation:'g'+n,sha256:sha(this.uploaded)};this.snap.models.push(m);this.operations.set(id,{id,kind:'submit',status:'completed',model_id:m.id,generation:m.generation,sha256:m.sha256});this.status={operation_id:id,model_id:m.id,generation:m.generation,input_sha256:m.sha256,conversion:'completed',calculation:'completed',regulation:'failed',result_id:'result-current'};if(this.fault==='submit-lost')throw Error('lost submit response');return {model_id:m.id,generation:m.generation};}
  async progress(id:string){return {...this.status!,...(this.fault==='stale-complete'?{operation_id:'old-operation',result_id:'old'}:{}),...(this.fault==='conversion-failed'?{conversion:'failed' as const}:{})};}
  async operation(id:string){return this.operations.get(id)??{id,kind:'delete' as const,status:'absent' as const};}
  async close(){}
}
function setup(){const root=mkdtempSync(join(tmpdir(),'hifc-sync-'));const remote=new Remote();const store=new Store(join(root,'state'));const engine=new SyncEngine(store,remote,{pollMs:1,timeoutMs:30});const manifest=handoff(root);return {root,remote,store,engine,manifest};}
async function bound(c:ReturnType<typeof setup>){return c.engine.bind('m1',c.manifest,{project_id:'p1',project_code:'C1',project_name:'School'});}

test('handoff rejects incomplete export, tampering and traversal; spool is frozen',()=>{
 const c=setup();const frozen=freezeHandoff(c.manifest,join(c.root,'spool'));assert.equal(verifyHandoff(frozen).handoff.original_name,'new.ifc');
 const source=JSON.parse(readFileSync(c.manifest,'utf8'));writeFileSync(join(c.manifest,'..','model.ifc'),ifc('changed'));assert.throws(()=>verifyHandoff(c.manifest),/hash|SHA/i);assert.doesNotThrow(()=>verifyHandoff(frozen));
 source.ifc_file='../outside.ifc';writeFileSync(c.manifest,JSON.stringify(source));assert.throws(()=>verifyHandoff(c.manifest),/path|relative|路径/i);
 const m=handoff(c.root);const h=JSON.parse(readFileSync(m,'utf8'));const rp=join(m,'..','native.receipt.json');const r=JSON.parse(readFileSync(rp,'utf8'));r.model_transaction='Committed';writeFileSync(rp,JSON.stringify(r));h.receipt_sha256=sha(readFileSync(rp));writeFileSync(m,JSON.stringify(h));assert.throws(()=>verifyHandoff(m),/receipt|回执/i);
 c.store.close();
});
test('plan freezes current payload, has no remote writes and binds independent of dated filename',async()=>{const c=setup();const b=await bound(c);const p=await c.engine.plan(b.id,c.manifest);assert.equal(p.state,'PLANNED');assert.deepEqual(c.remote.writes,[]);assert.notEqual(p.manifest_path,c.manifest);const m=handoff(c.root,'OUT_school_20261024_V2.ifc');assert.equal((await c.engine.plan(b.id,m)).state,'PLANNED');c.store.close();});
test('wrong project, wrong scene and copied document are blocked before writes',async()=>{const c=setup();const b=await bound(c);c.remote.snap.project_code='WRONG';await assert.rejects(c.engine.plan(b.id,c.manifest),/project/i);c.remote.snap.project_code='C1';c.remote.snap.scene_project_id='p2';await assert.rejects(c.engine.plan(b.id,c.manifest),/scene/i);c.remote.snap.scene_project_id='p1';await assert.rejects(c.engine.plan(b.id,handoff(c.root,'new.ifc','C:\\Copy\\school.rvt')),/document/i);assert.deepEqual(c.remote.writes,[]);c.store.close();});
test('zero/multiple matches and filename collision block',async()=>{const c=setup();const b=await bound(c);c.remote.snap.models=[];await assert.rejects(c.engine.plan(b.id,c.manifest));c.remote.snap.models=[{id:'m1',name:'old.ifc',generation:'g1'},{id:'m1',name:'old.ifc',generation:'g1'}];await assert.rejects(c.engine.plan(b.id,c.manifest));c.remote.snap.models=[{id:'m1',name:'old.ifc',generation:'g1'},{id:'other',name:'new.ifc',generation:'g3'}];await assert.rejects(c.engine.plan(b.id,c.manifest),/filename/i);c.store.close();});
test('full replacement records intents and advances binding only after current-run calculation',async()=>{const c=setup();const b=await bound(c);const p=await c.engine.plan(b.id,c.manifest);const r=await c.engine.run(p.id);assert.equal(r.state,'SUCCEEDED');assert.equal(r.calculation_status,'completed');assert.equal(r.regulation_status,'failed');assert.deepEqual(c.remote.writes,['delete','upload','submit']);assert.equal(c.store.binding(b.id).remote_model_id,'m2');assert.equal(c.store.events(p.id).filter(x=>x.kind==='intent').length,3);assert.equal((await c.engine.plan(b.id,c.manifest)).state,'NO_CHANGE');c.store.close();});
test('bad or unverified backup blocks delete',async()=>{const c=setup();const b=await bound(c);const p=await c.engine.plan(b.id,c.manifest);c.remote.fault='bad-backup';assert.equal((await c.engine.run(p.id)).state,'BLOCKED');assert.deepEqual(c.remote.writes,[]);c.store.close();});
test('tampered spool blocks writes',async()=>{const c=setup();const b=await bound(c);const p=await c.engine.plan(b.id,c.manifest);writeFileSync(join(p.manifest_path,'..','model.ifc'),ifc('tampered'));assert.equal((await c.engine.run(p.id)).state,'BLOCKED');assert.deepEqual(c.remote.writes,[]);c.store.close();});
test('changed generation after plan blocks replacing another computer update',async()=>{const c=setup();const b=await bound(c);const p=await c.engine.plan(b.id,c.manifest);c.remote.snap.models[0]!.generation='g-other';assert.equal((await c.engine.run(p.id)).state,'BLOCKED');assert.deepEqual(c.remote.writes,[]);c.store.close();});
test('lost delete response retains lock; resume reconciles without replay then continues',async()=>{const c=setup();const b=await bound(c);const p=await c.engine.plan(b.id,c.manifest);c.remote.fault='delete-lost';assert.equal((await c.engine.run(p.id)).state,'UNKNOWN_RESULT');assert.equal((await c.engine.resume(p.id)).state,'UPLOADING');assert.deepEqual(c.remote.writes,['delete']);c.remote.fault='';assert.equal((await c.engine.run(p.id)).state,'SUCCEEDED');assert.equal(c.remote.writes.filter(x=>x==='delete').length,1);c.store.close();});
test('lost submit response resumes by operation and never repeats submit',async()=>{const c=setup();const b=await bound(c);const p=await c.engine.plan(b.id,c.manifest);c.remote.fault='submit-lost';assert.equal((await c.engine.run(p.id)).state,'UNKNOWN_RESULT');c.remote.fault='';const r=await c.engine.resume(p.id);assert.equal(r.state,'SUCCEEDED');assert.equal(c.remote.writes.filter(x=>x==='submit').length,1);c.store.close();});
test('stale completion and conversion failure do not claim success or update baseline',async()=>{for(const fault of ['stale-complete','conversion-failed']){const c=setup();const b=await bound(c);const p=await c.engine.plan(b.id,c.manifest);c.remote.fault=fault;assert.notEqual((await c.engine.run(p.id)).state,'SUCCEEDED');assert.equal(c.store.binding(b.id).remote_model_id,'m1');c.store.close();}});
test('real adapter apply is denied before any write',async()=>{const c=setup();const b=await bound(c);const p=await c.engine.plan(b.id,c.manifest);Object.defineProperty(c.remote,'mode',{value:'real-readonly'});await assert.rejects(c.engine.run(p.id),/read.only|preview/i);assert.deepEqual(c.remote.writes,[]);c.store.close();});
test('SQLite project lock blocks another store and preserves unknown owner',()=>{const c=setup();const s2=new Store(c.store.directory);c.store.acquire('p1','r1');assert.throws(()=>s2.acquire('p1','r2'),/lock|busy/i);c.store.retain('p1','r1');assert.throws(()=>s2.acquire('p1','r2'),/lock|busy/i);s2.acquire('p1','r1',true);s2.release('p1','r1');c.store.close();s2.close();});
test('rollback is explicit and restores verified previous bytes',async()=>{const c=setup();const b=await bound(c);const p=await c.engine.plan(b.id,c.manifest);await c.engine.run(p.id);c.remote.old=Buffer.from(c.remote.uploaded);const r=await c.engine.rollback(p.id);assert.equal(r.state,'ROLLED_BACK');assert.equal(sha(c.remote.uploaded),sha(ifc('old')));assert.equal(c.store.binding(b.id).last_sha256,sha(ifc('old')));c.store.close();});
test('logs redact URL credentials and authorization secrets',()=>{const text=redact('Error https://hbbim.hbcic.net.cn:9000/planning-review?token=SECRET123#/index Authorization: Bearer abc Cookie: sid=xyz');assert.ok(!text.includes('SECRET123'));assert.ok(!text.includes('abc'));assert.ok(!text.includes('sid=xyz'));});
test('process restart must reconcile in-flight upload/submit before run can continue',async()=>{for(const kind of ['upload','submit'] as const){const c=setup();const b=await bound(c);const p=await c.engine.plan(b.id,c.manifest);c.remote.fault='delete-lost';await c.engine.run(p.id);const crashed=c.store.run(p.id);crashed.state=kind==='upload'?'UPLOADING':'SUBMITTING';crashed.intent={kind,operation_id:crashed.id+':'+kind};c.store.save(crashed);const before=c.remote.writes.length;await assert.rejects(c.engine.run(p.id),/reconcil/i);assert.equal(c.remote.writes.length,before);c.store.close();}});
test('unexpected same-generation byte change cannot be reported as no-change',async()=>{const c=setup();const b=await bound(c);const p=await c.engine.plan(b.id,c.manifest);await c.engine.run(p.id);c.remote.snap.models[0]!.sha256=sha(ifc('external-change'));await assert.rejects(c.engine.plan(b.id,c.manifest),/hash|baseline/i);c.store.close();});
test('different processes cannot steal a live or crashed project lock',()=>{
 const c=setup(),module=new URL('../src/store.js',import.meta.url).href;
 const code=`import {Store} from ${JSON.stringify(module)}; const s=new Store(process.argv[1]);try{s.acquire('p1',process.argv[2]);s.close();}catch(e){process.exitCode=e.code==='BUSY'?7:1;s.close();}`;
 c.store.acquire('p1','live');const blocked=spawnSync(process.execPath,['--input-type=module','-e',code,c.store.directory,'other'],{windowsHide:true,encoding:'utf8'});assert.equal(blocked.status,7,blocked.stderr);c.store.release('p1','live');
 const crashed=spawnSync(process.execPath,['--input-type=module','-e',code,c.store.directory,'crashed'],{windowsHide:true,encoding:'utf8'});assert.equal(crashed.status,0,crashed.stderr);assert.throws(()=>c.store.acquire('p1','other'),/busy/i);c.store.acquire('p1','crashed',true);c.store.release('p1','crashed');c.store.close();
});
test('backup tampering after delete or upload stops the next write and keeps project locked',async()=>{
 for(const stage of ['delete','upload']){
  const c=setup(),b=await bound(c),p=await c.engine.plan(b.id,c.manifest);
  const corrupt=()=>writeFileSync(c.store.run(p.id).backup_path!,ifc('corrupted backup'));
  if(stage==='delete'){const original=c.remote.deleteModel.bind(c.remote);c.remote.deleteModel=async(m,id)=>{await original(m,id);corrupt();};}
  else{const original=c.remote.upload.bind(c.remote);c.remote.upload=async(file,digest,id)=>{const value=await original(file,digest,id);corrupt();return value;};}
  const result=await c.engine.run(p.id);assert.equal(result.state,'UNKNOWN_RESULT');assert.deepEqual(c.remote.writes,stage==='delete'?['delete']:['delete','upload']);assert.equal(c.store.binding(b.id).remote_model_id,'m1');assert.throws(()=>c.store.acquire('p1','other'),/busy/i);c.store.close();
 }
});
test('transient pre-delete read failure reuses only the verified persisted backup',async()=>{
 const c=setup(),b=await bound(c),p=await c.engine.plan(b.id,c.manifest);const original=c.remote.snapshot.bind(c.remote);let count=0;
 c.remote.snapshot=async()=>{if(++count===2)throw Error('transient read failure');return original();};
 assert.equal((await c.engine.run(p.id)).state,'BLOCKED');assert.deepEqual(c.remote.writes,[]);assert.equal((await c.engine.resume(p.id)).state,'PLANNED');assert.equal((await c.engine.run(p.id)).state,'SUCCEEDED');assert.deepEqual(c.remote.writes,['delete','upload','submit']);c.store.close();
});
test('crash remnant backup is recovered by re-downloading and verifying correspondence',async()=>{
 for(const valid of [true,false]){const c=setup(),b=await bound(c),p=await c.engine.plan(b.id,c.manifest);const dir=join(c.store.directory,'backups',p.id);mkdirSync(dir,{recursive:true});writeFileSync(join(dir,'previous.ifc'),valid?c.remote.old:ifc('tampered remnant'));const r=await c.engine.run(p.id);assert.equal(r.state,valid?'SUCCEEDED':'BLOCKED');if(!valid)assert.deepEqual(c.remote.writes,[]);c.store.close();}
});
