import {startFixture} from './fixture.js';
import {spawn} from 'node:child_process';
import {mkdirSync,writeFileSync,existsSync} from 'node:fs';
import {resolve,join,dirname} from 'node:path';
import {fileURLToPath} from 'node:url';
import type {Config} from './types.js';

const values=process.argv.slice(2);const flag=(key:string)=>{const i=values.indexOf(key);return i<0?undefined:values[i+1];};
const preview=flag('--preview'),evidence=flag('--evidence');
if(!preview||!evidence||!existsSync(preview))throw Error('Pass absolute --preview executable and --evidence directory.');
const root=resolve(evidence);mkdirSync(root,{recursive:true});
const server=await startFixture();
try{
 const config:Config={schema:'hifc.platform-sync-config/1',adapter:'fixture',state_dir:join(root,'state'),browser_profile:join(root,'browser'),fixture_url:server.url,headless:true};
 const configPath=join(root,'config.local.json');writeFileSync(configPath,JSON.stringify(config,null,2));
 const worker=join(dirname(fileURLToPath(import.meta.url)),'cli.js');
 const args=['--node',process.execPath,'--worker',worker,'--config',configPath,'--evidence',root,...(values.includes('--smoke')?['--smoke']:[])];
 process.stdout.write(JSON.stringify({mode:'local-simulated-platform',fixture_url:server.url,config:configPath,evidence:root})+'\n');
 const result=await new Promise<number>((resolve,reject)=>{const child=spawn(preview,args,{windowsHide:values.includes('--smoke'),stdio:'inherit'});child.once('error',reject);child.once('exit',code=>resolve(code??1));});
 process.exitCode=result;
 writeFileSync(join(root,'demo-result.json'),JSON.stringify({exit_code:result,remote_platform_contacted:false,fixture_actions:server.counters()},null,2));
}finally{await server.close();}
