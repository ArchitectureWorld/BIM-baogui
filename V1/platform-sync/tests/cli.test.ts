import test from 'node:test';import assert from 'node:assert/strict';
import {mkdtempSync,readFileSync,writeFileSync,mkdirSync} from 'node:fs';import {tmpdir} from 'node:os';import {join,resolve} from 'node:path';import {spawnSync} from 'node:child_process';import {fileURLToPath} from 'node:url';
import {loadConfig,protectSecret,unprotectSecret} from '../src/security.js';
import {Store} from '../src/store.js';import type {Run} from '../src/types.js';
const cli=fileURLToPath(new URL('../src/cli.js',import.meta.url));
function invoke(args:string[],cwd?:string){const r=spawnSync(process.execPath,[cli,...args],{encoding:'utf8',cwd,windowsHide:true,timeout:15000});return {code:r.status,out:r.stdout,err:r.stderr};}
test('setup creates credential-free config and status works without browser/login',()=>{const root=mkdtempSync(join(tmpdir(),'sync-cli-')),config=join(root,'config.json');const r=invoke(['setup','--config',config]);assert.equal(r.code,0,r.err);assert.doesNotMatch(readFileSync(config,'utf8'),/[?&]token=/);const c=loadConfig(config);assert.equal(c.state_dir,resolve(root,'.runtime/state'));const s=invoke(['status','--config',config],tmpdir());assert.equal(s.code,0,s.err);assert.deepEqual(JSON.parse(s.out),{bindings:[],runs:[]});});
test('watch deferred, no apply flag and bad command fail before browser launch',()=>{const root=mkdtempSync(join(tmpdir(),'sync-cli-')),config=join(root,'config.json');invoke(['setup','--config',config]);assert.equal(invoke(['watch','--config',config]).code,4);assert.equal(invoke(['run','--config',config,'--run','missing']).code,4);assert.equal(invoke(['mystery','--config',config]).code,2);});
test('configuration refuses token, external fixture and unsafe headless real',()=>{const root=mkdtempSync(join(tmpdir(),'sync-cli-')),p=join(root,'c.json');const c={schema:'hifc.platform-sync-config/1',adapter:'real-readonly',state_dir:'state',browser_profile:'profile'};for(const extra of [{entry_url:'https://x?token=SECRET'},{adapter:'fixture',fixture_url:'http://example.com'},{headless:true}]){writeFileSync(p,JSON.stringify({...c,...extra}));assert.throws(()=>loadConfig(p));}});
test('DPAPI secrets are account-protected and never stored in plaintext',()=>{if(process.platform!=='win32')return;const plain='https://hbbim.hbcic.net.cn:9000/planning-review?token=TEST_CREDENTIAL#/index';const protectedValue=protectSecret(plain);assert.ok(!protectedValue.includes('TEST_CREDENTIAL'));assert.equal(unprotectSecret(protectedValue),plain);});

test('explicit real mode is accepted but readonly start and missing plan are blocked before browser',()=>{
 const root=mkdtempSync(join(tmpdir(),'sync-start-')),p=join(root,'config.json');invoke(['setup','--config',p]);
 assert.equal(invoke(['start','--config',p,'--run','missing','--apply']).code,4);
 const c=JSON.parse(readFileSync(p,'utf8'));c.adapter='real';writeFileSync(p,JSON.stringify(c));assert.equal(loadConfig(p).adapter,'real');
 assert.equal(invoke(['start','--config',p,'--run','missing']).code,4);assert.equal(invoke(['start','--config',p,'--run','missing','--apply']).code,2);
});

test('report commands are discoverable and validate successful run before browser',()=>{
 const help=invoke(['help']);assert.ok(JSON.parse(help.out).commands.some((c:string)=>c.startsWith('report-start')));
 const root=mkdtempSync(join(tmpdir(),'sync-report-cli-')),p=join(root,'config.json');invoke(['setup','--config',p]);
 assert.equal(invoke(['report','--config',p,'--run','missing']).code,2);
 assert.equal(invoke(['report-start','--config',p,'--run','missing']).code,2);
});

test('report login failure is persisted separately from a successful model sync',()=>{
 const root=mkdtempSync(join(tmpdir(),'sync-report-login-')),p=join(root,'config.json');invoke(['setup','--config',p]);const c=loadConfig(p),store=new Store(c.state_dir);
 store.save({id:'finished',project_id:'p',state:'SUCCEEDED',calculation_status:'completed',result_id:'r',handoff:{document_path:join(root,'school.rvt')}} as Run);store.close();
 assert.equal(invoke(['report','--config',p,'--run','finished']).code,3);
 const check=new Store(c.state_dir);try{assert.equal(check.run('finished').state,'SUCCEEDED');assert.equal(check.run('finished').report?.state,'NEEDS_LOGIN');}finally{check.close();}
});
