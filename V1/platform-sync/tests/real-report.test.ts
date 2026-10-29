import test from 'node:test';import assert from 'node:assert/strict';
import {chromium} from 'playwright';
import {hash} from '../src/security.js';import {REAL_ORIGIN,API_PATH} from '../src/real-contract.js';
const modulePath='../src/real-report.js';
const project='1717b620-7dbd-40c9-b18d-28f364be1ae3';
const pdf=Buffer.from("%PDF-1.7\n/CreationDate (D:20261026145244+08'00')\n%%EOF\n");
async function code(){const m=await import(modulePath).catch(()=>({}));assert.equal(typeof m.downloadRealReport,'function','Verified platform report download must be implemented');return m;}
test('real report URL is constrained to this project PDF and rejects traversal/other hosts',async()=>{const m=await code();
 assert.match(m.reportFileUrl(`Upload\\Leave\\20261026\\${project}\\综合报告\\学校.pdf`,project),/\.pdf$/);
 for(const path of [`Upload/Leave/20261026/another/综合报告/a.pdf`,`https://evil.test/a.pdf`,`Upload/Leave/20261026/${project}/综合报告/../a.pdf`,`Upload/Leave/20261026/${project}/综合报告/a.html`])assert.throws(()=>m.reportFileUrl(path,project));
});
test('current calculation proof maps actual IDs to frozen names and rejects stale records',async()=>{const m=await code(),body={CHECKSTATE:1,RECORDID:'result-record'};
 const models=[{id:'site',name:'site.ifc',generation:'1'},{id:'gate',name:'gate.ifc',generation:'2'},{id:'surroundings',name:'reference.ifc',generation:'3'}];
 const evidence={project_id:project,model_id:'gate',generation:'2',calculation_observation_version:2,calculation_started_utc:'2026-10-26T06:47:04Z',calculation_running_utc:'2026-10-26T06:47:08Z',calculation_saved_utc:'2026-10-26T06:47:23Z',calculation_result_hash:hash(JSON.stringify(body)),calculation_models:['site','gate']};
 const run={project_id:project,expected_project:{project_id:project,project_name:'School',project_code:'C'},new_model_id:'gate',new_generation:'2',handoff:{original_name:'gate.ifc'},non_target_models:[models[0],models[2]],result_id:'observed-save:'+evidence.calculation_result_hash};
 const snap={project_id:project,scene_project_id:project,models};
 assert.deepEqual(m.verifyReportCalculation(run,evidence,body,snap).models.map((x:any)=>x.name),['site.ifc','gate.ifc']);
 assert.throws(()=>m.verifyReportCalculation(run,evidence,{CHECKSTATE:1,RECORDID:'new-record'},snap));
 assert.throws(()=>m.verifyReportCalculation(run,evidence,body,{...snap,models:[...models,{id:'foreign',name:'foreign.ifc',generation:'x'}]}));
});
test('normal report button fetches native PDF without any model mutation; stale PDF rejected',async()=>{const m=await code();
 const browser=await chromium.launch({headless:true,...(process.platform==='win32'?{channel:'msedge'}:{})});try{for(const stale of [false,true]){
 const context=await browser.newContext(),page=await context.newPage(),calls:string[]=[];
 await context.route('**/*',async route=>{const req=route.request(),u=new URL(req.url());calls.push(req.method()+' '+u.pathname);
 if(u.pathname.endsWith('/GetHandleInfo_ZHReport'))return route.fulfill({contentType:'application/json',body:JSON.stringify({recordType:'3',filePath:`Upload/Leave/20261026/${project}/综合报告/学校.pdf`})});
 return route.fulfill({contentType:'text/html; charset=utf-8',body:`<button onclick="const b=new FormData();b.append('PROJECTID','${project}');b.append('RecordType','3');b.append('GetRecordByType','0');b.append('RECORDOLDID','');b.append('ProOldId','');fetch('${API_PATH}/StatisticsReport/GetHandleInfo_ZHReport',{method:'POST',body:b})">综合报告</button>`});});
 // APIRequestContext is separate from page routing; inject a scoped read transport.
 await page.goto(REAL_ORIGIN+'/planning-review');
 const result={project_id:project,result_id:'r1',models:[{id:'gate',name:'gate.ifc',generation:'1'}],calculation_completed_utc:stale?'2026-10-26T08:00:00Z':'2026-10-26T06:47:23Z'};
 const get=async(url:string)=>{assert.match(url,/\/综合报告\/|%E7%BB%BC/);return pdf;};
 const action=()=>m.downloadRealReport(page,{project_id:project},async()=>result,get);
 if(stale)await assert.rejects(action,/旧|早于/);else assert.deepEqual((await action()).pdf,pdf);
 assert.equal(calls.filter(x=>x.startsWith('POST')).length,1);assert.ok(calls.every(x=>!/(DelIfc|AddIfc|BindProject)/.test(x)));await context.close();
 }}finally{await browser.close();}
});
