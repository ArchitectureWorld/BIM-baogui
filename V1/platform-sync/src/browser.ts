import {existsSync,mkdirSync,readFileSync} from 'node:fs';
import {isAbsolute,resolve} from 'node:path';
import {createHash} from 'node:crypto';
import {chromium,type BrowserContext,type Page} from 'playwright';
import {redact} from './security.js';
import {SyncError,type Backup,type Config,type Model,type Operation,type PlatformAdapter,type Progress,type Snapshot} from './types.js';

const realOrigin='https://hbbim.hbcic.net.cn:9000';
export interface ReadonlyEvidence {
 connected:boolean;origin:string;page_title:string;login_required:boolean;project_form_visible:boolean;
 project_name_candidates:string[];project_code_candidates:string[];model_full_names:string[];
 identity_complete:false;missing_proofs:string[];blocked_non_read_requests:number;
}
async function visibleEvidence(page:Page,blockedRequests=0):Promise<ReadonlyEvidence>{
 const found=await page.evaluate(()=>{
  const visible=(node:Element)=>{const element=node as HTMLElement;return !!(element.offsetWidth||element.offsetHeight||element.getClientRects().length)&&getComputedStyle(element).visibility!=='hidden';};
  const formCandidates=Array.from(document.querySelectorAll('[role="dialog"],.el-dialog,main')).filter(node=>visible(node)&&Array.from(node.querySelectorAll('h1,h2,.el-dialog__title')).some(title=>title.textContent?.trim()==='编辑项目'));
  const form=formCandidates.length===1?formCandidates[0]:undefined,scope=form??document.body;
  const names:string[]=[],codes:string[]=[];
  for(const item of Array.from(scope.querySelectorAll('.el-form-item'))){if(!visible(item))continue;const label=item.querySelector('label')?.textContent?.trim().replace(/[：:]$/,'');const input=item.querySelector('input,textarea') as HTMLInputElement|HTMLTextAreaElement|null;if(!input||!visible(input))continue;const value=input.value.trim();if(!value||value.length>160)continue;if(label==='项目名称')names.push(value);if(['项目编号','项目编码','项目代码'].includes(label??''))codes.push(value);}
  const text=(scope as HTMLElement).innerText??'';
  for(const match of text.matchAll(/(?:^|\n)项目名称[：:]\s*([^\n]{1,160})/g))names.push(match[1]!.trim());
  for(const match of text.matchAll(/(?:^|\n)项目(?:编号|编码|代码)[：:]\s*([^\n]{1,160})/g))codes.push(match[1]!.trim());
  const models=Array.from(scope.querySelectorAll('label.fileInfoLabel .fileNameSpan[title]')).filter(visible).map(node=>node.getAttribute('title')??'').filter(name=>name.length>0&&name.length<=512);
  const login=Array.from(document.querySelectorAll('input[type="password"],button,[role="button"]')).some(node=>visible(node)&&(node.tagName==='INPUT'||/^(登录|Login)$/i.test(node.textContent?.trim()??'')));
  return {title:document.title,login,form:!!form,names:[...new Set(names)],codes:[...new Set(codes)],models};
 });
 return {connected:true,origin:new URL(page.url()).origin,page_title:redact(found.title),login_required:found.login,project_form_visible:found.form,project_name_candidates:found.names.map(redact),project_code_candidates:found.codes.map(redact),model_full_names:found.models.map(redact),identity_complete:false,missing_proofs:['platform_project_id','active_scene_project_id','remote_model_ids','remote_model_generations'],blocked_non_read_requests:blockedRequests};
}
function allowedReal(value:string):URL {
 let url:URL;try{url=new URL(value);}catch{throw new SyncError('CONFIG','A valid allowed HTTPS platform URL is required.');}
 if(url.origin!==realOrigin||url.pathname!=='/planning-review'||url.username||url.password)throw new SyncError('CONFIG','Only the allowed HTTPS platform origin and /planning-review path are supported.');return url;
}
function fixtureUrl(value:string|undefined):URL {
 let url:URL;try{url=new URL(value??'');}catch{throw new SyncError('CONFIG','Fixture URL must use a literal loopback address.');}
 if(url.protocol!=='http:'||!['127.0.0.1','[::1]'].includes(url.hostname)||url.username||url.password||url.search||url.hash)throw new SyncError('CONFIG','Fixture URL must be an HTTP literal loopback address without credentials.');return url;
}
async function contextFor(config:Config,real:boolean):Promise<BrowserContext>{
 const profile=resolve(config.browser_profile);if(!isAbsolute(config.browser_profile)||/[\\/](?:Google[\\/]Chrome|Microsoft[\\/]Edge)[\\/]User Data(?:[\\/]|$)/i.test(profile))throw new SyncError('CONFIG','Use a dedicated absolute browser profile directory.');
 mkdirSync(profile,{recursive:true});const executable=process.env.HIFC_SYNC_CHROMIUM;
 if(executable&&!existsSync(executable))throw new SyncError('CONFIG','HIFC_SYNC_CHROMIUM executable is unavailable.');
 try{return await chromium.launchPersistentContext(profile,{headless:real?false:config.headless??false,...(executable?{executablePath:executable}:process.platform==='win32'?{channel:'msedge'}:{}),acceptDownloads:false,serviceWorkers:real?'block':'allow'});}catch{throw new SyncError('CONFIG','Dedicated Chromium profile could not start. Close another worker using this profile, or configure HIFC_SYNC_CHROMIUM.');}
}

/** Actual UI mutations are implemented only for our explicitly identified loopback fixture. */
export class FixtureBrowserAdapter implements PlatformAdapter {
 readonly mode='fixture' as const;private closed=false;
 private constructor(private readonly url:URL,private readonly context:BrowserContext,private readonly page:Page){}
 static async create(config:Config):Promise<FixtureBrowserAdapter>{
  const url=fixtureUrl(config.fixture_url),context=await contextFor(config,false);
  try{
   const probe=await context.request.get(new URL('/api/snapshot',url).href,{timeout:10000});
   if(!probe.ok()||probe.headers()['x-hifc-fixture']!=='hifc-sync-fixture/1')throw new SyncError('CONFIG','Loopback service is not the documented HIFC fixture.');
   const page=context.pages()[0]??await context.newPage();page.setDefaultTimeout(30000);await page.goto(url.href,{waitUntil:'domcontentloaded',timeout:60000});await page.getByRole('main',{name:'编辑项目'}).waitFor();
   return new FixtureBrowserAdapter(url,context,page);
  }catch(e){await context.close();if(e instanceof SyncError)throw e;throw new SyncError('CONFIG','Loopback fixture browser initialization failed.');}
 }
 private guard(){if(this.closed)throw new SyncError('FAILED','Browser adapter is closed.');if(new URL(this.page.url()).origin!==this.url.origin)throw new SyncError('BLOCKED','Fixture page navigated outside its loopback origin.');}
 private async response(path:string){this.guard();try{const r=await this.context.request.get(new URL(path,this.url).href,{timeout:30000});if(!r.ok()||r.headers()['x-hifc-fixture']!=='hifc-sync-fixture/1')throw Error();return r;}catch{throw new SyncError('BLOCKED','Fixture read could not be verified.');}}
 async snapshot():Promise<Snapshot>{const s=await(await this.response('/api/snapshot')).json() as Snapshot;if(!Array.isArray(s.models)||typeof s.project_id!=='string'||typeof s.scene_project_id!=='string')throw new SyncError('BLOCKED','Fixture project snapshot is incomplete.');return s;}
 async inspectEvidence():Promise<ReadonlyEvidence>{this.guard();return visibleEvidence(this.page);}
 async backup(model:Model):Promise<Backup>{const p=new URLSearchParams({model_id:model.id,generation:model.generation}),r=await this.response('/api/backup?'+p),h=r.headers();return {bytes:await r.body(),model_id:h['x-model-id']??'',generation:h['x-generation']??'',sha256:h['x-ifc-sha256']??''};}
 private async settled(id:string,success:string){
  await this.page.waitForFunction(({id,success})=>{const text=document.getElementById(id)?.textContent??'';return text.startsWith(success)||text==='操作结果未知';},{id,success},{timeout:30000});
  const text=await this.page.locator('#'+id).textContent();if(!text?.startsWith(success))throw new SyncError('UNKNOWN','Fixture action acknowledgment was lost; reconcile the persisted operation before continuing.');return text;
 }
 async deleteModel(model:Model,operationId:string):Promise<void>{
  this.guard();try{
   const rows=this.page.locator('label.fileInfoLabel');await this.page.waitForFunction(()=>document.querySelectorAll('label.fileInfoLabel').length>0);
   let match=-1;for(let i=0;i<await rows.count();i++){const row=rows.nth(i),name=await row.locator('.fileNameSpan').getAttribute('title');if(name===model.name){if(match!==-1)throw new SyncError('BLOCKED','Full model name is ambiguous in the fixture UI.');if(await row.getAttribute('data-model-id')!==model.id||await row.getAttribute('data-generation')!==model.generation)throw new SyncError('BLOCKED','Fixture UI model generation changed.');match=i;}}
   if(match<0)throw new SyncError('BLOCKED','Exact full model name was not found in the fixture UI.');
   await rows.nth(match).getByRole('button',{name:'删除',exact:true}).click();const dialog=this.page.getByRole('dialog',{name:'提示',exact:true});await dialog.getByText('确定要删除该模型吗?',{exact:true}).waitFor();await dialog.getByText(model.name,{exact:true}).waitFor();await dialog.getByLabel('操作编号',{exact:true}).fill(operationId);await dialog.getByRole('button',{name:'确定',exact:true}).click();await this.settled('deleteStatus','删除已确认');
  }catch(e){if(e instanceof SyncError)throw e;throw new SyncError('UNKNOWN','Fixture delete outcome is uncertain; reconcile the operation.');}
 }
 private async uploadDialog(){
  // A lost delete acknowledgment can leave its non-destructive dialog open.
  // The engine permits this path only after it has reconciled the persisted operation.
  const oldDialog=this.page.getByRole('dialog',{name:'提示',exact:true});if(await oldDialog.isVisible())await oldDialog.getByRole('button',{name:'取消',exact:true}).click();
  const dialog=this.page.getByRole('dialog',{name:'添加IFC模型',exact:true});if(!await dialog.isVisible()){await this.page.getByRole('button',{name:'添加IFC模型',exact:true}).click();await dialog.waitFor();}return dialog;
 }
 async upload(file:string,sha256:string,operationId:string):Promise<{upload_id:string}>{
  this.guard();if(createHash('sha256').update(readFileSync(file)).digest('hex')!==sha256)throw new SyncError('BLOCKED','Frozen IFC changed before the file chooser.');
  try{const dialog=await this.uploadDialog();await dialog.getByLabel('上传操作编号',{exact:true}).fill(operationId);await dialog.getByLabel('输入SHA-256',{exact:true}).fill(sha256);
   const choosing=this.page.waitForEvent('filechooser');await dialog.getByRole('button',{name:'选择文件',exact:true}).click();const chooser=await choosing;await chooser.setFiles(file);const text=await this.settled('uploadStatus','上传已确认：');const id=text.slice('上传已确认：'.length).trim();if(!id)throw new SyncError('UNKNOWN','Fixture upload acknowledgment has no identity.');return {upload_id:id};
  }catch(e){if(e instanceof SyncError)throw e;throw new SyncError('UNKNOWN','Fixture upload outcome is uncertain; reconcile the operation.');}
 }
 async submit(uploadId:string,name:string,operationId:string):Promise<{model_id:string;generation:string}>{
  this.guard();let step='open dialog';try{let dialog=await this.uploadDialog();if(!await dialog.getByRole('button',{name:'确定',exact:true}).isEnabled()){
    // Reopen the visible dialog to read its uploaded-file list; never upload again.
    step='reopen uploaded file list';await dialog.getByRole('button',{name:'取消',exact:true}).click();dialog=await this.uploadDialog();step='choose acknowledged upload';await dialog.getByLabel('已上传文件',{exact:true}).selectOption(uploadId);
   }const confirm=dialog.getByRole('button',{name:'确定',exact:true});
   step='fill model name';await dialog.getByLabel('模型完整名称',{exact:true}).fill(name);step='fill operation';await dialog.getByLabel('提交操作编号',{exact:true}).fill(operationId);step='confirm submit';await confirm.click();step='observe receipt';const text=await this.settled('submitStatus','提交已确认：');const reply=JSON.parse(text.slice('提交已确认：'.length)) as {model_id:string;generation:string};if(!reply.model_id||!reply.generation)throw Error();return reply;
  }catch(e){if(e instanceof SyncError)throw e;const error=new SyncError('UNKNOWN','Fixture submission outcome is uncertain at '+step+'; reconcile the operation.');error.cause=e;throw error;}
 }
 async progress(operationId:string):Promise<Progress>{return await(await this.response('/api/progress?'+new URLSearchParams({id:operationId}))).json() as Progress;}
 async operation(operationId:string):Promise<Operation>{return await(await this.response('/api/operation?'+new URLSearchParams({id:operationId}))).json() as Operation;}
 async close(){if(!this.closed){this.closed=true;await this.context.close();}}
}

/** Preview deliberately cannot mutate a real platform. DOM identity must be publicly proven. */
export class RealReadonlyBrowserAdapter implements PlatformAdapter {
 readonly mode='real-readonly' as const;private context?:BrowserContext;private page?:Page;private readonly entry:URL;private blockedRequests=0;
 constructor(private readonly config:Config,entryUrl:string){this.entry=allowedReal(entryUrl);}
 private denied():never{throw new SyncError('BLOCKED','Real platform is read-only in this preview; platform writes are disabled.');}
 async inspectEvidence():Promise<ReadonlyEvidence>{
  if(!this.context){this.context=await contextFor(this.config,true);await this.context.route('**/*',route=>{if(['GET','HEAD','OPTIONS'].includes(route.request().method()))return route.continue();this.blockedRequests++;return route.abort('blockedbyclient');});this.page=this.context.pages()[0]??await this.context.newPage();this.page.setDefaultTimeout(30000);try{await this.page.goto(this.entry.href,{waitUntil:'domcontentloaded',timeout:60000});await this.page.locator('body').waitFor({state:'visible'});}catch{throw new SyncError('LOGIN','Read-only platform connection could not be established. Login or HTTPS trust may require attention.');}}
  const page=this.page!;allowedReal(page.url());return visibleEvidence(page,this.blockedRequests);
 }
 async snapshot():Promise<Snapshot>{
  const evidence=await this.inspectEvidence();
  if(evidence.login_required){const error=new SyncError('LOGIN','The dedicated platform browser needs login.');Object.assign(error,{evidence});throw error;}
  // No frontend-private Vue state, undocumented APIs, guessed IDs, config-as-evidence or
  // name-only matching is used. The present platform has not exposed a proven visible
  // project + active-scene identity and model-generation contract to this adapter.
  const error=new SyncError('BLOCKED','Read-only page opened. Project, active-scene and model-generation identity cannot yet be proven from the documented visible UI; live binding is blocked pending adapter acceptance.');Object.assign(error,{evidence});throw error;
 }
 async backup(_model:Model):Promise<Backup>{throw new SyncError('BLOCKED','A real previous IFC download and matching current generation have not been verified.');}
 async deleteModel(_model:Model,_operationId:string):Promise<void>{this.denied();}
 async upload(_file:string,_sha256:string,_operationId:string):Promise<{upload_id:string}>{this.denied();}
 async submit(_uploadId:string,_name:string,_operationId:string):Promise<{model_id:string;generation:string}>{this.denied();}
 async progress(_operationId:string):Promise<Progress>{throw new SyncError('BLOCKED','Current-run result correlation is not accepted for the real platform.');}
 async operation(_operationId:string):Promise<Operation>{throw new SyncError('BLOCKED','Real operation reconciliation is unavailable in this read-only preview.');}
 async close(){if(this.context){const context=this.context;this.context=undefined;this.page=undefined;await context.close();}}
}
export async function createAdapter(config:Config,entryUrl?:string):Promise<PlatformAdapter>{
 if(config.adapter==='fixture')return FixtureBrowserAdapter.create(config);
 if(config.adapter==='real-readonly')return new RealReadonlyBrowserAdapter(config,entryUrl??'');
 throw new SyncError('CONFIG','Unsupported browser adapter.');
}
