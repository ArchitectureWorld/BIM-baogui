import {existsSync,mkdirSync,readFileSync} from 'node:fs';
import {isAbsolute,resolve} from 'node:path';
import {createHash} from 'node:crypto';
import {chromium,type BrowserContext,type Page} from 'playwright';
import {redact} from './security.js';
import {SyncError,type Backup,type Config,type Model,type Operation,type PlatformAdapter,type Progress,type Snapshot} from './types.js';
import {requireRuntimeConfig} from './web-runtime.js';

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
function fixtureUrl(value:string|undefined):URL {
 let url:URL;try{url=new URL(value??'');}catch{throw new SyncError('CONFIG','Fixture URL must use a literal loopback address.');}
 if(url.protocol!=='http:'||!['127.0.0.1','[::1]'].includes(url.hostname)||url.username||url.password||url.search||url.hash)throw new SyncError('CONFIG','Fixture URL must be an HTTP literal loopback address without credentials.');return url;
}
export async function contextFor(config:Config,real:boolean):Promise<BrowserContext>{
 requireRuntimeConfig(config);
 const profile=resolve(config.browser_profile);if(!isAbsolute(config.browser_profile)||/[\\/](?:Google[\\/]Chrome|Microsoft[\\/]Edge)[\\/]User Data(?:[\\/]|$)/i.test(profile))throw new SyncError('CONFIG','Use a dedicated absolute browser profile directory.');
 mkdirSync(profile,{recursive:true});const executable=process.env.HIFC_SYNC_CHROMIUM;
 if(executable&&!existsSync(executable))throw new SyncError('CONFIG','HIFC_SYNC_CHROMIUM executable is unavailable.');
 try{return await chromium.launchPersistentContext(profile,{headless:real?false:config.headless??false,...(executable?{executablePath:executable}:process.platform==='win32'?{channel:'msedge'}:{}),acceptDownloads:false,serviceWorkers:real?'block':'allow'});}catch{throw new SyncError('CONFIG','Dedicated Chromium profile could not start. Close another worker using this profile, or configure HIFC_SYNC_CHROMIUM.');}
}

export async function createAdapter(config:Config,entryUrl?:string):Promise<PlatformAdapter>{
 requireRuntimeConfig(config);
 if(config.adapter==='fixture')throw new SyncError('BLOCKED','Fixture adapters are not available in the production Web runtime.');
 if(config.adapter==='real-readonly'||config.adapter==='real'){const {RealBrowserAdapter}=await import('./real-browser.js');return new RealBrowserAdapter(config,entryUrl??'');}
 throw new SyncError('CONFIG','Unsupported browser adapter.');
}
