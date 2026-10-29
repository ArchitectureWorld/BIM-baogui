import test from 'node:test';import assert from 'node:assert/strict';import {chromium} from 'playwright';
import {mkdtempSync,writeFileSync} from 'node:fs';import {tmpdir} from 'node:os';import {join} from 'node:path';
import {RealBrowserAdapter} from '../src/real-browser.js';import {API_PATH,REAL_ORIGIN} from '../src/real-contract.js';import {hash} from '../src/security.js';import type {Run} from '../src/types.js';
import {RealEvidence} from '../src/real-evidence.js';import type {Route} from 'playwright';
const bytes=Buffer.from("ISO-10303-21;\nHEADER;FILE_SCHEMA(('IFC2X3'));ENDSEC;\nDATA;#1=IFCPROJECT('0',$,'School',$,$,$,$,$,$);ENDSEC;\nEND-ISO-10303-21;\n");
async function setup(staleRunning=false,truncateNames=false,nestedDialogs=false){
 const root=mkdtempSync(join(tmpdir(),'real-write-')),file=join(root,'frozen.ifc');writeFileSync(file,bytes);
 const browser=await chromium.launch({headless:true,...(process.env.HIFC_SYNC_CHROMIUM?{executablePath:process.env.HIFC_SYNC_CHROMIUM}:process.platform==='win32'?{channel:'msedge'}:{})});const calls:string[]=[];
 let releaseStale=()=>{},readFails=false;const staleGate=new Promise<void>(resolve=>{releaseStale=resolve;});
 let list=[{ID:'old-123456',NAME:'old.ifc',PROJECTID:'p1',MODELPATH:'Upload/Leave/20261026/old.ifc',ADDTIME:'2026-10-26T00:00:00',MODELTYPE:2,THREEVER:100,LIGHTSTATE:1}];let calc=0;
 const fileRecord={ID:'upload-1',NAME:'gate.ifc',PATH:'Upload/Leave/20261026/gate.ifc'};
 const serve=async(route:Route)=>{const r=route.request(),u=new URL(r.url());const path=u.pathname.slice(API_PATH.length);calls.push(r.method()+' '+path);let value:unknown;
  if(path==='/HomePageShow/GetCurProject')value='p1';else if(path==='/ProjectManagement/QueryProjectSign')value={ID:'p1',NAME:'School',PLATFORMPROCODE:'C1',APPROVALSTATE:0};else if(path==='/Project/GetIfcInfos')value=list;else if(path==='/ProjectManagement/QueryProject')value=[{ProjectID:'p1',IFCCalcState:-1}];
  else if(path==='/Project/DelIfcModel'){list=[];value=true;}else if(path==='/FileManage/uploadFileChunk')value={RESULT:true,NEEDMERGE:false,FILEGROUP:fileRecord};
  else if(path==='/Project/AddIfcModel'){list=[{ID:'new-abcdef',NAME:'gate.ifc',PROJECTID:'p1',MODELPATH:fileRecord.PATH,ADDTIME:'2026-10-26T01:00:00',MODELTYPE:2,THREEVER:100,LIGHTSTATE:1}];value={ID:'new-abcdef'};}
  else if(path==='/Project/GetIFCPositionToWorld')value={ifcInfoState:'1',PyIfcData:[{threeEntId:'new-abcdef',positionData:'[]'}]};
  else if(path==='/ProjectManagement/BindProject_IFCThree'){calc=staleRunning?1:2;value='1';}
  else if(path==='/StatisticsReport/GetHandleInfo_ZHReport')value={recordType:'3',filePath:'Upload/Leave/20261026/p1/综合报告/School.pdf'};
  else if(path==='/Verification/GetCadRecordInfo'){if(readFails)return route.fulfill({status:503,body:'temporary read outage'});if(u.searchParams.get('phase')==='stale'){await staleGate;value={CHECKSTATE:2};}else value={CHECKSTATE:calc};}else if(path.startsWith('/Upload/'))return route.fulfill({body:bytes,contentType:'application/octet-stream'});
  if(value!==undefined)return route.fulfill({body:JSON.stringify(value),contentType:'application/json'});
  return route.fulfill({contentType:'text/html; charset=utf-8',body:`<button onclick="report()">综合报告</button>${nestedDialogs?'<div class="dialogBox"><div class="headhead">项目管理<i class="el-icon-close" onclick="this.closest(\'.dialogBox\').style.display=\'none\'">关闭</i></div><div class="dialogBox"><div class="headhead">编辑项目<i class="el-icon-close" onclick="this.closest(\'.dialogBox\').style.display=\'none\'">关闭</i></div>':''}<div id="addProject"><input placeholder="请输入项目名称" value="School"><input placeholder="请输入联审平台立项项目代码" value="C1"><div role="tab" aria-selected="true">BIM模型</div><div id="rows"></div><button onclick="document.querySelector('#uploadDialog').style.display='block'">选择文件</button></div>
  <div class="el-message-box" style="display:none"><div>确定要删除该模型吗?</div><button onclick="del()">确定</button></div>
  <div id="uploadDialog" class="dialogBox" style="display:none"><div class="headhead">添加IFC模型</div><button onclick="document.querySelector('input[type=file]').click()">选择文件</button><input type="file" style="display:none" onchange="upload(this.files[0])"><div class="dialogBoxFooter"><button onclick="submit()">确定</button></div></div>${nestedDialogs?'</div></div>':''}
  <script>const base='${API_PATH}';let uploaded;function showDelete(){document.querySelector('.el-message-box').style.display='block'};async function api(path,params={},options={}){return (await fetch(base+path+'?'+new URLSearchParams(params),options)).json()};async function rows(){const m=await api('/Project/GetIfcInfos',{proId:'p1',modelType:'2'});document.querySelector('#rows').innerHTML=m.map(x=>'<label class="fileInfoLabel"><span class="fileNameSpan" title="'+x.NAME+'">'+(${truncateNames} ? x.NAME.slice(0,3)+'...' : x.NAME)+'</span><img title="删除" style="width:16px;height:16px" onclick="showDelete()"></label>').join('')}
  async function del(){await api('/Project/DelIfcModel',{ID:'old-123456',PROJECTID:'p1',MODELTYPE:2},{method:'POST'});document.querySelector('.el-message-box').style.display='none';await rows()}
  async function upload(f){const b=new FormData();b.append('filename',f.name);b.append('file',f);uploaded=(await api('/FileManage/uploadFileChunk',{}, {method:'POST',body:b})).FILEGROUP}
  async function report(){const b=new FormData();for(const [k,v] of Object.entries({PROJECTID:'p1',RecordType:'3',GetRecordByType:'0',RECORDOLDID:'',ProOldId:''}))b.append(k,v);await api('/StatisticsReport/GetHandleInfo_ZHReport',{}, {method:'POST',body:b})}
  async function submit(){await api('/Project/AddIfcModel',{ID:'',MODELTYPE:2,PROJECTID:'p1',MODELPATH:uploaded.PATH,NAME:uploaded.NAME},{method:'POST'});await rows();document.querySelector('#uploadDialog').style.display='none';await api('/Project/GetIFCPositionToWorld',{proId:'p1'},{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify([{threeEntId:'new-abcdef',ifcIdAry:[1]}])});if(${staleRunning})void api('/Verification/GetCadRecordInfo',{ProjectId:'p1',subType:'5',phase:'stale'});const b=new FormData();b.append('projectId','p1');b.append('ifcFileArr',new File([JSON.stringify([{locadId:'abcdef-1',children:[]}])],'1000.json'));b.append('ifcFileArr',new File(['[]'],'1001.json'));await api('/ProjectManagement/BindProject_IFCThree',{}, {method:'POST',body:b});await api('/Verification/GetCadRecordInfo',{ProjectId:'p1',subType:'5'})}
  for(const p of ['/HomePageShow/GetCurProject','/ProjectManagement/QueryProjectSign','/ProjectManagement/QueryProject'])api(p,{ProjectId:'p1'});rows();</script>`});
 };
 const config={schema:'hifc.platform-sync-config/1' as const,adapter:'real' as const,state_dir:join(root,'state'),browser_profile:join(root,'profile'),expected_project:{project_id:'p1',project_code:'C1',project_name:'School'}};
 const reopen=async()=>{const context=await browser.newContext({serviceWorkers:'block'});await context.route('**/*',serve);const adapter=new RealBrowserAdapter(config,REAL_ORIGIN+'/planning-review',context);adapter.selectModel('old-123456');return adapter;};
 const a=await reopen(),evidence=new RealEvidence(config.state_dir);
 return {a,file,calls,evidence,reopen,releaseStale,failReads:(value:boolean)=>{readFails=value;},done:()=>{calc=1;},close:async()=>{releaseStale();await a.close();evidence.close();await browser.close();}};
}
test('a truncated model label is selected by its exact full tooltip before deletion',async()=>{
 const f=await setup(false,true);try{
  const old=(await f.a.snapshot()).models[0]!;
  f.a.prepare({id:'r',baseline:old,expected_project:{project_id:'p1',project_code:'C1',project_name:'School'},non_target_models:[],handoff:{original_name:'gate.ifc',ifc_sha256:hash(bytes)}} as unknown as Run);
  await f.a.deleteModel(old,'r:delete');
  assert.equal((await f.a.snapshot()).models.length,0);
  assert.equal(f.calls.filter(x=>x==='POST /Project/DelIfcModel').length,1);
  assert.equal(f.evidence.get('r:delete')?.status,'completed');
 }finally{await f.close();}
});

test('nested project dialogs resolve only the upload window for file selection and submission',async()=>{
 const f=await setup(false,false,true);try{
  const old=(await f.a.snapshot()).models[0]!;
  f.a.prepare({id:'r',baseline:old,expected_project:{project_id:'p1',project_code:'C1',project_name:'School'},non_target_models:[],handoff:{original_name:'gate.ifc',ifc_sha256:hash(bytes)}} as unknown as Run);
  await f.a.deleteModel(old,'r:delete');
  const uploaded=await f.a.upload(f.file,hash(bytes),'r:upload');
  assert.equal(uploaded.upload_id,'upload-1');
  assert.equal((await f.a.submit(uploaded.upload_id,'gate.ifc','r:submit')).model_id,'new-abcdef');
  assert.equal(f.calls.filter(x=>x==='POST /FileManage/uploadFileChunk').length,1);
  assert.equal(f.calls.filter(x=>x==='POST /Project/AddIfcModel').length,1);
 }finally{await f.close();}
});

test('actual browser clicks and file chooser complete the scoped replacement; stale completion is ignored',async()=>{
 const f=await setup();try{
  const s=await f.a.snapshot(),old=s.models[0]!;f.a.prepare({id:'r',baseline:old,expected_project:{project_id:'p1',project_code:'C1',project_name:'School'},non_target_models:[],handoff:{original_name:'gate.ifc',ifc_sha256:hash(bytes)}} as unknown as Run);
  await f.a.deleteModel(old,'r:delete');const u=await f.a.upload(f.file,hash(bytes),'r:upload');const n=await f.a.submit(u.upload_id,'gate.ifc','r:submit');assert.equal(n.model_id,'new-abcdef');
  let p=await f.a.progress('r:submit');assert.notEqual(p.calculation,'completed');
  for(let i=0;i<30&&!p.current_calculation_proven;i++){await new Promise(r=>setTimeout(r,100));p=await f.a.progress('r:submit');if(p.calculation==='running')f.done();}
  assert.equal(p.current_calculation_proven,true);assert.equal(p.calculation,'completed');assert.equal((await f.a.snapshot()).models[0]!.sha256,hash(bytes));
  const savedAt=f.evidence.get('r:submit')!.calculation_saved_utc;await new Promise(r=>setTimeout(r,30));await f.a.progress('r:submit');
  assert.equal(f.evidence.get('r:submit')!.calculation_saved_utc,savedAt,'Repeated completion reads must not move the calculation timestamp used by report correlation');
  await assert.rejects(f.a.deleteModel(old,'r:delete'));assert.equal(f.calls.filter(x=>x==='POST /Project/DelIfcModel').length,1);assert.equal(f.calls.filter(x=>x==='POST /Project/AddIfcModel').length,1);
 }finally{await f.close();}
});

test('same-process automatic report preserves completion proof and never repeats model writes',async()=>{
 const f=await setup(false,false,true);try{
  const old=(await f.a.snapshot()).models[0]!,run={id:'report-run',project_id:'p1',baseline:old,expected_project:{project_id:'p1',project_code:'C1',project_name:'School'},non_target_models:[],handoff:{original_name:'gate.ifc',ifc_sha256:hash(bytes)}} as unknown as Run;
  f.a.prepare(run);await f.a.deleteModel(old,run.id+':delete');const u=await f.a.upload(f.file,hash(bytes),run.id+':upload');const n=await f.a.submit(u.upload_id,'gate.ifc',run.id+':submit');
  let p=await f.a.progress(run.id+':submit');for(let i=0;i<30&&!p.current_calculation_proven;i++){await new Promise(r=>setTimeout(r,100));p=await f.a.progress(run.id+':submit');if(p.calculation==='running')f.done();}
  assert.ok(p.current_calculation_proven);Object.assign(run,{state:'SUCCEEDED',calculation_status:'completed',result_id:p.result_id,new_model_id:n.model_id,new_generation:n.generation});
  const saved=f.evidence.get(run.id+':submit')!.calculation_saved_utc;
  const context=(f.a as any).context;context.request.get=async(url:string)=>{assert.match(url,/p1\/.*\.pdf$/);return {status:()=>200,body:async()=>Buffer.from('%PDF-1.7\n/CreationDate (D:'+new Date().toISOString().replace(/[-:T]/g,'').slice(0,14)+'Z)\n%%EOF\n'),dispose:async()=>{}};};
  const report=await f.a.report(run);assert.equal(report.models[0]!.name,'gate.ifc');assert.equal(report.calculation_completed_utc,saved);
  assert.equal(f.calls.filter(x=>x==='POST /Project/DelIfcModel').length,1);assert.equal(f.calls.filter(x=>x==='POST /Project/AddIfcModel').length,1);assert.equal(f.calls.filter(x=>x==='POST /ProjectManagement/BindProject_IFCThree').length,1);assert.equal(f.calls.filter(x=>x==='POST /StatisticsReport/GetHandleInfo_ZHReport').length,1);
 }finally{await f.close();}
});

test('persisted submit evidence selects and downloads the new model before recovery, including browser restart',async()=>{
 const f=await setup();let reopened:RealBrowserAdapter|undefined;try{
  const old=(await f.a.snapshot()).models[0]!;const run={id:'r',baseline:old,expected_project:{project_id:'p1',project_code:'C1',project_name:'School'},non_target_models:[],handoff:{original_name:'gate.ifc',ifc_sha256:hash(bytes)}} as unknown as Run;
  f.a.prepare(run);await f.a.deleteModel(old,'r:delete');const u=await f.a.upload(f.file,hash(bytes),'r:upload');await f.a.submit(u.upload_id,'gate.ifc','r:submit');
  run.upload_id=u.upload_id;run.intent={kind:'submit',operation_id:'r:submit',acknowledged:false};
  f.a.prepare(run);assert.equal((await f.a.snapshot()).models[0]!.sha256,hash(bytes),'same-process reconciliation must download the submitted model');
  await f.a.close();reopened=await f.reopen();reopened.prepare(run);assert.equal((await reopened.snapshot()).models[0]!.sha256,hash(bytes),'restarted reconciliation must download the submitted model');
  assert.equal(f.calls.filter(x=>x==='POST /Project/AddIfcModel').length,1);
 }finally{await reopened?.close();await f.close();}
});

test('a query issued before current calculation acceptance cannot supply a late running proof',async()=>{
 const f=await setup(true);try{
  const old=(await f.a.snapshot()).models[0]!;f.a.prepare({id:'r',baseline:old,expected_project:{project_id:'p1',project_code:'C1',project_name:'School'},non_target_models:[],handoff:{original_name:'gate.ifc',ifc_sha256:hash(bytes)}} as unknown as Run);
  await f.a.deleteModel(old,'r:delete');const u=await f.a.upload(f.file,hash(bytes),'r:upload');await f.a.submit(u.upload_id,'gate.ifc','r:submit');
  for(let i=0;i<100&&!f.evidence.get('r:submit')?.calculation_started_utc;i++)await new Promise(r=>setTimeout(r,30));
  assert.ok(f.evidence.get('r:submit')?.calculation_started_utc);f.releaseStale();
  for(let i=0;i<10;i++){await new Promise(r=>setTimeout(r,30));await f.a.progress('r:submit');}
  assert.equal(f.evidence.get('r:submit')?.calculation_running_utc,undefined);assert.equal((await f.a.progress('r:submit')).current_calculation_proven,false);
 }finally{await f.close();}
});

test('a transient calculation read failure permits readonly recovery in the same browser without write replay',async()=>{
 const f=await setup();try{
  const old=(await f.a.snapshot()).models[0]!;f.a.prepare({id:'r',baseline:old,expected_project:{project_id:'p1',project_code:'C1',project_name:'School'},non_target_models:[],handoff:{original_name:'gate.ifc',ifc_sha256:hash(bytes)}} as unknown as Run);
  await f.a.deleteModel(old,'r:delete');const u=await f.a.upload(f.file,hash(bytes),'r:upload');await f.a.submit(u.upload_id,'gate.ifc','r:submit');
  for(let i=0;i<100&&!f.evidence.get('r:submit')?.calculation_started_utc;i++)await new Promise(r=>setTimeout(r,30));
  assert.ok(f.evidence.get('r:submit')?.calculation_started_utc);f.failReads(true);await assert.rejects(f.a.progress('r:submit'));f.failReads(false);
  assert.equal((await f.a.snapshot()).models[0]!.sha256,hash(bytes));await f.a.progress('r:submit');
  assert.equal(f.calls.filter(x=>x==='POST /Project/AddIfcModel').length,1);assert.equal(f.calls.filter(x=>x==='POST /Project/DelIfcModel').length,1);
 }finally{await f.close();}
});
