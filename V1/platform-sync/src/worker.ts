import {spawn} from 'node:child_process';
import {mkdirSync,existsSync,readFileSync,writeFileSync,openSync,closeSync,unlinkSync} from 'node:fs';
import {join,resolve} from 'node:path';
import {randomUUID} from 'node:crypto';
import {Store} from './store.js';import {hash} from './security.js';import {verifyHandoff} from './handoff.js';import {SyncError} from './types.js';

export function startWorker(store:Store,runId:string,config:string,cli:string){
 const run=store.run(runId);if(!['PLANNED','UPLOADING','SUBMITTING','CONVERTING','CALCULATING','VERIFYING'].includes(run.state)||run.intent&&!run.intent.acknowledged)throw new SyncError('BLOCKED','先只读核对任务状态，再继续执行。');
 verifyHandoff(run.manifest_path);return launchWorker(store,runId,config,cli,'run');
}
export function startReportWorker(store:Store,runId:string,config:string,cli:string){
 const run=store.run(runId);if(run.state!=='SUCCEEDED'||run.calculation_status!=='completed'||!run.result_id||run.rollback_of)throw new SyncError('BLOCKED','仅计算已核验成功的任务可以下载综合报告。');
 // The original apply worker also owns automatic report delivery until it exits.
 requireIdle(join(store.directory,'workers',hash(runId)+'.json'));
 return launchWorker(store,runId,config,cli,'report');
}
function alive(pid:unknown){
 if(typeof pid!=='number'||!Number.isSafeInteger(pid)||pid<=0)return false;
 try{process.kill(pid,0);return true;}catch(e){return (e as NodeJS.ErrnoException).code==='EPERM';}
}
function requireIdle(meta:string){
 if(!existsSync(meta))return;const old=JSON.parse(readFileSync(meta,'utf8'));
 // New reservations track their launcher; legacy reservations get a short grace period.
 const pending=!old.pid&&(old.launcher_pid?alive(old.launcher_pid):Date.now()-Date.parse(old.created_utc)<30000);
 if(alive(old.pid)||pending)throw new SyncError('BUSY','后台同步程序仍在运行，请刷新状态。');unlinkSync(meta);
}
function launchWorker(store:Store,runId:string,config:string,cli:string,command:'run'|'report'){
 const dir=join(store.directory,'workers');mkdirSync(dir,{recursive:true});const key=hash(command==='run'?runId:'report|'+runId),meta=join(dir,key+'.json');
 requireIdle(meta);const launch_id=randomUUID();
 writeFileSync(meta,JSON.stringify({run_id:runId,pid:null,launcher_pid:process.pid,launch_id,created_utc:new Date().toISOString()}),{flag:'wx'});
 const clearOwnReservation=()=>{if(existsSync(meta)&&JSON.parse(readFileSync(meta,'utf8')).launch_id===launch_id)unlinkSync(meta);};
 let out:number|undefined,err:number|undefined;
 try{
  out=openSync(join(dir,key+'.out.log'),'a');err=openSync(join(dir,key+'.error.log'),'a');
  const child=spawn(process.execPath,[resolve(cli),command,'--config',resolve(config),'--run',runId,...(command==='run'?['--apply']:[])],{detached:true,windowsHide:true,stdio:['ignore',out,err]});
  child.once('error',()=>{try{clearOwnReservation();}catch{/* A later retry still checks process liveness. */}});
  if(!child.pid)throw new SyncError('FAILED','后台任务未能启动。');child.unref();const result={started:true,run_id:runId,pid:child.pid,created_utc:new Date().toISOString()};writeFileSync(meta,JSON.stringify({...result,launch_id}));return result;
 }catch(e){clearOwnReservation();throw e;}finally{if(out!==undefined)closeSync(out);if(err!==undefined)closeSync(err);}
}
