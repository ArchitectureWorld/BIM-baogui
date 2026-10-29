import {fileURLToPath} from 'node:url';
import {resolve,join} from 'node:path';
import {existsSync} from 'node:fs';
import {runLocalCli} from './local-cli.js';
import {loadConfig,redact} from './security.js';
import {Store} from './store.js';
import {SyncError} from './types.js';
import {WEB_VERSION,NODE_VERSION,configureRuntime,prepareParentLaunch,authorizeWorker,type WebConfig} from './web-runtime.js';
import {setLaunchAuthorization} from './worker.js';
import {verifyHandoff} from './handoff.js';

async function main(){const input=process.argv.slice(2),command=input[0]??'help';
 if(process.versions.node!==NODE_VERSION)throw new SyncError('CONFIG','Web synchronization requires its locked Node 25.4.0 runtime.');
 if(command==='help'){process.stdout.write(JSON.stringify({version:WEB_VERSION,node_version:NODE_VERSION,commands:['setup --credential-stdin','inspect','bind','plan','status','start --apply --web-permit','resume (read/reconcile only)','report-start','report'],launch_authorization:'signed Web permit and single-use server consumption required; fixture/Local state forbidden'})+'\n');return;}
 if(!['setup','inspect','bind','plan','status','start','run','resume','report-start','report'].includes(command))throw new SyncError('BLOCKED','This operation is unavailable in the Web runtime.');
 const take=(key:string)=>{const index=input.indexOf(key);if(index<0)return undefined;if(input.filter(x=>x===key).length!==1||!input[index+1]||input[index+1]!.startsWith('--'))throw new SyncError('CONFIG','Invalid Web CLI options.');const value=input[index+1]!;input.splice(index,2);return value;};
 const permit=take('--web-permit'),proof=take('--web-worker-proof');const configIndex=input.indexOf('--config');if(configIndex<0||!input[configIndex+1])throw new SyncError('CONFIG','An isolated Web configuration is required.');
 const file=resolve(input[configIndex+1]!),config=loadConfig(file) as WebConfig;configureRuntime(config,file);
 delete process.env.NODE_OPTIONS;delete process.env.NODE_PATH;delete process.env.NODE_TLS_REJECT_UNAUTHORIZED;delete process.env.HIFC_SYNC_CHROMIUM;
 process.env.NODE_USE_ENV_PROXY='0';for(const name of ['HTTP_PROXY','HTTPS_PROXY','ALL_PROXY','http_proxy','https_proxy','all_proxy'])delete process.env[name];
 const edge=[process.env['ProgramFiles(x86)'],process.env.ProgramFiles,process.env.LOCALAPPDATA].filter((x):x is string=>!!x).map(x=>join(x,'Microsoft','Edge','Application','msedge.exe')).find(existsSync);
 if(!['status','setup','report','report-start'].includes(command)&&!edge)throw new SyncError('CONFIG','Microsoft Edge is missing; install Edge before opening the dedicated Web platform browser.');if(edge)process.env.HIFC_SYNC_CHROMIUM=edge;
 if(command==='start'||command==='run'){
  if(!permit||!input.includes('--apply'))throw new SyncError('BLOCKED','Bare apply is forbidden; confirm the current Web preview first.');
  const index=input.indexOf('--run'),id=input[index+1];if(index<0||!id)throw new SyncError('CONFIG','Missing run identity.');
  const store=new Store(config.state_dir);try{const run=store.run(id);verifyHandoff(run.manifest_path);if(command==='start'){if(proof)throw new SyncError('BLOCKED','Unexpected worker proof.');setLaunchAuthorization(prepareParentLaunch(permit,run,store.directory));}else{if(!proof)throw new SyncError('BLOCKED','Direct run/apply is forbidden.');await authorizeWorker(permit,proof,run,store.directory);store.save(run,'web-launch-consumed');}}finally{store.close();}
 }else if(permit||proof||input.includes('--apply'))throw new SyncError('BLOCKED','Unexpected write authorization on a read-only operation.');
 await runLocalCli(input,fileURLToPath(import.meta.url));
}
main().catch(e=>{const code=e instanceof SyncError?e.code:'FAILED';process.stderr.write(JSON.stringify({error:code,message:redact(e instanceof Error?e.message:'Web sync failed')})+'\n');process.exitCode=code==='CONFIG'?2:code==='LOGIN'?3:code==='BUSY'?7:4;});
