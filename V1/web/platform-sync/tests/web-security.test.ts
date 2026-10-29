import {test} from 'node:test';
import assert from 'node:assert/strict';
import {generateKeyPairSync,sign,randomUUID} from 'node:crypto';
import {mkdtempSync,mkdirSync,writeFileSync,rmSync,readFileSync,linkSync,symlinkSync,lstatSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join,resolve} from 'node:path';
import {spawnSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import {accountRoot,configureRuntime,validateConfig,runtimeIdentity,sha,permitPayload,consumptionPayload,validatePermit,prepareParentLaunch,authorizeWorker,requireWriteAuthorization,receiptIdentity,type WebConfig,type Permit,type Consumption} from '../src/web-runtime.js';
import type {Run} from '../src/types.js';
import {createAdapter} from '../src/browser.js';
import {RealBrowserAdapter} from '../src/real-browser.js';
import {RealEvidence} from '../src/real-evidence.js';
import {Store} from '../src/store.js';
import {DatabaseSync} from 'node:sqlite';
import {API_PATH,REAL_ORIGIN} from '../src/real-contract.js';
import type {Request,Route} from 'playwright';

const root=mkdtempSync(join(tmpdir(),'hifc-web-sync-')),previousLocal=process.env.LOCALAPPDATA;process.env.LOCALAPPDATA=root;
const account=randomUUID(),base=accountRoot(account),configFile=join(base,'config.json');mkdirSync(base,{recursive:true});
const config:WebConfig={schema:'hifc.platform-sync-config/1',adapter:'real',state_dir:join(base,'state'),browser_profile:join(base,'browser'),entry_secret_file:join(base,'entry.dpapi'),headless:false,web:{schemaVersion:1,minimumReaderVersion:1,accountId:account,componentVersion:'0.2.0-web.1',nodeVersion:'25.4.0',mainReleaseId:'0.5.0-dev-20261029T000000Z',mainSourceCommit:'a'.repeat(40)}};
writeFileSync(configFile,JSON.stringify(config));configureRuntime(config,configFile);
const pair=generateKeyPairSync('rsa',{modulusLength:2048}),jwk=pair.publicKey.export({format:'jwk'}),key={modulus:Buffer.from(jwk.n!,'base64url').toString('base64'),exponent:Buffer.from(jwk.e!,'base64url').toString('base64')};
const keyPath=join(root,'HIFC','0.5.0-Web','Revit2020','update-public-key.json');mkdirSync(resolve(keyPath,'..'),{recursive:true});writeFileSync(keyPath,JSON.stringify(key));
function run():Run{return {id:randomUUID(),binding_id:'binding',project_id:'project',state:'PLANNED',reason:'preview',created_utc:new Date().toISOString(),updated_utc:new Date().toISOString(),web:{...runtimeIdentity()},handoff:{schema:'hifc.platform-sync-handoff/1',export_id:randomUUID(),created_utc:new Date().toISOString(),document_path:'C:\\model.rvt',project_unique_id:'project-unique',original_name:'model.ifc',ifc_file:'model.ifc',receipt_file:'receipt.json',ifc_sha256:'b'.repeat(64),receipt_sha256:'c'.repeat(64),web:{schema:'hifc.web-platform-export/1',document_session_id:'doc-1',receipt_identity_hash:'d'.repeat(64)}},manifest_path:'unused',baseline:{id:'target',name:'model.ifc',generation:'generation'},expected_project:{project_id:'project',project_code:'code',project_name:'name'},idempotency_key:'key',calculation_status:'not_started',regulation_status:'unknown'};}
function permit(r:Run):Permit{const now=Math.floor(Date.now()/1000),p:Permit={exportPlanId:r.handoff.export_id,documentSessionId:'doc-1',projectId:r.project_id,targetModelId:r.baseline.id,targetGeneration:r.baseline.generation,ifcSha256:r.handoff.ifc_sha256,runId:r.id,mainReleaseId:config.web.mainReleaseId,mainSourceCommit:config.web.mainSourceCommit,componentVersion:'0.2.0-web.1',nodeVersion:'25.4.0',confirmedReplacement:true,id:randomUUID(),accountId:account,exportPlanHash:'e'.repeat(64),receiptIdentityHash:'d'.repeat(64),issuedAtUnixSeconds:now,expiresAtUnixSeconds:now+60,signature:''};p.signature=sign('RSA-SHA256',Buffer.from(permitPayload(p)),pair.privateKey).toString('base64');return p;}
test('Web config rejects fixture, Local paths and unknown schema',async()=>{assert.throws(()=>validateConfig({...config,adapter:'fixture'},configFile));assert.throws(()=>validateConfig({...config,state_dir:join(root,'Local','state')},configFile));assert.throws(()=>validateConfig({...config,web:{...config.web,schemaVersion:2 as 1}},configFile));await assert.rejects(()=>createAdapter({...config,adapter:'fixture'}));});
test('schema markers and tasks require an explicit compatible minimum reader',()=>{const marker=join(config.state_dir,'web-schema.json');try{writeFileSync(marker,JSON.stringify({schemaVersion:1,accountId:account}));assert.throws(()=>configureRuntime(config,configFile));}finally{writeFileSync(marker,JSON.stringify({schemaVersion:1,minimumReaderVersion:1,accountId:account}));configureRuntime(config,configFile);}const r=run(),p=permit(r);assert.throws(()=>validatePermit(p,{...r,web:{...r.web!,minimumReaderVersion:undefined as unknown as 1}},key));});
test('bare apply has no engine authorization even with a valid signed permit',()=>{const r=run(),p=permit(r);validatePermit(p,r,key);assert.throws(()=>requireWriteAuthorization(r),/Bare apply/);const result=spawnSync(process.execPath,[fileURLToPath(new URL('../src/cli.js',import.meta.url)),'run','--config',configFile,'--run',r.id,'--apply'],{encoding:'utf8',env:{...process.env,LOCALAPPDATA:root}});assert.notEqual(result.status,0);assert.equal(result.stdout,'');});
test('every target and frozen identity is covered by signature and lock',()=>{const r=run(),p=permit(r);for(const field of ['runId','projectId','targetModelId','targetGeneration','ifcSha256','mainReleaseId','mainSourceCommit','componentVersion','nodeVersion','documentSessionId','receiptIdentityHash','accountId'] as const){const changed={...p,[field]:'changed'};assert.throws(()=>validatePermit(changed,r,key),field);}assert.throws(()=>validatePermit({...p,expiresAtUnixSeconds:Math.floor(Date.now()/1000)-1},r,key));});
test('native receipt identity preserves zero ambiguous export state',()=>{const r={planId:'plan',documentSessionId:'doc',ifcHash:'a'.repeat(64),snapshotBefore:'same',snapshotAfter:'same',sourceRestored:true,exported:true};assert.notEqual(receiptIdentity(r),receiptIdentity({...r,sourceRestored:false}));assert.notEqual(receiptIdentity(r),receiptIdentity({...r,snapshotAfter:'changed'}));});
test('direct real adapter methods and request admission cannot bypass the consumed launch grant',async()=>{
 const adapter=new RealBrowserAdapter(config,REAL_ORIGIN+'/planning-review');const r=run();adapter.prepare(r);let browserCalls=0,admitted=0,aborted=0;
 Reflect.set(adapter,'editor',async()=>{browserCalls++;throw Error('Test forbids browser/network access.');});
 try{
  const blocked=(e:unknown)=>e instanceof Error&&'code' in e&&e.code==='BLOCKED';
  await assert.rejects(()=>adapter.deleteModel(r.baseline,'direct-delete'),blocked);
  await assert.rejects(()=>adapter.upload('unused.ifc',r.handoff.ifc_sha256,'direct-upload'),blocked);
  await assert.rejects(()=>adapter.submit('upload',r.handoff.original_name,'direct-submit'),blocked);
  const evidence=Reflect.get(adapter,'evidence') as RealEvidence;evidence.begin('route-delete','delete',{project_id:r.project_id,model_id:r.baseline.id,generation:r.baseline.generation});Reflect.set(adapter,'active',{kind:'delete',id:'route-delete'});
  const request={url:()=>REAL_ORIGIN+API_PATH+'/Project/DelIfcModel',method:()=> 'POST',headers:()=>({'content-type':'application/json'}),postData:()=>JSON.stringify({ID:r.baseline.id,PROJECTID:r.project_id,MODELTYPE:2})} as unknown as Request;
  const route={request:()=>request,fallback:async()=>{admitted++;},abort:async()=>{aborted++;}} as unknown as Route;
  await (Reflect.get(adapter,'route') as (r:Route)=>Promise<void>).call(adapter,route);
  assert.equal(browserCalls,0);assert.equal(admitted,0);assert.equal(aborted,1);assert.notEqual(evidence.get('route-delete')?.request_seen,true);
 }finally{await adapter.close();}
});
test('real adapter constructor and factory refuse non-Web runtime state',async()=>{
 const unsafe={...config,state_dir:join(root,'simulated-Local')};const blocked=(e:unknown)=>e instanceof Error&&'code' in e&&e.code==='BLOCKED';
 assert.throws(()=>{const adapter=new RealBrowserAdapter(unsafe,REAL_ORIGIN+'/planning-review');void adapter.close();},blocked);
 await assert.rejects(async()=>{const adapter=await createAdapter(unsafe,REAL_ORIGIN+'/planning-review');await adapter.close();},blocked);
});
test('SQLite database and every sidecar reject symbolic and hard links without changing their targets',()=>{
 const local=join(root,'simulated-Local-databases');mkdirSync(local,{recursive:true});let cases=0;
 for(const database of ['sync.sqlite','real-evidence.sqlite'])for(const suffix of ['','-wal','-shm','-journal'])for(const kind of ['symbolic','hard']){
  const target=join(local,database+suffix+'-'+kind);const initial=new DatabaseSync(target);initial.exec('CREATE TABLE LocalSentinel(value TEXT); INSERT INTO LocalSentinel VALUES(\'preserve\')');initial.close();const before=sha(readFileSync(target));
  const leaf=join(config.state_dir,database+suffix);for(const ext of ['','-wal','-shm','-journal'])rmSync(join(config.state_dir,database+ext),{force:true});
  if(kind==='symbolic')symlinkSync(target,leaf,'file');else linkSync(target,leaf);
  assert.equal(kind==='symbolic'?lstatSync(leaf).isSymbolicLink():lstatSync(leaf).nlink>1,true);
  try{assert.throws(()=>{const db=database==='sync.sqlite'?new Store(config.state_dir):new RealEvidence(config.state_dir);db.close();},(e:unknown)=>e instanceof Error&&'code' in e&&e.code==='BLOCKED');assert.equal(sha(readFileSync(target)),before,'Local target changed: '+database+suffix+' '+kind);cases++;}
  finally{for(const ext of ['','-wal','-shm','-journal'])rmSync(join(config.state_dir,database+ext),{force:true});}
 }
 assert.equal(cases,16);
 const store=new Store(config.state_dir);assert.deepEqual(store.runs(),[]);const statusReader=new Store(config.state_dir),planned=run();store.save(planned);assert.equal(statusReader.run(planned.id).id,planned.id);statusReader.close();store.close();
 const evidence=new RealEvidence(config.state_dir),evidenceReader=new RealEvidence(config.state_dir);evidence.begin('normal','delete',{project_id:'project',model_id:'model',generation:'generation'});assert.equal(evidenceReader.get('normal')?.kind,'delete');evidenceReader.close();evidence.close();
});
test('worker requires a single-use signed server consumption and keeps its grant during account expiry',async()=>{const r=run(),p=permit(r),file=join(base,p.id+'.json');writeFileSync(file,JSON.stringify(p));const parent=prepareParentLaunch(file,r,config.state_dir);let consumed=false;const originalFetch=globalThis.fetch;
 globalThis.fetch=async(_url,init)=>{const body=JSON.parse(String(init?.body)) as {permit:Permit;consumerNonce:string};if(consumed)return new Response('{}',{status:409});consumed=true;const receipt:Consumption={permitId:p.id,permitHash:sha(permitPayload(p)),accountId:account,runId:r.id,consumerNonce:body.consumerNonce,consumedAtUnixSeconds:Math.floor(Date.now()/1000),signature:''};receipt.signature=sign('RSA-SHA256',Buffer.from(consumptionPayload(receipt)),pair.privateKey).toString('base64');return new Response(JSON.stringify(receipt),{status:200});};
 try{await authorizeWorker(file,parent.proof,r,config.state_dir);requireWriteAuthorization(r);const adapter=new RealBrowserAdapter(config,REAL_ORIGIN+'/planning-review');adapter.prepare(r);const oldNow=Date.now;Date.now=()=>oldNow()+3600000;
  try{requireWriteAuthorization(r);assert.doesNotThrow(()=>Reflect.get(adapter,'writable').call(adapter));let admitted=0;const evidence=Reflect.get(adapter,'evidence') as RealEvidence;evidence.begin('authorized-route','delete',{project_id:r.project_id,model_id:r.baseline.id,generation:r.baseline.generation});Reflect.set(adapter,'active',{kind:'delete',id:'authorized-route'});
   const request={url:()=>REAL_ORIGIN+API_PATH+'/Project/DelIfcModel',method:()=> 'POST',headers:()=>({'content-type':'application/json'}),postData:()=>JSON.stringify({ID:r.baseline.id,PROJECTID:r.project_id,MODELTYPE:2})} as unknown as Request;
   const route={request:()=>request,fallback:async()=>{admitted++;},abort:async()=>{throw Error('Authorized fixture route unexpectedly blocked.');}} as unknown as Route;await (Reflect.get(adapter,'route') as (route:Route)=>Promise<void>).call(adapter,route);assert.equal(admitted,1);
  }finally{Date.now=oldNow;await adapter.close();}
  const readonly=new RealBrowserAdapter({...config,adapter:'real-readonly'},REAL_ORIGIN+'/planning-review');try{assert.doesNotThrow(()=>readonly.prepare({...run(),web:{...runtimeIdentity(),componentVersion:'0.1.0-web.1',mainReleaseId:'0.5.0-dev-20261020T000000Z'}}));}finally{await readonly.close();}
  assert.throws(()=>requireWriteAuthorization({...r,id:randomUUID()}));assert.throws(()=>requireWriteAuthorization({...r,baseline:{...r.baseline,generation:'changed'}}));assert.throws(()=>requireWriteAuthorization({...r,expected_project:{...r.expected_project,project_id:'other-project'}}));rmSync(join(config.state_dir,'authorizations'),{recursive:true});const replay=prepareParentLaunch(file,r,config.state_dir);await assert.rejects(()=>authorizeWorker(file,replay.proof,r,config.state_dir),/refused/);
 }finally{globalThis.fetch=originalFetch;}
});
process.on('exit',()=>{process.env.LOCALAPPDATA=previousLocal;rmSync(root,{recursive:true,force:true});});
