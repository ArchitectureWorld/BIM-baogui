import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtempSync,writeFileSync,readFileSync,existsSync,unlinkSync,readdirSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join,basename} from 'node:path';
import {Store} from '../src/store.js';
import type {Run,PlatformAdapter} from '../src/types.js';
const modulePath='../src/report.js';
const pdf=Buffer.from('%PDF-1.7\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n');
async function setup(){
 const mod=await import(modulePath).catch(()=>({}));assert.equal(typeof mod.ReportService,'function','ReportService must be implemented');
 const root=mkdtempSync(join(tmpdir(),'hifc-report-')),store=new Store(join(root,'state')),document=join(root,'学校_大门.rvt');writeFileSync(document,'test model');
 const models=[{id:'site',name:'OUT_学校_报规_总图_20261026_V2.ifc',generation:'g1'},{id:'gate',name:'OUT_学校_报规_大门_20261026_V1.ifc',generation:'g2'}];
 const run={id:'run1',project_id:'p1',state:'SUCCEEDED',calculation_status:'completed',result_id:'result1',expected_project:{project_id:'p1',project_name:'学校/建设:项目',project_code:'C1'},handoff:{document_path:document,original_name:models[1]!.name,ifc_sha256:'sha'},new_model_id:'gate',new_generation:'g2',non_target_models:[models[0]],created_utc:'2026-10-26T06:00:00Z'} as Run;store.save(run);
 let calls=0,bad='',date=new Date(2026,9,26,15,0,0);
 const adapter={mode:'fixture',report:async()=>{calls++;if(bad==='network')throw Error('offline');return {project_id:bad==='project'?'wrong':'p1',result_id:bad==='stale'?'old':'result1',models:bad==='model'?[{...models[1],generation:'changed'}]:models,pdf:bad==='pdf'?Buffer.from('<html>login</html>'):pdf,calculation_completed_utc:'2026-10-26T06:47:23Z',generated_utc:'2026-10-26T06:52:44Z'};}} as unknown as PlatformAdapter;
 const service=new mod.ReportService(store,adapter,{now:()=>date});
 return {root,store,run,service,models,calls:()=>calls,bad:(s:string)=>{bad=s;},date:(d:Date)=>{date=d;}};
}
test('paired native PDF and UTF8 TXT contain every actual model beside source RVT',async()=>{const c=await setup();try{
 const r=await c.service.download(c.run.id);assert.equal(r.state,'SUCCEEDED');assert.equal(r.report.state,'SUCCEEDED');
 assert.equal(basename(r.report.pdf_path),'DOC_学校_建设_项目_20261026_V1.pdf');assert.equal(r.report.txt_path,r.report.pdf_path.replace(/\.pdf$/,'.txt'));
 assert.deepEqual(readFileSync(r.report.pdf_path),pdf);const txt=readFileSync(r.report.txt_path,'utf8');for(const m of c.models)assert.ok(txt.includes(m.name));assert.ok(txt.startsWith('\ufeff'));assert.match(txt,/计算完成时间/);
 assert.deepEqual(readdirSync(c.root).filter(x=>x.startsWith('DOC_')).sort(),[basename(r.report.pdf_path),basename(r.report.txt_path)].sort());
}finally{c.store.close();}});
test('daily versions consider either extension and preserve existing files',async()=>{const c=await setup();try{
 writeFileSync(join(c.root,'DOC_学校_建设_项目_20261026_V3.txt'),'old');let r=await c.service.download(c.run.id);assert.match(r.report.pdf_path,/_V4\.pdf$/);
 const next={...c.run,id:'run2'};c.store.save(next);c.date(new Date(2026,9,27,1));r=await c.service.download(next.id);assert.match(r.report.pdf_path,/_20261027_V1\.pdf$/);assert.equal(readFileSync(join(c.root,'DOC_学校_建设_项目_20261026_V3.txt'),'utf8'),'old');
}finally{c.store.close();}});
test('retry reuses frozen pair without fetching or altering successful sync',async()=>{const c=await setup();try{
 let r=await c.service.download(c.run.id);const path=r.report.pdf_path;unlinkSync(r.report.txt_path);c.bad('network');r=await c.service.download(c.run.id);
 assert.equal(r.report.state,'SUCCEEDED');assert.equal(r.report.pdf_path,path);assert.ok(existsSync(r.report.txt_path));assert.equal(c.calls(),1);assert.equal(r.state,'SUCCEEDED');
}finally{c.store.close();}});
test('bad project, stale calculation, changed model and HTML never publish a report',async()=>{for(const bad of ['project','stale','model','pdf','network']){const c=await setup();try{
 c.bad(bad);const r=await c.service.download(c.run.id);assert.equal(r.state,'SUCCEEDED');assert.notEqual(r.report.state,'SUCCEEDED');assert.equal(readdirSync(c.root).filter(x=>x.startsWith('DOC_')).length,0);
}finally{c.store.close();}}});
test('modified delivered file is never overwritten during retry',async()=>{const c=await setup();try{
 const first=await c.service.download(c.run.id);writeFileSync(first.report.txt_path,'manual edit');const r=await c.service.download(c.run.id);assert.notEqual(r.report.state,'SUCCEEDED');assert.equal(readFileSync(first.report.txt_path,'utf8'),'manual edit');assert.equal(c.calls(),1);
}finally{c.store.close();}});
test('unfinished sync and unsaved RVT cannot start report request',async()=>{const c=await setup();try{
 c.run.state='CALCULATING';c.store.save(c.run);await assert.rejects(()=>c.service.download(c.run.id));assert.equal(c.calls(),0);
 c.run.state='SUCCEEDED';c.run.handoff.document_path='';c.store.save(c.run);const r=await c.service.download(c.run.id);assert.notEqual(r.report.state,'SUCCEEDED');assert.equal(c.calls(),0);
}finally{c.store.close();}});
