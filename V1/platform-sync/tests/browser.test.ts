import test from 'node:test';
import assert from 'node:assert/strict';
import {createHash,randomUUID} from 'node:crypto';
import {mkdtempSync,mkdirSync,writeFileSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {Store} from '../src/store.js';
import {SyncEngine} from '../src/engine.js';
import {createAdapter,FixtureBrowserAdapter,RealReadonlyBrowserAdapter} from '../src/browser.js';
import {startFixture,type FixtureFaults} from '../src/fixture.js';
import type {Config,Handoff} from '../src/types.js';

const hash=(v:Buffer)=>createHash('sha256').update(v).digest('hex');
const ifc=(name:string)=>Buffer.from(`ISO-10303-21;\nHEADER;FILE_DESCRIPTION(('${name}'),'2;1');ENDSEC;\nDATA;#1=IFCPROJECT($);ENDSEC;\nEND-ISO-10303-21;`);
function manifest(root:string){
 const dir=join(root,'input');mkdirSync(dir);const bytes=ifc('current'),id=randomUUID();
 const receipt=Buffer.from(JSON.stringify({schema_version:'hifc.native-export-receipt/1.5',export_id:id,ifc_sha256:hash(bytes),source_snapshot_before:'same',source_snapshot_after:'same',model_transaction:'RolledBack',export_scope:'CURRENT_PROJECT',doc_export_calls:1,ifc_postprocessed:false,original_export_server_restored:true,temporary_type_schedules_rolled_back:true}));
 const h:Handoff={schema:'hifc.platform-sync-handoff/1',export_id:id,created_utc:new Date().toISOString(),document_path:'C:\\Models\\school.rvt',project_unique_id:'school-guid',original_name:'OUT_School_20261023_V2.ifc',ifc_file:'model.ifc',receipt_file:'native.receipt.json',ifc_sha256:hash(bytes),receipt_sha256:hash(receipt)};
 writeFileSync(join(dir,h.ifc_file),bytes);writeFileSync(join(dir,h.receipt_file),receipt);writeFileSync(join(dir,'handoff.json'),JSON.stringify(h));return join(dir,'handoff.json');
}
async function setup(faults:FixtureFaults={}){
 const root=mkdtempSync(join(tmpdir(),'hifc-browser-')),fixture=await startFixture({faults});
 const config:Config={schema:'hifc.platform-sync-config/1',adapter:'fixture',state_dir:join(root,'state'),browser_profile:join(root,'browser'),fixture_url:fixture.url,headless:true};
 const adapter=await createAdapter(config),store=new Store(config.state_dir),engine=new SyncEngine(store,adapter,{pollMs:10,timeoutMs:1000});
 const h=manifest(root),s=await adapter.snapshot(),binding=await engine.bind(s.models[0]!.id,h,s),run=await engine.plan(binding.id,h);
 return {root,fixture,config,adapter,store,engine,binding,run,close:async()=>{await adapter.close();store.close();await fixture.close();}};
}

test('browser plan has no writes; scoped UI replacement verifies current calculation separately from regulation',async()=>{
 const c=await setup();try{assert.equal(c.run.state,'PLANNED');assert.deepEqual(c.fixture.counters(),{delete:0,upload:0,submit:0});
 const r=await c.engine.run(c.run.id);assert.equal(r.state,'SUCCEEDED');assert.equal(r.calculation_status,'completed');assert.equal(r.regulation_status,'failed');assert.deepEqual(c.fixture.counters(),{delete:1,upload:1,submit:1});assert.notEqual(c.store.binding(c.binding.id).remote_model_id,c.run.baseline.id);
 }finally{await c.close();}
});
test('lost delete response survives browser restart and reconciles without repeating deletion',async()=>{
 const c=await setup({loseDeleteResponse:true});try{
 assert.equal((await c.engine.run(c.run.id)).state,'UNKNOWN_RESULT');await c.adapter.close();
 const adapter=await createAdapter(c.config);try{const engine=new SyncEngine(c.store,adapter,{pollMs:10,timeoutMs:1000});assert.equal((await engine.resume(c.run.id)).state,'UPLOADING');c.fixture.control({loseDeleteResponse:false});assert.equal((await engine.run(c.run.id)).state,'SUCCEEDED');assert.deepEqual(c.fixture.counters(),{delete:1,upload:1,submit:1});}finally{await adapter.close();}
 }finally{await c.close();}
});
test('lost submit response is reconciled by operation identity and does not add a second model',async()=>{
 const c=await setup({loseSubmitResponse:true});try{assert.equal((await c.engine.run(c.run.id)).state,'UNKNOWN_RESULT');
 await new Promise<void>(resolve=>setTimeout(resolve,200));const r=await c.engine.resume(c.run.id);assert.equal(r.state,'SUCCEEDED');assert.deepEqual(c.fixture.counters(),{delete:1,upload:1,submit:1});assert.equal(c.fixture.snapshot().models.length,1);
 }finally{await c.close();}
});
test('same browser recovers interrupted delete or immediate upload without repeating either action',async()=>{
 for(const faults of [{loseDeleteResponse:true},{loseUploadResponse:true}]){const c=await setup(faults);try{
 assert.equal((await c.engine.run(c.run.id)).state,'UNKNOWN_RESULT');const reconciled=await c.engine.resume(c.run.id);assert.ok(['UPLOADING','SUBMITTING'].includes(reconciled.state));c.fixture.control({loseDeleteResponse:false,loseUploadResponse:false});const resumed=await c.engine.run(c.run.id);assert.equal(resumed.state,'SUCCEEDED',JSON.stringify({faults,reason:resumed.reason,counters:c.fixture.counters()}));assert.deepEqual(c.fixture.counters(),{delete:1,upload:1,submit:1});
 }finally{await c.close();}}
});
test('truncated-looking names are matched by exact full title and generation',async()=>{
 const root=mkdtempSync(join(tmpdir(),'hifc-names-')),long='A very long shared project and building model name ';
 const fixture=await startFixture({models:[{id:'right',name:long+'one.ifc',generation:'g1',bytes:ifc('one')},{id:'other',name:long+'two.ifc',generation:'g2',bytes:ifc('two')}]});
 const adapter=await createAdapter({schema:'hifc.platform-sync-config/1',adapter:'fixture',state_dir:join(root,'state'),browser_profile:join(root,'browser'),fixture_url:fixture.url,headless:true});try{
 const s=await adapter.snapshot();const evidence=await(adapter as FixtureBrowserAdapter).inspectEvidence();assert.equal(evidence.connected,true);assert.equal(evidence.identity_complete,false);assert.ok(evidence.project_name_candidates.includes('School'));assert.ok(evidence.model_full_names.includes(long+'two.ifc'));assert.ok(evidence.missing_proofs.includes('active_scene_project_id'));assert.deepEqual(fixture.counters(),{delete:0,upload:0,submit:0});
 await adapter.deleteModel(s.models.find(m=>m.id==='other')!,'exact:delete');assert.deepEqual(fixture.snapshot().models.map(m=>m.id),['right']);assert.deepEqual(fixture.counters(),{delete:1,upload:0,submit:0});
 }finally{await adapter.close();await fixture.close();}
});
test('stale completed result cannot advance binding; failed conversion pauses with evidence',async()=>{
 for(const faults of [{staleComplete:true},{conversionFails:true}]){const c=await setup(faults);try{const r=await c.engine.run(c.run.id);assert.ok(['UNKNOWN_RESULT','PAUSED'].includes(r.state));assert.equal(c.store.binding(c.binding.id).remote_model_id,c.run.baseline.id);}finally{await c.close();}}
});
test('missing or corrupt old backup blocks every UI write',async()=>{
 for(const faults of [{corruptBackup:true},{unverifiedBackup:true}]){const c=await setup(faults);try{assert.equal((await c.engine.run(c.run.id)).state,'BLOCKED');assert.deepEqual(c.fixture.counters(),{delete:0,upload:0,submit:0});}finally{await c.close();}}
});
test('fixture browser rejects non-loopback and real browser writes fail before launching',async()=>{
 const root=mkdtempSync(join(tmpdir(),'hifc-denied-'));const config:Config={schema:'hifc.platform-sync-config/1',adapter:'fixture',state_dir:join(root,'state'),browser_profile:join(root,'profile'),fixture_url:'https://example.com'};
 await assert.rejects(createAdapter(config),/loopback/i);
 const real=new RealReadonlyBrowserAdapter({...config,adapter:'real-readonly'},'https://hbbim.hbcic.net.cn:9000/planning-review');
 const model={id:'m',name:'a.ifc',generation:'g'};
 await assert.rejects(real.deleteModel(model,'op'),/read.only/i);await assert.rejects(real.upload('unused','hash','op'),/read.only/i);await assert.rejects(real.submit('u','a.ifc','op'),/read.only/i);await real.close();
 await assert.rejects(createAdapter({...config,adapter:'real-readonly'},'http://hbbim.hbcic.net.cn:9000/planning-review'),/allowed|https|origin/i);
});
