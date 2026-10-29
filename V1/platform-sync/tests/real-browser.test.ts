import test from 'node:test';
import assert from 'node:assert/strict';
import {chromium} from 'playwright';
import {mkdtempSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {RealBrowserAdapter} from '../src/real-browser.js';
import {hash} from '../src/security.js';
import {API_PATH,REAL_ORIGIN} from '../src/real-contract.js';

const project={project_id:'p1',project_name:'Test school',project_code:'CODE1'};
const bytes=Buffer.from("ISO-10303-21;\nHEADER;FILE_SCHEMA(('IFC2X3'));ENDSEC;\nDATA;#1=IFCPROJECT('0',$,'School',$,$,$,$,$,$);ENDSEC;\nEND-ISO-10303-21;\n");
async function fixture(state=0){
 const root=mkdtempSync(join(tmpdir(),'real-browser-'));const browser=await chromium.launch({headless:true,...(process.env.HIFC_SYNC_CHROMIUM?{executablePath:process.env.HIFC_SYNC_CHROMIUM}:process.platform==='win32'?{channel:'msedge'}:{})});const context=await browser.newContext({serviceWorkers:'block'});
 const calls:string[]=[];const models=[{ID:'gate',PROJECTID:'p1',NAME:'gate.ifc',MODELPATH:'Upload/Leave/20261026/gate.ifc',ADDTIME:'2026-10-26T00:00:00',MODELTYPE:2,THREEVER:100,LIGHTSTATE:1}];
 await context.route('**/*',async route=>{const u=new URL(route.request().url());calls.push(u.pathname);
  let data:unknown;if(u.pathname.endsWith('/GetCurProject'))data='p1';else if(u.pathname.endsWith('/QueryProjectSign'))data={ID:'p1',NAME:'Test school',PLATFORMPROCODE:'CODE1',APPROVALSTATE:state};else if(u.pathname.endsWith('/GetIfcInfos'))data=models;else if(u.pathname.includes('/Upload/'))return route.fulfill({body:bytes,contentType:'application/octet-stream'});else if(u.pathname.includes('/api/'))data=true;
  if(data!==undefined)return route.fulfill({body:JSON.stringify(data),contentType:'application/json'});
  return route.fulfill({contentType:'text/html; charset=utf-8',body:`<div id="addProject"><input placeholder="请输入项目名称" value="Test school"><input placeholder="请输入联审平台立项项目代码" value="CODE1"><div role="tab" aria-selected="true">BIM模型</div><label class="fileInfoLabel"><span class="fileNameSpan" title="gate.ifc">gate.ifc</span></label></div><script>for(const p of ['HomePageShow/GetCurProject','ProjectManagement/QueryProjectSign','Project/GetIfcInfos'])fetch('${API_PATH}/'+p+'?ProjectId=p1&proId=p1&modelType=2');</script>`});
 });
 const config={schema:'hifc.platform-sync-config/1' as const,adapter:'real-readonly' as const,state_dir:join(root,'state'),browser_profile:join(root,'profile'),expected_project:project};
 const adapter=new RealBrowserAdapter(config,REAL_ORIGIN+'/planning-review',context);
 adapter.selectModel('gate');
 return {adapter,calls,models,close:async()=>{await adapter.close();await browser.close();}};
}
test('real reads reconcile visible project, scene and model bytes; readonly adapter cannot write',async()=>{
 const f=await fixture();try{const s=await f.adapter.snapshot();assert.equal(s.project_id,'p1');assert.equal(s.writable,true);assert.equal(s.models[0]!.sha256,hash(bytes));const b=await f.adapter.backup(s.models[0]!);assert.equal(b.sha256,hash(bytes));await assert.rejects(f.adapter.deleteModel(s.models[0]!,'r:delete'),/read.only/i);assert.ok(!f.calls.some(x=>x.endsWith('/DelIfcModel')));}finally{await f.close();}
});
test('review-state remains readable but is never writable',async()=>{const f=await fixture(1);try{assert.equal((await f.adapter.snapshot()).writable,false);}finally{await f.close();}});
test('a changed remote generation invalidates the previous-file backup',async()=>{const f=await fixture();try{const s=await f.adapter.snapshot();f.models[0]!.MODELPATH='Upload/Leave/20261026/new.ifc';await assert.rejects(f.adapter.backup(s.models[0]!),/changed|match/i);}finally{await f.close();}});
