import {readFileSync,writeFileSync,existsSync,mkdirSync,readdirSync,statSync,copyFileSync,constants,openSync,fsyncSync,closeSync,linkSync,unlinkSync} from 'node:fs';
import {randomUUID} from 'node:crypto';
import {join,dirname,extname,isAbsolute,resolve} from 'node:path';
import {Store} from './store.js';
import {hash,redact} from './security.js';
import {SyncError,type Run,type PlatformAdapter,type ReportPayload,type ReportDelivery} from './types.js';

const fail=(message:string):never=>{throw new SyncError('BLOCKED',message);};
export function assertPdf(bytes:Buffer){
 if(bytes.length<32||bytes.length>100*1024*1024||!/^%PDF-1\.[0-9]|^%PDF-2\.0/.test(bytes.subarray(0,12).toString('ascii'))||!bytes.subarray(-2048).toString('latin1').trimEnd().endsWith('%%EOF'))fail('平台未返回完整 PDF，可能为登录页、错误页或下载中断。');
}
function safeName(value:string){const name=value.normalize('NFC').replace(/[<>:"/\\|?*\x00-\x1f]/g,'_').replace(/[. ]+$/g,'').trim().slice(0,100).replace(/[. ]+$/g,'');if(!name)fail('平台项目名称为空，无法命名报告。');return name;}
function dateKey(now:Date){return `${now.getFullYear()}${String(now.getMonth()+1).padStart(2,'0')}${String(now.getDate()).padStart(2,'0')}`;}
function destination(run:Run){const doc=run.handoff.document_path;if(!isAbsolute(doc)||extname(doc).toLowerCase()!=='.rvt'||!existsSync(doc)||!statSync(doc).isFile())fail('来源 RVT 不存在或尚未保存，不能确定报告保存目录。');return dirname(resolve(doc));}
function verifyPayload(run:Run,p:ReportPayload){
 if(p.project_id!==run.project_id||p.result_id!==run.result_id||!p.calculation_completed_utc||!Number.isFinite(Date.parse(p.calculation_completed_utc)))fail('报告对应的项目或本次计算结果无法核实。');
 const expected=[...(run.non_target_models??[]),{id:run.new_model_id,name:run.handoff.original_name,generation:run.new_generation}];
 if(!Array.isArray(run.non_target_models)||!Array.isArray(p.models)||!p.models.length||new Set(p.models.map(m=>m.id)).size!==p.models.length||!p.models.some(m=>m.id===run.new_model_id)||p.models.some(m=>!m.name||!expected.some(e=>e.id===m.id&&e.name===m.name&&e.generation===m.generation)))fail('审查模型清单与本次计算不一致，未归档报告。');
 assertPdf(p.pdf);
}
function durable(path:string,bytes:Buffer){
 if(!existsSync(path))writeFileSync(path,bytes,{flag:'wx'});
 if(hash(readFileSync(path))!==hash(bytes))fail('内部报告冻结文件已变化。');
 const fd=openSync(path,'r+');try{fsyncSync(fd);}finally{closeSync(fd);}
}
function publish(from:string,to:string,sha:string){
 if(!existsSync(from)||hash(readFileSync(from))!==sha)fail('内部报告文件缺失或摘要不符，未覆盖交付。');
 if(!existsSync(to)){
  const staging=join(dirname(to),'.hifc-report-'+randomUUID()+'.tmp');
  try{copyFileSync(from,staging,constants.COPYFILE_EXCL);const fd=openSync(staging,'r+');try{fsyncSync(fd);}finally{closeSync(fd);}try{linkSync(staging,to);}catch(e){if(!existsSync(to))throw e;}}
  finally{if(existsSync(staging))unlinkSync(staging);}
 }
 if(hash(readFileSync(to))!==sha)fail('目标报告文件已存在且内容不同，已保留原文件。');
 const fd=openSync(to,'r+');try{fsyncSync(fd);}finally{closeSync(fd);}
}
export class ReportService {
 constructor(private store:Store,private adapter:Pick<PlatformAdapter,'report'|'prepare'>,private options={now:()=>new Date()}){}
 async download(runId:string):Promise<Run>{
  const run=this.store.run(runId);
  if(run.state!=='SUCCEEDED'||run.calculation_status!=='completed'||!run.result_id||run.rollback_of)fail('仅本次计算已核验成功的同步任务可以下载报告。');
  this.store.acquire(run.project_id,run.id,true);
  try{
   const dir=destination(run);let r=run.report;
   if(!r?.frozen_pdf||!r.frozen_txt||!r.pdf_sha256||!r.txt_sha256||!r.pdf_path||!r.txt_path){
    run.report={state:'DOWNLOADING',reason:'正在核对本次计算并下载平台报告。',updated_utc:this.options.now().toISOString()};this.store.save(run,'report');
    if(!this.adapter.report)fail('当前平台适配器尚不支持报告下载。');this.adapter.prepare?.(run);
    const payload=await this.adapter.report!(run);verifyPayload(run,payload);const now=this.options.now(),name='DOC_'+safeName(run.expected_project.project_name)+'_'+dateKey(now);
    const match=new RegExp('^'+name.replace(/[.*+?^${}()|[\]\\]/g,'\\$&')+'_V([1-9][0-9]*)\\.(?:pdf|txt)$','i');
    const versions=readdirSync(dir).map(file=>match.exec(file)).filter(Boolean).map(m=>Number(m![1]));const version=Math.max(0,...versions)+1;if(!Number.isSafeInteger(version))fail('报告版本号异常。');
    const base=join(dir,name+'_V'+version),downloaded=now.toISOString();
    const lines=['综合报告对应模型说明','项目名称：'+run.expected_project.project_name,'项目编号：'+run.expected_project.project_code,
     '报告文件：'+name+'_V'+version+'.pdf','来源 RVT：'+run.handoff.document_path,
     '计算完成时间：'+payload.calculation_completed_utc,'报告生成时间：'+(payload.generated_utc??'平台未提供可核验时间'),'下载时间：'+downloaded,
     '说明：以下为本次计算实际包含的模型；报告下载成功不代表法规审查通过。','',...payload.models.map((m,i)=>`${i+1}. ${m.name.replace(/[\r\n\x00-\x1f]/g,' ')}\r\n   模型 ID：${m.id}\r\n   版本标识：${m.generation}`)];
    const txt=Buffer.from('\ufeff'+lines.join('\r\n')+'\r\n','utf8'),pdfHash=hash(payload.pdf),txtHash=hash(txt),spool=join(this.store.directory,'reports',hash(run.id));mkdirSync(spool,{recursive:true});
    const frozenPdf=join(spool,pdfHash+'.pdf'),frozenTxt=join(spool,txtHash+'.txt');durable(frozenPdf,payload.pdf);durable(frozenTxt,txt);
    r=run.report={state:'PUBLISHING',reason:'报告已核验，正在保存 PDF 和说明文件。',updated_utc:downloaded,downloaded_utc:downloaded,calculation_completed_utc:payload.calculation_completed_utc,generated_utc:payload.generated_utc,models:payload.models,pdf_path:base+'.pdf',txt_path:base+'.txt',pdf_sha256:pdfHash,txt_sha256:txtHash,frozen_pdf:frozenPdf,frozen_txt:frozenTxt};this.store.save(run,'report-intent');
   }
   if(dirname(resolve(r.pdf_path!))!==dir||dirname(resolve(r.txt_path!))!==dir||r.txt_path!==r.pdf_path!.replace(/\.pdf$/,'.txt'))fail('报告发布路径与来源 RVT 不一致。');
   // The persisted intent plus content hashes makes a crash between the two files recoverable.
   publish(r.frozen_pdf!,r.pdf_path!,r.pdf_sha256!);publish(r.frozen_txt!,r.txt_path!,r.txt_sha256!);
   r.state='SUCCEEDED';r.reason='PDF 与对应模型说明已保存。';r.updated_utc=this.options.now().toISOString();this.store.save(run,'report-complete');
  }catch(e){
   const error=e as Error;run.report={...run.report,state:e instanceof SyncError?(e.code==='LOGIN'?'NEEDS_LOGIN':'BLOCKED'):'FAILED',reason:redact(error.message),updated_utc:this.options.now().toISOString()};this.store.save(run,'report-failed');
  }finally{this.store.release(run.project_id,run.id);}
  return this.store.run(run.id);
 }
}
