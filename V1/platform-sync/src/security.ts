import {createHash} from 'node:crypto';
import {spawnSync} from 'node:child_process';
import {readFileSync,writeFileSync,mkdirSync,existsSync,realpathSync} from 'node:fs';
import {dirname,resolve,isAbsolute,relative,sep} from 'node:path';
import {SyncError,type Config} from './types.js';

export const hash=(bytes:Buffer|string)=>createHash('sha256').update(bytes).digest('hex');
export function redact(value:unknown):string {
 return String(value).replace(/https?:\/\/[^\s<>"']+/gi,u=>{try{const x=new URL(u);return x.origin+x.pathname+(x.search?'?[REDACTED]':'');}catch{return '[URL REDACTED]';}})
  .replace(/\b(authorization|cookie|set-cookie)\s*[:=][^\r\n]*/gi,'$1: [REDACTED]')
  .replace(/\b(token|password|secret|access_token|refresh_token)\s*[=:]\s*[^\s&,;]+/gi,'$1=[REDACTED]');
}
export function entryUrl(value:string):string {
 const url=new URL(value.trim());
 if(url.origin!=='https://hbbim.hbcic.net.cn:9000'||url.pathname!=='/planning-review'||url.username||url.password)
  throw new SyncError('CONFIG','Only the approved HBBIM planning-review origin is allowed.');
 if(!url.searchParams.get('token'))throw new SyncError('CONFIG','Platform entry URL is missing its credential.');
 return url.href;
}
// Credentials travel through stdin, never argv or diagnostics. DPAPI binds them to this Windows account.
export function protectSecret(value:string):string { return dpapi('protect',value); }
export function unprotectSecret(value:string):string { return dpapi('unprotect',value); }
function dpapi(action:'protect'|'unprotect',value:string):string {
 if(process.platform!=='win32')throw new SyncError('CONFIG','Credential storage requires Windows DPAPI.');
 const script=action==='protect'
  ? '$ErrorActionPreference="Stop"; Add-Type -AssemblyName System.Security; $s=[Console]::In.ReadToEnd(); [Convert]::ToBase64String([Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::UTF8.GetBytes($s),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser))'
  : '$ErrorActionPreference="Stop"; Add-Type -AssemblyName System.Security; $s=[Console]::In.ReadToEnd(); [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); [Text.Encoding]::UTF8.GetString([Security.Cryptography.ProtectedData]::Unprotect([Convert]::FromBase64String($s),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser))';
 const result=spawnSync('powershell.exe',['-NoLogo','-NoProfile','-NonInteractive','-Command',script],{input:value,encoding:'utf8',windowsHide:true,timeout:15000});
 if(result.status!==0)throw new SyncError('CONFIG','Windows credential protection failed.');
 return result.stdout.trim();
}
export function loadConfig(file:string):Config {
 let c:Config;try{c=JSON.parse(readFileSync(file,'utf8').replace(/^\uFEFF/,''));}catch{throw new SyncError('CONFIG','Cannot read sync configuration.');}
 if(c.schema!=='hifc.platform-sync-config/1'||!['fixture','real-readonly'].includes(c.adapter)||!c.state_dir||!c.browser_profile)throw new SyncError('CONFIG','Unsupported sync configuration.');
 // Never accept an inline credential, even if the rest of the config is valid.
 const raw=JSON.stringify(c);if(/(?:[?&]token=|"(?:token|password|entry_url)"\s*:)/i.test(raw))throw new SyncError('CONFIG','Plaintext credentials are forbidden in configuration.');
 const base=dirname(resolve(file)); c={...c,state_dir:resolve(base,c.state_dir),browser_profile:resolve(base,c.browser_profile),entry_secret_file:c.entry_secret_file?resolve(base,c.entry_secret_file):undefined};
 if(c.adapter==='fixture'){
  let u:URL;try{u=new URL(c.fixture_url!);}catch{throw new SyncError('CONFIG','Invalid fixture URL.');}
  if(u.protocol!=='http:'||u.hostname!=='127.0.0.1'||u.username||u.password||u.search)throw new SyncError('CONFIG','Fixture must use a loopback HTTP URL.');
 }else if(c.headless)throw new SyncError('CONFIG','Real-platform preview requires a visible dedicated browser.');
 return c;
}
export function initializeConfig(file:string):void {
 if(existsSync(file))throw new SyncError('CONFIG','Configuration already exists; it will not be overwritten.');
 mkdirSync(dirname(resolve(file)),{recursive:true});
 const c:Config={schema:'hifc.platform-sync-config/1',adapter:'real-readonly',state_dir:'.runtime/state',browser_profile:'.runtime/browser',entry_secret_file:'.runtime/entry.dpapi',headless:false};
 writeFileSync(file,JSON.stringify(c,null,2),{flag:'wx'});
}
export function containedFile(root:string,name:string):string {
 if(!name||isAbsolute(name)||name.includes('..')||name.includes('\\')||name.includes('/')||name.includes(':'))throw new SyncError('BLOCKED','Invalid relative artifact path.');
 const canonical=realpathSync(root),path=resolve(root,name),actual=realpathSync(path),rel=relative(canonical,actual);
 if(rel.startsWith('..'+sep)||isAbsolute(rel)||actual!==path && process.platform!=='win32')throw new SyncError('BLOCKED','Artifact path escapes spool.');
 return path;
}
