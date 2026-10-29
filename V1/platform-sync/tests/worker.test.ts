import test from 'node:test';import assert from 'node:assert/strict';
import {mkdtempSync,mkdirSync,writeFileSync,readFileSync,existsSync} from 'node:fs';import {tmpdir} from 'node:os';import {join} from 'node:path';
import childProcess from 'node:child_process';import {syncBuiltinESMExports} from 'node:module';
import {Store} from '../src/store.js';import {hash} from '../src/security.js';import {startReportWorker} from '../src/worker.js';import {SyncError,type Run} from '../src/types.js';
function setup(){
 const root=mkdtempSync(join(tmpdir(),'report-worker-')),store=new Store(join(root,'state')),dir=join(store.directory,'workers');mkdirSync(dir);
 const run={id:'finished',project_id:'p',state:'SUCCEEDED',calculation_status:'completed',result_id:'result'} as Run;store.save(run);
 const cli=join(root,'noop.cjs');writeFileSync(cli,"process.stdout.write('REPORT_WORKER_OK');");
 const report=join(dir,hash('report|finished')+'.json'),model=join(dir,hash('finished')+'.json');
 return {store,dir,report,model,start:()=>startReportWorker(store,run.id,join(root,'config.json'),cli)};
}
const busy=(e:unknown)=>e instanceof SyncError&&e.code==='BUSY';
test('manual report retry cannot launch while model worker is automatically downloading',()=>{const f=setup();try{
 writeFileSync(f.model,JSON.stringify({run_id:'finished',pid:process.pid,created_utc:new Date().toISOString()}));
 assert.throws(f.start,busy);assert.equal(existsSync(f.report),false);
}finally{f.store.close();}});
test('abandoned legacy reservation without PID can be recovered',async()=>{const f=setup();try{
 writeFileSync(f.report,JSON.stringify({run_id:'finished',pid:null,created_utc:'2020-01-01T00:00:00Z'}));
 const launched=f.start();assert.equal(launched.started,true);assert.ok(launched.pid>0);
 const log=join(f.dir,hash('report|finished')+'.out.log');for(let i=0;i<50&&!readFileSync(log,'utf8').includes('REPORT_WORKER_OK');i++)await new Promise(r=>setTimeout(r,20));
 assert.match(readFileSync(log,'utf8'),/REPORT_WORKER_OK/);
}finally{f.store.close();}});
test('launch failure removes its reservation so retry is available',t=>{const f=setup();try{
 t.mock.method(childProcess,'spawn',()=>{throw Error('process launch unavailable');});syncBuiltinESMExports();
 assert.throws(f.start,/process launch unavailable/);assert.equal(existsSync(f.report),false,'failed startup left a permanent reservation');
}finally{t.mock.restoreAll();syncBuiltinESMExports();f.store.close();}});
test('live report worker or live launcher keeps duplicate retries blocked',()=>{for(const lease of [{pid:process.pid},{pid:null,launcher_pid:process.pid}]){const f=setup();try{
 writeFileSync(f.report,JSON.stringify({...lease,created_utc:new Date().toISOString()}));assert.throws(f.start,busy);
}finally{f.store.close();}}});
