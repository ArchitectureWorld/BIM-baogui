import {createServer,type IncomingMessage,type ServerResponse} from 'node:http';
import {createHash} from 'node:crypto';
import {pathToFileURL} from 'node:url';
import type {Snapshot,Model,Operation,Progress} from './types.js';
export interface FixtureFaults {loseDeleteResponse?:boolean;loseUploadResponse?:boolean;loseSubmitResponse?:boolean;conversionFails?:boolean;calculationFails?:boolean;staleComplete?:boolean;corruptBackup?:boolean;unverifiedBackup?:boolean;holdConversion?:boolean;unauthenticated?:boolean;wrongScene?:boolean}
export interface FixtureServer {url:string;close():Promise<void>;control(faults:Partial<FixtureFaults>):void;snapshot():Snapshot;counters():{delete:number;upload:number;submit:number}}
export interface FixtureOptions {faults?:FixtureFaults;port?:number;models?:Array<Model&{bytes:Buffer}>;project?:Pick<Snapshot,'project_id'|'project_code'|'project_name'>}
const sha=(bytes:Buffer)=>createHash('sha256').update(bytes).digest('hex');
const oldIfc=Buffer.from("ISO-10303-21;\nHEADER;FILE_DESCRIPTION(('old IFC'),'2;1');ENDSEC;\nDATA;#1=IFCPROJECT($);ENDSEC;\nEND-ISO-10303-21;");
const html=String.raw`<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>HIFC 本地平台替换演练</title>
<style>body{font:16px system-ui;margin:30px;background:#f4f6f8}main{max-width:900px;background:white;padding:25px}label.fileInfoLabel{display:block;border:1px solid #ddd;margin:8px;padding:12px}.fileNameSpan{display:inline-block;max-width:450px;overflow:hidden;white-space:nowrap;text-overflow:ellipsis}button,input{margin:8px;padding:8px}dialog{min-width:420px}label{display:block}output{display:block;white-space:pre-wrap}.error{color:#b00020}</style>
<main aria-label="编辑项目"><h1>编辑项目</h1><p>仅本机演练，不连接真实平台。</p><pre id="identity"></pre><section aria-label="BIM模型"><h2>BIM模型</h2><div id="models"></div><button id="add">添加IFC模型</button></section><p id="result">历史计算：已完成</p></main>
<dialog id="deleteDialog" aria-label="提示"><h2>提示</h2><p>确定要删除该模型吗?</p><p id="deleteName"></p><label>操作编号<input id="deleteOperation"></label><button id="deleteConfirm">确定</button><button onclick="this.closest('dialog').close()">取消</button><output id="deleteStatus" role="status"></output></dialog>
<dialog id="uploadDialog" aria-label="添加IFC模型"><h2>添加IFC模型</h2><label>上传操作编号<input id="uploadOperation"></label><label>输入SHA-256<input id="inputHash"></label><button id="choose">选择文件</button><input id="ifcFile" type="file" accept=".ifc" hidden><input type="file" accept=".frag" hidden><output id="uploadStatus" role="status"></output><label for="existingUpload">已上传文件</label><select id="existingUpload"><option value="">请选择</option></select><label>模型完整名称<input id="modelName"></label><label>提交操作编号<input id="submitOperation"></label><button id="submitConfirm" disabled>确定</button><button onclick="this.closest('dialog').close()">取消</button><output id="submitStatus" role="status"></output></dialog>
<script>
const el=id=>document.getElementById(id);let selected,uploadId,uploadBusy=false;
async function read(path){const r=await fetch(path);if(!r.ok)throw Error('读取失败');return r.json()}
async function send(path,body,extra={}){const r=await fetch(path,{method:'POST',headers:{'x-hifc-fixture-ui':'1',...extra},body});if(!r.ok)throw Error('操作未确认');return r.json()}
async function render(){const s=await read('/api/snapshot');el('identity').textContent='项目ID：'+s.project_id+'\n项目编号：'+s.project_code+'\n项目名称：'+s.project_name+'\n当前场景项目ID：'+s.scene_project_id;el('models').replaceChildren();for(const m of s.models){const row=document.createElement('label');row.className='fileInfoLabel';row.dataset.modelId=m.id;row.dataset.generation=m.generation;const name=document.createElement('span');name.className='fileNameSpan';name.title=m.name;name.textContent=m.name;row.append(name);const ids=document.createElement('div');ids.textContent='编号：'+m.id+'；版本：'+m.generation;row.append(ids);const btn=document.createElement('button');btn.title='删除';btn.setAttribute('aria-label','删除');btn.textContent='删除';btn.onclick=()=>{selected=m;el('deleteName').textContent=m.name;el('deleteStatus').textContent='';el('deleteDialog').showModal()};row.append(btn);el('models').append(row)}}
el('deleteConfirm').onclick=async()=>{el('deleteStatus').textContent='正在删除';try{await send('/actions/delete',JSON.stringify({model_id:selected.id,generation:selected.generation,operation_id:el('deleteOperation').value}),{'content-type':'application/json'});el('deleteStatus').textContent='删除已确认';await render();el('deleteDialog').close()}catch{el('deleteStatus').textContent='操作结果未知';}};
el('add').onclick=async()=>{uploadId=undefined;el('uploadStatus').textContent='';el('submitStatus').textContent='';el('submitConfirm').disabled=true;el('ifcFile').value='';el('existingUpload').replaceChildren(new Option('请选择',''));for(const item of await read('/api/uploads'))el('existingUpload').append(new Option(item.id+' '+item.sha256,item.id));el('uploadDialog').showModal()};
el('existingUpload').onchange=()=>{uploadId=el('existingUpload').value;el('submitConfirm').disabled=!uploadId};
el('choose').onclick=()=>el('ifcFile').click();
el('ifcFile').onchange=async()=>{const file=el('ifcFile').files[0];if(!file||uploadBusy)return;uploadBusy=true;el('uploadStatus').textContent='正在上传';try{const r=await send('/actions/upload',await file.arrayBuffer(),{'content-type':'application/octet-stream','x-operation-id':el('uploadOperation').value,'x-input-sha256':el('inputHash').value});uploadId=r.upload_id;el('uploadStatus').textContent='上传已确认：'+uploadId;el('submitConfirm').disabled=false}catch{el('uploadStatus').textContent='操作结果未知'}finally{uploadBusy=false}};
el('submitConfirm').onclick=async()=>{el('submitConfirm').disabled=true;el('submitStatus').textContent='正在提交';try{const r=await send('/actions/submit',JSON.stringify({upload_id:uploadId,name:el('modelName').value,operation_id:el('submitOperation').value}),{'content-type':'application/json'});el('submitStatus').textContent='提交已确认：'+JSON.stringify(r);await render();el('uploadDialog').close()}catch{el('submitStatus').textContent='操作结果未知'}};
render();setInterval(()=>render().catch(()=>{}),250);
</script></html>`;

/** Deliberately documented local fixture contract; it never contacts a remote service.
 * Read APIs: snapshot, backup bytes+metadata, operation acknowledgment and correlated progress.
 * Mutation endpoints are called only by the fixture page's visible confirmation/filechooser flow.
 * Conversion is a deterministic simulation; passing it does not certify the live IFC/FRAG frontend.
 */
export async function startFixture(options:FixtureOptions={}):Promise<FixtureServer>{
 const faults:FixtureFaults={...options.faults},counts={delete:0,upload:0,submit:0};let revision=1,sequence=1,url='';
 const seed=options.models??[{id:'m1',name:'OUT_School_20261022_V1.ifc',generation:'g1',bytes:oldIfc}];
 const models=new Map<string,Model&{bytes:Buffer}>(seed.map(m=>[m.id,{...m,sha256:sha(m.bytes),bytes:Buffer.from(m.bytes)}]));
 const operations=new Map<string,Operation>(),uploads=new Map<string,{bytes:Buffer;sha256:string}>(),jobs=new Map<string,{value:Progress;started:number}>();
 function snapshot():Snapshot{return {authenticated:!faults.unauthenticated,...(options.project??{project_id:'p1',project_code:'SCHOOL-001',project_name:'School'}),scene_project_id:faults.wrongScene?'wrong-project':options.project?.project_id??'p1',revision:'r'+revision,models:[...models.values()].map(({bytes,...m})=>({...m,sha256:faults.unverifiedBackup?undefined:m.sha256}))};}
 function json(res:ServerResponse,status:number,data:unknown){res.writeHead(status,{'content-type':'application/json; charset=utf-8','x-hifc-fixture':'hifc-sync-fixture/1','cache-control':'no-store'});res.end(JSON.stringify(data));}
 function loseResponse(res:ServerResponse){res.writeHead(200,{'content-type':'application/json','content-length':'1000'});res.flushHeaders();res.write('{"interrupted":');setTimeout(()=>res.destroy(),10);}
 async function body(req:IncomingMessage){const parts:Buffer[]=[];let size=0;for await(const item of req){const chunk=Buffer.from(item);size+=chunk.length;if(size>100*1024*1024)throw Error('Request too large.');parts.push(chunk);}return Buffer.concat(parts);}
 const server=createServer(async(req,res)=>{try{
  const endpoint=new URL(req.url??'/',url||'http://127.0.0.1');
  if(req.method==='GET'&&endpoint.pathname==='/'){res.writeHead(200,{'content-type':'text/html; charset=utf-8','content-security-policy':"default-src 'self'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'self'; img-src 'self' data:",'x-hifc-fixture':'hifc-sync-fixture/1'});res.end(html);return;}
  if(req.method==='GET'&&endpoint.pathname==='/api/snapshot'){json(res,200,snapshot());return;}
  if(req.method==='GET'&&endpoint.pathname==='/api/uploads'){json(res,200,[...uploads].map(([id,value])=>({id,sha256:value.sha256})));return;}
  if(req.method==='GET'&&endpoint.pathname==='/api/backup'){
   const m=models.get(endpoint.searchParams.get('model_id')??'');if(!m||m.generation!==endpoint.searchParams.get('generation')){json(res,404,{error:'Model generation not found.'});return;}
   res.writeHead(200,{'content-type':'application/octet-stream','x-hifc-fixture':'hifc-sync-fixture/1','x-model-id':m.id,'x-generation':m.generation,'x-ifc-sha256':m.sha256!,'cache-control':'no-store'});res.end(faults.corruptBackup?Buffer.from('corrupted'):m.bytes);return;
  }
  if(req.method==='GET'&&endpoint.pathname==='/api/operation'){
   const id=endpoint.searchParams.get('id')??'',kind=id.endsWith(':upload')?'upload':id.endsWith(':submit')?'submit':'delete';json(res,200,operations.get(id)??{id,kind,status:'absent'});return;
  }
  if(req.method==='GET'&&endpoint.pathname==='/api/progress'){
   const id=endpoint.searchParams.get('id')??'',job=jobs.get(id);if(!job){json(res,404,{error:'No progress for this operation.'});return;}
   const p={...job.value},age=Date.now()-job.started;
   if(!faults.holdConversion&&age>=40){p.conversion=faults.conversionFails?'failed':'completed';if(p.conversion==='completed')p.calculation=age>=90?(faults.calculationFails?'failed':'completed'):'running';}
   if(p.calculation==='completed'){p.result_id='result-'+p.generation;p.regulation='failed';}
   if(faults.staleComplete){p.operation_id='historical-submit';p.conversion='completed';p.calculation='completed';p.result_id='historical-result';}
   json(res,200,p);return;
  }
  if(req.method!=='POST'||!endpoint.pathname.startsWith('/actions/')||req.headers['x-hifc-fixture-ui']!=='1'||req.headers.origin!==url){json(res,403,{error:'Fixture UI actions only.'});return;}
  if(faults.unauthenticated){json(res,401,{error:'Login required.'});return;}
  const bytes=await body(req);
  if(endpoint.pathname==='/actions/delete'){
   const value=JSON.parse(bytes.toString('utf8')) as {model_id:string;generation:string;operation_id:string};if(!value.operation_id){json(res,400,{error:'Operation required.'});return;}
   const previous=operations.get(value.operation_id);if(previous){json(res,200,previous);return;}
   const m=models.get(value.model_id);if(!m||m.generation!==value.generation){json(res,409,{error:'Model generation changed.'});return;}
   models.delete(m.id);counts.delete++;revision++;const op:Operation={id:value.operation_id,kind:'delete',status:'completed',model_id:m.id,generation:m.generation};operations.set(op.id,op);
   if(faults.loseDeleteResponse){loseResponse(res);return;}json(res,200,op);return;
  }
  if(endpoint.pathname==='/actions/upload'){
   const id=String(req.headers['x-operation-id']??''),expected=String(req.headers['x-input-sha256']??'');if(!id||sha(bytes)!==expected||!bytes.toString('utf8').startsWith('ISO-10303-21;')){json(res,400,{error:'IFC hash or operation invalid.'});return;}
   const previous=operations.get(id);if(previous){json(res,200,previous);return;}
   const uploadId='u'+(uploads.size+1);uploads.set(uploadId,{bytes:Buffer.from(bytes),sha256:expected});operations.set(id,{id,kind:'upload',status:'completed',upload_id:uploadId,sha256:expected});counts.upload++;
   if(faults.loseUploadResponse){loseResponse(res);return;}json(res,200,{upload_id:uploadId});return;
  }
  if(endpoint.pathname==='/actions/submit'){
   const value=JSON.parse(bytes.toString('utf8')) as {upload_id:string;name:string;operation_id:string};const previous=operations.get(value.operation_id);if(previous){json(res,200,previous);return;}
   const input=uploads.get(value.upload_id);if(!input||!value.operation_id||!value.name||[...models.values()].some(m=>m.name===value.name)){json(res,409,{error:'Upload missing or filename conflict.'});return;}
   const n=++sequence,m:Model&{bytes:Buffer}={id:'m'+n,generation:'g'+n,name:value.name,sha256:input.sha256,bytes:Buffer.from(input.bytes)};models.set(m.id,m);revision++;counts.submit++;
   operations.set(value.operation_id,{id:value.operation_id,kind:'submit',status:'completed',model_id:m.id,generation:m.generation,sha256:m.sha256});jobs.set(value.operation_id,{started:Date.now(),value:{operation_id:value.operation_id,model_id:m.id,generation:m.generation,input_sha256:input.sha256,conversion:'pending',calculation:'not_started',regulation:'unknown'}});
   if(faults.loseSubmitResponse){loseResponse(res);return;}json(res,200,{model_id:m.id,generation:m.generation});return;
  }
  json(res,404,{error:'Unknown fixture endpoint.'});
 }catch{if(!res.headersSent)json(res,400,{error:'Fixture request rejected.'});else res.destroy();}});
 await new Promise<void>((resolve,reject)=>{server.once('error',reject);server.listen(options.port??0,'127.0.0.1',()=>resolve());});
 const address=server.address();if(!address||typeof address==='string')throw Error('Fixture bind failed.');url='http://127.0.0.1:'+address.port;
 return {url,control(value){Object.assign(faults,value);},snapshot:()=>structuredClone(snapshot()),counters:()=>({...counts}),async close(){server.closeAllConnections();await new Promise<void>((resolve,reject)=>server.close(error=>error?reject(error):resolve()));}};
}
if(process.argv[1]&&import.meta.url===pathToFileURL(process.argv[1]).href){const server=await startFixture();process.stdout.write(JSON.stringify({fixture_url:server.url})+'\n');process.on('SIGINT',()=>void server.close().then(()=>process.exit(0)));}
