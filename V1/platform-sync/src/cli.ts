import {existsSync,readFileSync,writeFileSync,mkdirSync,statSync} from 'node:fs';
import {resolve,join,dirname} from 'node:path';
import {Store} from './store.js';import {SyncEngine} from './engine.js';
import {initializeConfig,loadConfig,entryUrl,protectSecret,unprotectSecret,redact} from './security.js';
import {SyncError,type PlatformAdapter,type Snapshot,type Run} from './types.js';

const exitCodes={CONFIG:2,LOGIN:3,BLOCKED:4,UNKNOWN:5,FAILED:6,BUSY:7};
function args(input:string[]){const command=input.shift()??'help';const options:Record<string,string|boolean>={};const boolean=new Set(['apply','credential-stdin']);const keys=new Set(['config','binding','model','handoff','run',...boolean]);for(let i=0;i<input.length;i++){const flag=input[i]!;if(!flag.startsWith('--')||!keys.has(flag.slice(2)))throw new SyncError('CONFIG','Unknown CLI option.');const key=flag.slice(2);if(options[key]!==undefined)throw new SyncError('CONFIG','Duplicate CLI option.');if(boolean.has(key))options[key]=true;else{const value=input[++i];if(!value||value.startsWith('--')||/[?&]token=/i.test(value))throw new SyncError('CONFIG','Missing option value or credential passed on command line.');options[key]=value;}}return {command,options};}
function sanitize(value:unknown):unknown {if(typeof value==='string')return redact(value);if(Array.isArray(value))return value.map(sanitize);if(value&&typeof value==='object')return Object.fromEntries(Object.entries(value).filter(([k])=>!['entry_url','token','password','cookie','authorization'].includes(k.toLowerCase())).map(([k,v])=>[k,sanitize(v)]));return value;}
function output(value:unknown){process.stdout.write(JSON.stringify(sanitize(value))+'\n');}
function resultCode(r:Run){return r.state==='UNKNOWN_RESULT'?5:r.state==='NEEDS_LOGIN'?3:['BLOCKED','PAUSED'].includes(r.state)?4:r.state==='FAILED'?6:0;}
async function stdin(){let text='';for await(const part of process.stdin){text+=part.toString();if(text.length>16000)throw new SyncError('CONFIG','Credential input is too long.');}return text.trim();}
async function main(){const {command,options:o}=args(process.argv.slice(2));
 if(command==='help'){output({version:'0.1.0-preview',commands:['setup','inspect','bind','plan','run --apply (fixture only)','status','resume (readonly)','rollback --apply (fixture only)'],real_platform:'read-only'});return;}
 const file=typeof o.config==='string'?resolve(o.config):undefined;if(!file)throw new SyncError('CONFIG','--config is required.');
 if(command==='watch')throw new SyncError('BLOCKED','watch/batch/unattended operation is deferred in this preview.');
 if(!['setup','inspect','bind','plan','run','status','resume','rollback'].includes(command))throw new SyncError('CONFIG','Unknown command.');
 if(command==='setup'){
  if(!existsSync(file))initializeConfig(file);else if(!o['credential-stdin'])throw new SyncError('CONFIG','Configuration already exists.');
  if(o['credential-stdin']){const c=loadConfig(file);if(c.adapter!=='real-readonly'||!c.entry_secret_file)throw new SyncError('CONFIG','Credential storage is only configured for real readonly adapter.');const secret=entryUrl(await stdin());mkdirSync(dirname(c.entry_secret_file),{recursive:true});writeFileSync(c.entry_secret_file,protectSecret(secret),{mode:0o600});}
  output({configured:true,credential_stored:!!o['credential-stdin'],real_platform:'read-only',config:file});return;
 }
 const c=loadConfig(file);let store:Store|undefined,adapter:PlatformAdapter|undefined;
 try{
  store=new Store(c.state_dir);
  if(command==='status'){output({bindings:store.bindings(),runs:store.runs()});return;}
  if(['run','rollback'].includes(command)&&!o.apply)throw new SyncError('BLOCKED','Explicit --apply is required for fixture writes.');
  if(['run','rollback'].includes(command)&&c.adapter!=='fixture')throw new SyncError('BLOCKED','Real platform writes are disabled in this preview.');
  let entry:string|undefined;
  if(c.adapter==='real-readonly'){if(!c.entry_secret_file||!existsSync(c.entry_secret_file))throw new SyncError('LOGIN','Configure the platform credential with Configure-Platform.ps1 first.');entry=entryUrl(unprotectSecret(readFileSync(c.entry_secret_file,'utf8')));}
  const adapterModule='./browser.js';adapter=await (await import(adapterModule)).createAdapter(c,entry) as PlatformAdapter;
  const engine=new SyncEngine(store,adapter);
  const required=(key:string)=>{const value=o[key];if(typeof value!=='string')throw new SyncError('CONFIG','Missing --'+key+'.');return value;};
  const inspection=join(c.state_dir,'last-inspection.json');
  if(command==='inspect'){const s=await adapter.snapshot();if(!s.authenticated)throw new SyncError('LOGIN','Dedicated browser needs login.');writeFileSync(inspection,JSON.stringify(s,null,2));output(s);return;}
  if(command==='bind'){
   let expected=c.expected_project;
   if(!expected){if(!existsSync(inspection)||Date.now()-statSync(inspection).mtimeMs>5*60*1000)throw new SyncError('BLOCKED','Inspect project/model list before binding; inspection expires after five minutes.');const s=JSON.parse(readFileSync(inspection,'utf8')) as Snapshot;expected={project_id:s.project_id,project_code:s.project_code,project_name:s.project_name};}
   output(await engine.bind(required('model'),resolve(required('handoff')),expected));return;
  }
  if(command==='plan'){const r=await engine.plan(required('binding'),resolve(required('handoff')));output(r);process.exitCode=resultCode(r);return;}
  if(command==='run'||command==='resume'||command==='rollback'){const id=required('run'),r=command==='run'?await engine.run(id):command==='resume'?await engine.resume(id):await engine.rollback(id);output(r);process.exitCode=resultCode(r);return;}
 }finally{await adapter?.close();store?.close();}
}
main().catch(e=>{const code=e instanceof SyncError?e.code:'FAILED';const evidence=e instanceof Error?(e as Error & {evidence?:unknown}).evidence:undefined;process.stderr.write(JSON.stringify({error:code,message:redact(e instanceof Error?e.message:'Unexpected sync error'),...(evidence?{evidence:sanitize(evidence)}:{})})+'\n');process.exitCode=exitCodes[code];});
