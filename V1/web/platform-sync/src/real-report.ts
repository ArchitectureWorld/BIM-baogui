import type {Page,Route} from 'playwright';
import {API_PATH,REAL_ORIGIN} from './real-contract.js';
import type {Evidence} from './real-evidence.js';
import {multipart,calculationState} from './real-calculation.js';
import {hash} from './security.js';
import {assertPdf} from './report.js';
import {SyncError,type Run,type Snapshot,type ReportPayload} from './types.js';
const endpoint='/StatisticsReport/GetHandleInfo_ZHReport';
const fail=(message:string):never=>{throw new SyncError('BLOCKED',message);};
export type ReportCalculation=Omit<ReportPayload,'pdf'|'generated_utc'>;
export function verifyReportCalculation(run:Run,e:Evidence|undefined,body:unknown,s:Pick<Snapshot,'project_id'|'scene_project_id'|'models'>):ReportCalculation{
 if(!e||e.project_id!==run.project_id||e.model_id!==run.new_model_id||e.generation!==run.new_generation||e.calculation_observation_version!==2||!e.calculation_started_utc||!e.calculation_running_utc||!e.calculation_saved_utc||!e.calculation_models||!e.calculation_result_hash||run.result_id!=='observed-save:'+e.calculation_result_hash||hash(JSON.stringify(body))!==e.calculation_result_hash||calculationState(body)!=='completed')fail('平台当前计算与该任务不一致，不能将旧报告作为本次综合报告。');
 const expected=[...(run.non_target_models??[]),{id:run.new_model_id,name:run.handoff.original_name,generation:run.new_generation}];
 if(!Array.isArray(run.non_target_models)||s.project_id!==run.project_id||s.scene_project_id!==run.project_id||s.models.length!==expected.length||new Set(s.models.map(m=>m.id)).size!==expected.length||expected.some(m=>!s.models.some(x=>x.id===m.id&&x.name===m.name&&x.generation===m.generation)))fail('平台项目或模型清单已变化，请先核对本次计算。');
 if(new Set(e!.calculation_models).size!==e!.calculation_models!.length||!e!.calculation_models!.includes(run.new_model_id!))fail('本次参与计算的模型记录不完整。');
 const models=e!.calculation_models!.map(id=>{const m=s.models.find(x=>x.id===id);if(!m)fail('本次审查模型已不存在。');return m!;});
 return {project_id:run.project_id,result_id:run.result_id!,models,calculation_completed_utc:e!.calculation_saved_utc!};
}
export function reportFileUrl(value:unknown,project:string):string{
 if(typeof value!=='string')fail('平台未提供综合报告地址。');
 let path:string;try{path=decodeURIComponent(value as string).replace(/\\/g,'/').replace(/^\/+/, '');}catch{return fail('综合报告地址编码无效。');}
 const pieces=path.split('/');
 if(pieces.length!==6||pieces[0]!=='Upload'||pieces[1]!=='Leave'||!/^\d{8}$/.test(pieces[2]!)||pieces[3]!==project||pieces[4]!=='综合报告'||!/^.+\.pdf$/i.test(pieces[5]!)||pieces.some(p=>!p||p==='.'||p==='..'||/[?#:%\x00-\x1f]/.test(p)))fail('综合报告地址不属于本项目的原生 PDF。');
 return REAL_ORIGIN+API_PATH+'/'+pieces.map(encodeURIComponent).join('/');
}
export function pdfCreationTime(bytes:Buffer):string{
 const m=/\/CreationDate\s*\(D:(\d{4})(\d{2})(\d{2})(\d{2})(\d{2})(\d{2})(Z|[+-]\d{2}'?\d{2}'?)\)/.exec(bytes.toString('latin1'));
 if(!m)fail('PDF 缺少可核验的生成时间，暂不能确认是本次报告。');
 const tz=m![7]==='Z'?'Z':m![7]!.replace(/'/g,'').replace(/^([+-]\d{2})(\d{2})$/,'$1:$2');
 const date=new Date(`${m![1]}-${m![2]}-${m![3]}T${m![4]}:${m![5]}:${m![6]}${tz}`);if(!Number.isFinite(date.getTime()))fail('PDF 生成时间无效。');return date.toISOString();
}
export async function closeProjectWindows(page:Page){
 const form=page.locator('#addProject:visible');
 if(await form.count())await form.locator('xpath=ancestor::*[contains(concat(" ",normalize-space(@class)," ")," dialogBox ")][1]').locator('i.el-icon-close:visible').click();
 const heading=page.locator('.headhead:visible').filter({hasText:'项目管理'});
 if(await heading.count()===1)await heading.locator('xpath=ancestor::*[contains(concat(" ",normalize-space(@class)," ")," dialogBox ")][1]').locator('i.el-icon-close:visible').click();
}
export async function downloadRealReport(page:Page,run:Pick<Run,'project_id'>,check:()=>Promise<ReportCalculation>,readPdf?:(url:string)=>Promise<Buffer>):Promise<ReportPayload>{
 const before=await check();if(before.project_id!==run.project_id)fail('综合报告项目不一致。');
 const context=page.context(),pattern='**'+endpoint+'*';let seen=false,requestError:Error|undefined;
 const handler=async(route:Route)=>{
  const r=route.request(),u=new URL(r.url()),fields=Object.fromEntries(multipart(r.postData()??'',r.headers()['content-type']??'').map(p=>[p.name,p.value]));
  if(seen||r.method()!=='POST'||u.origin!==REAL_ORIGIN||u.pathname!==API_PATH+endpoint||fields.PROJECTID!==run.project_id||fields.RecordType!=='3'||fields.GetRecordByType!=='0'||fields.RECORDOLDID!==''||fields.ProOldId!==''){
   requestError=new SyncError('BLOCKED','综合报告请求与当前项目不符或重复，已停止。');return route.abort('blockedbyclient');
  }
  seen=true;return route.fallback();
 };
 await context.route(pattern,handler);
 try{
  const responsePromise=page.waitForResponse(r=>{const u=new URL(r.url());return u.origin===REAL_ORIGIN&&u.pathname===API_PATH+endpoint&&r.request().method()==='POST';},{timeout:120000});
  // Attach rejection immediately so failed UI navigation cannot leave an unhandled waiter.
  void responsePromise.catch(()=>{});
  await page.getByText('综合报告',{exact:true}).filter({visible:true}).click();const response=await responsePromise;
  if(requestError)throw requestError;if(!seen||response.status()!==200)fail('平台综合报告尚未就绪，稍后可单独重试。');
  const body=await response.json();if(String(body?.recordType)!=='3')fail('平台未返回本次规划综合报告。');
  const url=reportFileUrl(body.filePath,run.project_id);
  const pdf=readPdf?await readPdf(url):await(async()=>{
   const r=await context.request.get(url,{timeout:90000,maxRedirects:0});if(r.status()!==200)fail('综合报告 PDF 下载失败。');const bytes=await r.body();await r.dispose();return bytes;
  })();assertPdf(pdf);
  const generated=pdfCreationTime(pdf);
  if(Date.parse(generated)<Date.parse(before.calculation_completed_utc)-5000)fail('综合报告生成时间早于本次计算，可能仍是旧报告。');
  if(Date.parse(generated)>Date.now()+5*60*1000)fail('综合报告生成时间异常。');
  const after=await check();if(JSON.stringify(before)!==JSON.stringify(after))fail('生成报告期间计算或模型发生变化，未归档。');
  const dialog=page.locator('.el-message-box:visible').filter({hasText:'综合报告加载完成'});if(await dialog.count()===1)await dialog.getByRole('button',{name:'取消',exact:true}).click();
  return {...before,pdf,generated_utc:generated};
 }finally{await context.unroute(pattern,handler);}
}
