import {readFileSync,writeFileSync,mkdirSync,existsSync} from 'node:fs';
import {join} from 'node:path';
import type {BrowserContext,Page,Request,Route,Response} from 'playwright';
import {contextFor} from './browser.js';
import {hash} from './security.js';
import {assertIfc} from './handoff.js';
import {API_PATH,REAL_ORIGIN,modelFileUrl,parseModels,parseProjectIdentity,type RealModel} from './real-contract.js';
import {RealEvidence} from './real-evidence.js';
import {calculationInput,calculationState} from './real-calculation.js';
import {closeProjectWindows,downloadRealReport,verifyReportCalculation} from './real-report.js';
import {SyncError,type Config,type Model,type Snapshot,type Backup,type Operation,type Progress,type Run,type PlatformAdapter} from './types.js';
import {requireRuntimeConfig,requireWriteAuthorization,validateRun} from './web-runtime.js';

const pause=(ms:number)=>new Promise<void>(r=>setTimeout(r,ms));
const navigationReads=new Set(['/UserLayout/SetSubSys','/ShareManagement/GetSchemeShare','/HomePageShow/SetCurProject','/HomePageShow/GetUserView','/HomePageShow/GetCeShowConfig','/HomePageShow/GetLayerTreeInfo','/HomePageShow/GetLayerConfigNew','/UserLayout/GetUserSplitThreeVer','/UserLayout/GetOtherEntTree','/HomePageShow/GetSZLayerTree','/UserOperate/GetCitySunArguments','/FileManage/CheckFile_TY','/Project/GetIFCPositionToWorld','/Project/getFileStream']);
const reads=new Set(['/HomePageShow/GetCurProject','/Project/GetProjectData','/ProjectManagement/QueryProject','/ProjectManagement/QueryProjectSign','/Project/GetIfcInfos','/Project/GetIfcPropRecord','/Verification/GetCadRecordInfo']);
const fail=(message:string):never=>{throw new SyncError('BLOCKED',message);};
export function requestFields(r:Request):Record<string,unknown>{
 const fields:Record<string,unknown>=Object.fromEntries(new URL(r.url()).searchParams);const body=r.postData();if(!body)return fields;
 const type=r.headers()['content-type']??'';
 if(type.includes('application/json')){try{const x=JSON.parse(body);if(x&&typeof x==='object'&&!Array.isArray(x))Object.assign(fields,x);}catch{}}
 else if(type.includes('application/x-www-form-urlencoded'))Object.assign(fields,Object.fromEntries(new URLSearchParams(body)));
 else if(type.includes('multipart/form-data'))for(const m of body.matchAll(/Content-Disposition: form-data; name="([^"]+)"\r\n\r\n([^\r\n]*)\r\n/g)){if(m[2]!.length<50000)fields[m[1]!]=m[2]!;}
 return fields;
}
/** Uses normal UI for mutations and observed, same-origin read contracts. */
export class RealBrowserAdapter implements PlatformAdapter {
 readonly mode:'real-readonly'|'real';private page?:Page;private initialized=false;private templates=new Map<string,{url:string;headers:Record<string,string>}>();
 private evidence:RealEvidence;private run?:Run;private models:RealModel[]=[];private closing=false;private focus?:string;
 private active?:{kind:'delete'|'upload'|'submit';id:string};private pending=Promise.resolve();private eventError?:Error;
 private reportProject?:string;
 private requests=new WeakMap<Request,{kind:string;id:string;fields:Record<string,unknown>;calc?:ReturnType<typeof calculationInput>}>();
 private geometryModels=new Set<string>();private lastCalc:'not_started'|'running'|'completed'|'failed'='not_started';
 private order=0;private requestOrder=new WeakMap<Request,number>();
 private calculationGates=new Map<string,{accepted:number;running?:number}>();
 private context?:BrowserContext;
 constructor(private config:Config,private entry:string){
  this.config=structuredClone(requireRuntimeConfig(config));
  const u=new URL(entry);if(u.origin!==REAL_ORIGIN||u.pathname!=='/planning-review'||u.username||u.password)fail('Only the verified HTTPS platform entry is supported.');
  this.mode=this.config.adapter==='real'?'real':'real-readonly';this.evidence=new RealEvidence(this.config.state_dir);
 }
 selectModel(id:string){this.focus=id;}
 browserOpen(){return !!this.page&&!this.page.isClosed();}
 prepare(run:Run){
  validateRun(run);
  this.run=structuredClone(run);this.focus=run.new_model_id??run.baseline.id;this.config={...this.config,expected_project:run.expected_project};
  const id=run.id+':submit',e=this.evidence.get(id);
  if(run.intent?.kind==='submit'&&run.intent.operation_id===id&&e?.kind==='submit'&&e.status==='completed'&&e.project_id===run.expected_project.project_id&&e.upload_id===run.upload_id&&e.name===run.handoff.original_name&&e.sha256===run.handoff.ifc_sha256&&e.model_id&&e.generation&&e.model_id!==run.baseline.id&&!run.non_target_models?.some(m=>m.id===e.model_id)){
   // This selects bytes to download; it never substitutes a stored input hash for a remote read.
   this.focus=run.new_model_id??e.model_id;
   if(e.calculation_observation_version===2&&e.calculation_started_utc&&!this.calculationGates.has(id))this.calculationGates.set(id,{accepted:++this.order,...(e.calculation_running_utc?{running:++this.order}:{})});
  }
 }
 private stage(stage:string){writeFileSync(join(this.config.state_dir,'last-connection.json'),JSON.stringify({stage,utc:new Date().toISOString()}));}
 private async connect(){
  if(this.initialized)return;this.stage('启动独立浏览器');this.context??=await contextFor(this.config,true);this.page=this.context.pages()[0]??await this.context.newPage();this.page.setDefaultTimeout(20000);
  this.page.on('request',r=>{this.requestOrder.set(r,++this.order);const u=new URL(r.url()),p=u.pathname.slice(API_PATH.length);if(u.origin===REAL_ORIGIN&&u.pathname.startsWith(API_PATH)&&reads.has(p)&&r.method()==='GET'){
   const h=Object.fromEntries(Object.entries(r.headers()).filter(([k])=>!['host','cookie','content-length','connection','origin','referer'].includes(k)&&!k.startsWith('sec-')));this.templates.set(p,{url:r.url(),headers:h});
  }});
  this.page.on('response',r=>{const received=++this.order,u=new URL(r.url());if(u.origin===REAL_ORIGIN&&u.pathname.startsWith(API_PATH))this.trace('response',u.pathname,r.status());this.pending=this.pending.then(()=>this.response(r,received)).catch(e=>{
   if(this.requests.has(r.request()))this.eventError=e instanceof Error?e:new Error('平台响应无法核对。');
   else this.trace('read-error',u.pathname,r.status()); // A failed read must not poison later read-only reconciliation.
  });});
  await this.context.route('**/*',route=>this.route(route));
  try{this.stage('打开平台入口');await this.page.goto(this.entry,{waitUntil:'domcontentloaded',timeout:60000});this.initialized=true;}catch{throw new SyncError('LOGIN','独立浏览器未能打开平台，请检查登录和网络。');}
 }
 private traces:{kind:string;path:string;status?:number}[]=[];
 private trace(kind:string,path:string,status?:number){this.traces.push({kind,path,status});this.traces=this.traces.slice(-100);writeFileSync(join(this.config.state_dir,'connection-events.json'),JSON.stringify(this.traces));}
 private async route(route:Route){
  const r=route.request(),u=new URL(r.url()),path=u.pathname.slice(API_PATH.length),f=requestFields(r),active=this.active,run=this.run;
  if(['GET','HEAD','OPTIONS'].includes(r.method()))return route.fallback();
  if(u.origin===REAL_ORIGIN&&u.pathname.startsWith(API_PATH)&&navigationReads.has(path))return route.fallback();
  if(this.reportProject&&r.method()==='POST'&&u.origin===REAL_ORIGIN&&u.pathname===API_PATH+'/StatisticsReport/GetHandleInfo_ZHReport'&&f.PROJECTID===this.reportProject&&String(f.RecordType)==='3')return route.fallback();
  if(this.mode==='real'&&run&&active&&u.origin===REAL_ORIGIN){
   try{
    requireWriteAuthorization(run);
    if(path==='/Project/DelIfcModel'&&active.kind==='delete'&&f.ID===run.baseline.id&&f.PROJECTID===run.expected_project.project_id&&Number(f.MODELTYPE)===2){if(this.evidence.get(active.id)?.request_seen)fail('Repeated delete request was blocked.');this.evidence.seen(active.id);this.requests.set(r,{kind:'delete',id:active.id,fields:f});return route.fallback();}
    if(['/FileManage/uploadFileChunk','/FileManage/mergeChunk'].includes(path)){
     const name=f.filename??f.resumableFilename??f.flowFilename;
     const expected=active.kind==='upload'?run.handoff.original_name:active.kind==='submit'?run.handoff.original_name.replace(/\.ifc$/i,'.frag'):undefined;
     if(expected&&name===expected){this.requests.set(r,{kind:active.kind==='upload'?'upload':'frag',id:active.id,fields:f});return route.fallback();}
    }
    if(path==='/Project/AddIfcModel'&&active.kind==='submit'){
     const e=this.evidence.get(active.id)!,upload=this.evidence.uploadedFile(e.upload_id!);if(e.request_seen||f.ID!==''||f.NAME!==run.handoff.original_name||f.PROJECTID!==run.expected_project.project_id||Number(f.MODELTYPE)!==2||f.MODELPATH!==upload.source_path)fail('Unmatched or repeated model submission was blocked.');this.evidence.seen(active.id);this.requests.set(r,{kind:'submit',id:active.id,fields:f});return route.fallback();
    }
    if(path==='/Project/ChangeModelExtInfo'&&active.kind==='submit'){
     await this.pending;const e=this.evidence.get(active.id);const ids=Object.entries(f).filter(([k])=>k.endsWith('.THREEENTID'));if(e?.model_id&&ids.length===1&&ids[0]![1]===e.model_id&&f['EntExtInfoAry[0].PROJECTID']===run.expected_project.project_id&&Number(f['EntExtInfoAry[0].LIGHTSTATE'])===1)return route.fallback();
    }
    if(path==='/ProjectManagement/BindProject_IFCThree'&&active.kind==='submit'){
     await this.pending;const e=this.evidence.get(active.id);if(!e?.model_id||e.calculation_started_utc||!this.geometryModels.has(e.model_id))fail('Calculation cannot yet be tied to the new model.');
     const calc=calculationInput(r.postData()??'',r.headers()['content-type']??'',run.expected_project.project_id,this.models.map(m=>m.id),e!.model_id!);this.requests.set(r,{kind:'calculation',id:active.id,fields:f,calc});return route.fallback();
    }
   }catch(e){this.eventError=e as Error;}
  }
  this.trace('blocked',u.pathname);if(active&&!this.eventError)this.eventError=new SyncError('UNKNOWN','平台产生了尚未验证的写请求，已阻止：'+u.pathname);return route.abort('blockedbyclient');
 }
 private async response(response:Response,receivedOrder:number){
  const request=response.request(),u=new URL(response.url()),path=u.pathname.slice(API_PATH.length);if(u.origin!==REAL_ORIGIN||!u.pathname.startsWith(API_PATH))return;
  const linked=this.requests.get(request);const active=this.active;if(!linked&&!(active?.kind==='submit'&&['/Project/GetIFCPositionToWorld','/Verification/GetCadRecordInfo'].includes(path))&&!(active?.kind==='upload'&&path==='/FileManage/uploadFileChunk'))return;
  if(response.status()!==200)throw new SyncError('UNKNOWN','平台操作响应丢失或失败，需只读核对。');const body=await response.json();
  if(linked?.kind==='delete')this.evidence.deleted(linked.id,linked.fields,body);
  else if(linked?.kind==='upload'||active?.kind==='upload'&&path==='/FileManage/uploadFileChunk'){
   const file=body?.Result?.RESULT&&body.Result.RESULT!=='wait'?body.Result.NEWFILE:body?.RESULT&&body.NEEDMERGE===false?body.FILEGROUP:undefined;
   if(file){const bytes=await this.download(file.PATH);this.evidence.uploaded(linked?.id??active!.id,file,hash(bytes));}
  }else if(linked?.kind==='submit'){
   const project=this.run!.expected_project.project_id,models=await this.list(project),match=models.find(x=>x.id===body?.ID);if(!match)throw new SyncError('UNKNOWN','平台返回的新模型尚不能与清单核对。');this.evidence.submitted(linked.id,linked.fields,body,match.generation);this.focus=match.id;this.models=models;
  }else if(path==='/Project/GetIFCPositionToWorld'&&active?.kind==='submit'){
   const params=new URL(request.url()).searchParams;if(params.get('proId')!==this.run?.expected_project.project_id||body?.ifcInfoState!=='1'||!Array.isArray(body.PyIfcData))return;
   const requested=JSON.parse(request.postData()??'null');if(!Array.isArray(requested))return;for(const item of body.PyIfcData)if(typeof item.threeEntId==='string'&&requested.some(x=>x.threeEntId===item.threeEntId))this.geometryModels.add(item.threeEntId);
  }else if(linked?.kind==='calculation'){
   if(body!=='1')throw new SyncError('UNKNOWN','平台未受理本次计算。');this.evidence.calculationStarted(linked.id,this.run!.expected_project.project_id,linked.calc!.included_model_ids);this.calculationGates.set(linked.id,{accepted:receivedOrder});this.lastCalc='not_started';
  }else if(path==='/Verification/GetCadRecordInfo'&&active?.kind==='submit')this.observeCalculation(active.id,requestFields(request),body,this.requestOrder.get(request),receivedOrder);
 }
 private observeCalculation(id:string,fields:Record<string,unknown>,body:unknown,requestedOrder:number|undefined,receivedOrder:number){
  if(fields.ProjectId!==this.run?.expected_project.project_id||String(fields.subType)!=='5')return;
  const e=this.evidence.get(id),gate=this.calculationGates.get(id);if(!e?.calculation_started_utc||!gate||requestedOrder===undefined||requestedOrder<=gate.accepted)return;const state=calculationState(body);
  if(state==='running'){if(gate.running===undefined){this.evidence.calculationRunning(id);gate.running=receivedOrder;}this.lastCalc='running';}
  else if(state==='failed')this.lastCalc='failed';
  else if(state==='completed'&&e.calculation_running_utc&&gate.running!==undefined&&requestedOrder>gate.running){this.evidence.calculationSaved(id,e.project_id,e.calculation_models!,hash(JSON.stringify(body)));this.lastCalc='completed';}
 }
 private guard(){if(!this.page||new URL(this.page.url()).origin!==REAL_ORIGIN||new URL(this.page.url()).pathname!=='/planning-review')throw new SyncError('LOGIN','独立浏览器已离开报规平台，需重新登录。');}
 private async waitFor(check:()=>Promise<boolean>,message:string,timeout=90000){const end=Date.now()+timeout;while(Date.now()<end){if(this.eventError)throw this.eventError;if(await check())return;await pause(250);}throw new SyncError('BLOCKED',message);}
 private async api(path:string,params:Record<string,string>={}):Promise<unknown>{
  this.guard();if(!reads.has(path))fail('Read endpoint is outside the verified contract.');
  // This exact read contract was verified during live calculation acceptance.
  // A fresh page need not open the calculation panel merely to read its record.
  if(path==='/Verification/GetCadRecordInfo'&&!this.templates.has(path)&&this.templates.has('/Project/GetIfcInfos')){
   const t=this.templates.get('/Project/GetIfcInfos')!,u=new URL(t.url);u.pathname=API_PATH+path;u.search='';this.templates.set(path,{url:u.href,headers:t.headers});
  }
  await this.waitFor(async()=>this.templates.has(path),'页面尚未发出所需的项目读取请求：'+path,30000);
  const template=this.templates.get(path)!,u=new URL(template.url);for(const[k,v]of Object.entries(params))u.searchParams.set(k,v);
  const result=await this.page!.evaluate(async({url,headers})=>{try{const r=await fetch(url,{headers,credentials:'same-origin',signal:AbortSignal.timeout(30000)});return {status:r.status,body:await r.json()};}catch{return {status:0,body:null};}},{url:u.href,headers:template.headers});
  if(result.status!==200)throw new SyncError('LOGIN','平台读取失败，需要核对登录或网络。');return result.body;
 }
 private async editor(){
  await this.connect();this.stage('核对项目编辑窗口');const p=this.page!,form=p.locator('#addProject:visible');let expected=this.config.expected_project;
  if(!await form.isVisible()){
   if(!expected)fail('请先在平台设置中选择并核对具体项目；不会自动选择同名项目。');
   const card=p.locator('div.projectManagement_wh-ZJXM.ZJXM').filter({has:p.locator('span[title]').filter({hasText:expected!.project_name})});
   const menu=p.getByText('项目管理',{exact:true}).filter({visible:true});
   await this.waitFor(async()=>await card.isVisible()||await p.getByRole('row').filter({hasText:expected!.project_name}).count()===1||await menu.count()===1,'未找到已绑定项目的唯一可见入口。');
   if(!await card.isVisible()){const row=p.getByRole('row').filter({hasText:expected!.project_name});if(await row.count()===1)await row.locator('.mxsh').click();if(!await card.isVisible()){await menu.waitFor({state:'visible',timeout:90000});await menu.click();}}
   await card.waitFor({state:'visible',timeout:90000});if(await card.locator('span[title]').filter({hasText:expected!.project_name}).getAttribute('title')!==expected!.project_name)fail('Visible project name is not an exact match.');
   await card.locator('i.el-icon-edit[title="修改"]').click();await form.waitFor({state:'visible'});
  }
  await this.waitFor(async()=>!!(await form.getByPlaceholder('请输入项目名称',{exact:true}).inputValue())&&!!(await form.getByPlaceholder('请输入联审平台立项项目代码',{exact:true}).inputValue()),'项目表单尚未读完名称和编号。',30000);
  const name=await form.getByPlaceholder('请输入项目名称',{exact:true}).inputValue(),code=await form.getByPlaceholder('请输入联审平台立项项目代码',{exact:true}).inputValue();
  if(expected&&(name!==expected!.project_name||code!==expected.project_code))fail('编辑窗口与绑定的项目名称/编号不同。');
  const tab=form.getByRole('tab',{name:'BIM模型',exact:true});if(await tab.getAttribute('aria-selected')!=='true')await tab.click();
  const scene=await this.api('/HomePageShow/GetCurProject');if(typeof scene!=='string'||!scene)fail('Active scene identity is missing.');
  const identity=parseProjectIdentity(await this.api('/ProjectManagement/QueryProjectSign',{ProjectId:scene as string}),scene as string,{name,code});
  if(expected&&identity.project_id!==expected.project_id)fail('Active scene differs from the bound project.');
  return identity;
 }
 private async list(project:string){return parseModels(await this.api('/Project/GetIfcInfos',{proId:project,modelType:'2'}),project);}
 private async download(path:string){
  const url=modelFileUrl(path);this.guard();const result=await this.page!.evaluate(async url=>{
   try{const r=await fetch(url,{credentials:'same-origin',signal:AbortSignal.timeout(90000)});if(!r.ok)return {status:r.status,data:''};const bytes=new Uint8Array(await r.arrayBuffer());let text='';for(let i=0;i<bytes.length;i+=32768)text+=String.fromCharCode(...bytes.subarray(i,i+32768));return {status:r.status,data:btoa(text)};}catch{return {status:0,data:''};}
  },url);if(result.status!==200)fail('真实 IFC 文件下载失败；禁止删除旧模型。');const bytes=Buffer.from(result.data,'base64');assertIfc(bytes);return bytes;
 }
 async snapshot():Promise<Snapshot>{
  const identity=await this.editor(),before=await this.list(identity.project_id);const models:Model[]=[];
  for(const m of before){let sha256:string|undefined;if(m.id===this.focus){this.stage('下载并核验所选模型 IFC');sha256=hash(await this.download(m.source_path));}models.push({id:m.id,name:m.name,generation:m.generation,...(sha256?{sha256}:{})});}
  const after=await this.list(identity.project_id);if(JSON.stringify(before.map(m=>[m.id,m.generation]).sort())!==JSON.stringify(after.map(m=>[m.id,m.generation]).sort()))fail('模型清单在文件校验期间发生变化。');this.models=after;
  return {authenticated:true,...identity,scene_project_id:identity.project_id,revision:hash(JSON.stringify(models)),models};
 }
 async backup(model:Model):Promise<Backup>{
  const identity=await this.editor(),before=(await this.list(identity.project_id)).filter(x=>x.id===model.id);if(before.length!==1||before[0]!.generation!==model.generation||before[0]!.name!==model.name)fail('Remote model changed before backup.');
  const bytes=await this.download(before[0]!.source_path),after=(await this.list(identity.project_id)).find(x=>x.id===model.id);if(after?.generation!==model.generation||hash(bytes)!==model.sha256)fail('Remote IFC no longer matches the selected generation/hash.');
  return {bytes,model_id:model.id,generation:model.generation,sha256:hash(bytes)};
 }
 private writable(){if(this.mode!=='real')fail('Real platform is read-only; writes are disabled.');if(!this.run)fail('A saved, verified run is required before writes.');requireWriteAuthorization(this.run!);}
 private async editable(){this.writable();const identity=await this.editor();if(!identity.writable)fail('项目审核中或已办结，不能替换模型。');return identity;}
 async deleteModel(m:Model,id:string):Promise<void>{
  const project=await this.editable();if(m.id!==this.run!.baseline.id||m.generation!==this.run!.baseline.generation)fail('Delete target does not match saved plan.');
  const list=await this.list(project.project_id);if(list.filter(x=>x.name===m.name).length!==1||!list.some(x=>x.id===m.id&&x.generation===m.generation))fail('Target changed before UI deletion.');
  // The platform truncates visible filename text; its tooltip retains the full name.
  const row=this.page!.locator('#addProject:visible label.fileInfoLabel').filter({has:this.page!.getByTitle(m.name,{exact:true})});if(await row.count()!==1||await row.locator('.fileNameSpan').getAttribute('title')!==m.name)fail('Exact full model name is not unique in the UI.');
  this.evidence.begin(id,'delete',{project_id:project.project_id,model_id:m.id,generation:m.generation});this.active={kind:'delete',id};
  await row.locator('img[title="删除"]').click();const dialog=this.page!.locator('.el-message-box:visible');await dialog.getByText('确定要删除该模型吗?',{exact:true}).waitFor();await dialog.getByRole('button',{name:'确定',exact:true}).click();
  await this.waitFor(async()=>this.evidence.get(id)?.status==='completed','删除结果未知；不能重发删除。',30000);this.active=undefined;
 }
 private uploadWindow(){
  // Project management and edit windows also contain this nested dialog. Start
  // at its visible heading and select only the nearest dialog, never ancestors.
  return this.page!.locator('.headhead:visible').filter({hasText:'添加IFC模型'}).locator('xpath=ancestor::*[contains(concat(" ",normalize-space(@class)," ")," dialogBox ")][1]');
 }
 private async uploadDialog(){const dialog=this.uploadWindow();if(!await dialog.isVisible()){await this.page!.locator('#addProject:visible').getByRole('button',{name:'选择文件',exact:true}).click();await dialog.waitFor({state:'visible'});}return dialog;}
 async upload(file:string,sha:string,id:string):Promise<{upload_id:string}>{
  const project=await this.editable(),bytes=readFileSync(file);if(hash(bytes)!==sha||sha!==this.run!.handoff.ifc_sha256)fail('Frozen IFC changed before upload.');const name=this.run!.handoff.original_name;
  if(/[\\/:\x00-\x1f]/.test(name)||!name.toLowerCase().endsWith('.ifc'))fail('Invalid delivery filename.');const dir=join(this.config.state_dir,'browser-staging',hash(id));mkdirSync(dir,{recursive:true});const path=join(dir,name);if(!existsSync(path))writeFileSync(path,bytes,{flag:'wx'});if(hash(readFileSync(path))!==sha)fail('Staged upload bytes changed.');
  this.evidence.begin(id,'upload',{project_id:project.project_id,sha256:sha,name});this.active={kind:'upload',id};const dialog=await this.uploadDialog();
  const choosing=this.page!.waitForEvent('filechooser');await dialog.getByRole('button',{name:'选择文件',exact:true}).click();await(await choosing).setFiles(path);
  await this.waitFor(async()=>this.evidence.get(id)?.status==='completed','上传结果未知；保留现场并核对，不能重复选择文件。',15*60*1000);this.active=undefined;return {upload_id:this.evidence.get(id)!.upload_id!};
 }
 async submit(upload:string,name:string,id:string):Promise<{model_id:string;generation:string}>{
  const project=await this.editable(),u=this.evidence.uploadedFile(upload);if(u.project_id!==project.project_id||u.name!==name||name!==this.run!.handoff.original_name||u.sha256!==this.run!.handoff.ifc_sha256)fail('Submit does not match confirmed upload.');
  const dialog=this.uploadWindow();if(!await dialog.isVisible())fail('上传窗口已关闭，不能安全重放提交；请只读核对。');
  this.evidence.begin(id,'submit',{project_id:project.project_id,upload_id:upload,name,sha256:u.sha256});this.active={kind:'submit',id};
  await dialog.locator('.dialogBoxFooter').getByRole('button',{name:'确定',exact:true}).click();await this.waitFor(async()=>this.evidence.get(id)?.status==='completed','模型提交结果未知；禁止重复添加。',60000);const e=this.evidence.get(id)!;return {model_id:e.model_id!,generation:e.generation!};
 }
 async progress(id:string):Promise<Progress>{
  await this.pending;if(this.eventError)throw this.eventError;const e=this.evidence.get(id);if(!e?.model_id||!e.generation||!e.sha256)throw new SyncError('UNKNOWN','本次提交证据缺失。');
  const models=await this.list(e.project_id),m=models.find(x=>x.id===e.model_id);if(!m||m.generation!==e.generation)throw new SyncError('UNKNOWN','本次模型已变化。');
  if(e.calculation_started_utc&&this.templates.has('/Verification/GetCadRecordInfo')){const requested=++this.order,body=await this.api('/Verification/GetCadRecordInfo',{ProjectId:e.project_id,subType:'5'});this.observeCalculation(id,{ProjectId:e.project_id,subType:'5'},body,requested,++this.order);}
  const overlay=await this.page!.locator('.el-loading-mask:visible').count()>0;const proof=this.evidence.calculationProof(id,overlay);
  return {operation_id:id,model_id:e.model_id,generation:e.generation,input_sha256:e.sha256,conversion:m.conversion_state===1?'completed':m.conversion_state===2?'failed':'pending',calculation:proof?'completed':this.lastCalc,regulation:'unknown',result_id:proof,current_calculation_proven:!!proof,active_overlay:overlay};
 }
 async operation(id:string):Promise<Operation>{return this.evidence.operation(id);}
 async report(run:Run){
  if(run.state!=='SUCCEEDED'||run.calculation_status!=='completed')fail('本次计算尚未核验完成。');
  this.active=undefined;await this.pending;
  this.prepare(run);const initial=await this.snapshot();await closeProjectWindows(this.page!);
  const check=async()=>{
   const scene=await this.api('/HomePageShow/GetCurProject'),models=await this.list(run.project_id),body=await this.api('/Verification/GetCadRecordInfo',{ProjectId:run.project_id,subType:'5'});
   return verifyReportCalculation(run,this.evidence.get(run.id+':submit'),body,{...initial,scene_project_id:typeof scene==='string'?scene:'',models});
  };
  // Permit only this verified report-generation POST; all model writes still
  // use the existing operation guard. The report helper independently validates fields.
  this.reportProject=run.project_id;
  try{return await downloadRealReport(this.page!,run,check);}finally{this.reportProject=undefined;}
 }
 async close(){if(this.closing)return;this.closing=true;await this.context?.close();await this.pending;this.evidence.close();}
}
