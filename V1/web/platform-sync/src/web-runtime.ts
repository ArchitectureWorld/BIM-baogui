import {createHash,createPublicKey,verify,randomBytes} from 'node:crypto';
import {existsSync,lstatSync,readFileSync,writeFileSync,mkdirSync} from 'node:fs';
import {join,resolve,dirname} from 'node:path';
import {SyncError,type Run,type Config} from './types.js';
import {getCACertificates,setDefaultCACertificates} from 'node:tls';

export const WEB_VERSION='0.2.0-web.1',NODE_VERSION='25.4.0',WEB_SERVER='https://192.168.6.45:8443/';
export type WebIdentity={schemaVersion:1;minimumReaderVersion:1;accountId:string;componentVersion:string;nodeVersion:string;mainReleaseId:string;mainSourceCommit:string;serverClockOffsetSeconds?:number};
export type WebConfig=Config&{web:WebIdentity};
export type Permit={exportPlanId:string;documentSessionId:string;projectId:string;targetModelId:string;targetGeneration:string;ifcSha256:string;runId:string;mainReleaseId:string;mainSourceCommit:string;componentVersion:string;nodeVersion:string;confirmedReplacement:boolean;id:string;accountId:string;exportPlanHash:string;receiptIdentityHash:string;issuedAtUnixSeconds:number;expiresAtUnixSeconds:number;signature:string};
export type Consumption={permitId:string;permitHash:string;accountId:string;runId:string;consumerNonce:string;consumedAtUnixSeconds:number;signature:string};
type Key={modulus:string;exponent:string};
const fail=(message:string):never=>{throw new SyncError('BLOCKED',message);};
export const sha=(s:string|Buffer)=>createHash('sha256').update(s).digest('hex');
export function normalize<T>(value:unknown):T {if(Array.isArray(value))return value.map(x=>normalize(x)) as T;if(value&&typeof value==='object')return Object.fromEntries(Object.entries(value).map(([k,v])=>[k[0]!.toLowerCase()+k.slice(1),normalize(v)])) as T;return value as T;}
export function readJson<T>(file:string):T {noLinks(file);if(!existsSync(file)||lstatSync(file).size>65536)fail('Web configuration or authorization file is missing or too large.');return normalize<T>(JSON.parse(readFileSync(file,'utf8').replace(/^\uFEFF/,'')));}
export function noLinks(file:string){for(let current=resolve(file);;current=dirname(current)){if(lstatSync(current,{throwIfNoEntry:false})?.isSymbolicLink())fail('Web state and authorization paths cannot use links.');if(dirname(current)===current)break;}}
export function accountRoot(id:string){if(!/^[a-f0-9-]{32,36}$/i.test(id))fail('Invalid Web account identity.');const local=process.env.LOCALAPPDATA;if(!local)fail('Windows LOCALAPPDATA is required.');return resolve(local!,'HIFC','0.5.0-Web','PlatformSync',sha(id));}
export function validateConfig(config:WebConfig,file:string){
 const w=config.web;if(!w||w.schemaVersion!==1||w.minimumReaderVersion!==1||w.componentVersion!==WEB_VERSION||w.nodeVersion!==NODE_VERSION||!/^0\.5\.0-dev-\d{8}T\d{6}Z$/.test(w.mainReleaseId)||!/^[a-f0-9]{40}$/.test(w.mainSourceCommit))fail('Unsupported Web runtime/schema version.');
 const root=accountRoot(w.accountId);noLinks(root);noLinks(file);
 if(resolve(file)!==join(root,'config.json')||resolve(config.state_dir)!==join(root,'state')||resolve(config.browser_profile)!==join(root,'browser')||resolve(config.entry_secret_file??'')!==join(root,'entry.dpapi')||!['real','real-readonly'].includes(config.adapter)||config.headless)fail('Use the isolated Web account configuration; Local/fixture paths are forbidden.');
 for(const path of [config.state_dir,config.browser_profile,config.entry_secret_file!])noLinks(path);
}
let identity:WebIdentity|undefined;let consumed:{permit:Permit;receipt:Consumption}|undefined;
const consumerNonce=randomBytes(32).toString('hex');
export function configureRuntime(config:WebConfig,file:string){validateConfig(config,file);identity=Object.freeze({...config.web});
 const marker=join(config.state_dir,'web-schema.json');noLinks(marker);
 if(existsSync(marker)){const version=readJson<{schemaVersion:number;minimumReaderVersion:number;accountId:string}>(marker);if(version.schemaVersion!==1||version.minimumReaderVersion!==1||version.accountId!==identity.accountId)fail('Web task store requires another reader/schema version.');}
 else {if(existsSync(join(config.state_dir,'sync.sqlite')))fail('Existing unversioned SQLite state will not be migrated automatically.');mkdirSync(config.state_dir,{recursive:true});writeFileSync(marker,JSON.stringify({schemaVersion:1,minimumReaderVersion:1,accountId:identity.accountId}),{flag:'wx'});}
}
export function runtimeIdentity(){if(!identity)fail('Web CLI context was not initialized.');return identity!;}
export function requireRuntimeConfig(config:Config):WebConfig{
 const current=runtimeIdentity(),web=config as WebConfig;validateConfig(web,join(accountRoot(current.accountId),'config.json'));
 if(web.web.accountId!==current.accountId||web.web.componentVersion!==current.componentVersion||web.web.nodeVersion!==current.nodeVersion||web.web.mainReleaseId!==current.mainReleaseId||web.web.mainSourceCommit!==current.mainSourceCommit)fail('Browser runtime does not match the initialized Web account/components.');
 return web;
}
export function stampRun(run:Run){const current=runtimeIdentity();if(!run.web)run.web={...current};validateRun(run);}
export function validateRun(run:Run){const current=runtimeIdentity(),w=run.web;if(!w||w.schemaVersion!==1||w.minimumReaderVersion!==1||w.accountId!==current.accountId)fail('Legacy or incompatible tasks cannot enter the Web worker.');}
export function requestPayload(x:Permit){return [x.exportPlanId,x.documentSessionId,x.projectId,x.targetModelId,x.targetGeneration,x.ifcSha256,x.runId,x.mainReleaseId,x.mainSourceCommit,x.componentVersion,x.nodeVersion,x.confirmedReplacement?'1':'0'].join('\n');}
export function permitPayload(x:Permit){return ['HIFC-WEB-PLATFORM-LAUNCH-1',requestPayload(x),x.id,x.accountId,x.exportPlanHash,x.receiptIdentityHash,String(x.issuedAtUnixSeconds),String(x.expiresAtUnixSeconds)].join('\n');}
export function consumptionPayload(x:Consumption){return ['HIFC-WEB-PLATFORM-CONSUME-1',x.permitId,x.permitHash,x.accountId,x.runId,x.consumerNonce,String(x.consumedAtUnixSeconds)].join('\n');}
export function verifySignature(payload:string,signature:string,key:Key){try{const jwk={kty:'RSA',n:Buffer.from(key.modulus,'base64').toString('base64url'),e:Buffer.from(key.exponent,'base64').toString('base64url')};if(Buffer.from(key.modulus,'base64').length<256||!verify('RSA-SHA256',Buffer.from(payload),createPublicKey({key:jwk,format:'jwk'}),Buffer.from(signature,'base64')))fail('Web authorization signature is invalid.');}catch{fail('Web authorization signature is invalid.');}}
function installationKey(){return readJson<Key>(join(process.env.LOCALAPPDATA!,'HIFC','0.5.0-Web','Revit2020','update-public-key.json'));}
export function receiptIdentity(r:{planId:string;documentSessionId:string;ifcHash:string;snapshotBefore:string;snapshotAfter:string;sourceRestored:boolean;exported:boolean}){return sha(['HIFC-WEB-EXPORT-RECEIPT-2',r.planId,r.documentSessionId,r.ifcHash,r.snapshotBefore,r.snapshotAfter,r.sourceRestored?'1':'0',r.exported?'1':'0'].join('\n'));}
export function validatePermit(permit:Permit,run:Run,key:Key,now=Math.floor(Date.now()/1000)){
 validateRun(run);const current=runtimeIdentity();const h=run.handoff.web;
 if(!permit||permit.confirmedReplacement!==true||!Number.isSafeInteger(permit.issuedAtUnixSeconds)||!Number.isSafeInteger(permit.expiresAtUnixSeconds)||!/^[a-f0-9-]{32,36}$/i.test(permit.id)||!/^[a-f0-9]{64}$/.test(permit.exportPlanHash)||!/^[a-f0-9]{64}$/.test(permit.receiptIdentityHash))fail('Web launch permit field types or identity are invalid.');
 now+=Number.isSafeInteger(current.serverClockOffsetSeconds)?current.serverClockOffsetSeconds!:0;
 if(!permit||!h||!permit.confirmedReplacement||permit.accountId!==current.accountId||permit.runId!==run.id||permit.exportPlanId!==run.handoff.export_id||permit.documentSessionId!==h.document_session_id||permit.projectId!==run.project_id||run.expected_project.project_id!==run.project_id||permit.targetModelId!==run.baseline.id||permit.targetGeneration!==run.baseline.generation||permit.ifcSha256!==run.handoff.ifc_sha256||permit.receiptIdentityHash!==h.receipt_identity_hash||permit.mainReleaseId!==current.mainReleaseId||permit.mainSourceCommit!==current.mainSourceCommit||permit.componentVersion!==WEB_VERSION||permit.nodeVersion!==NODE_VERSION||run.web?.mainReleaseId!==current.mainReleaseId||run.web?.mainSourceCommit!==current.mainSourceCommit||run.web?.componentVersion!==current.componentVersion||run.web?.nodeVersion!==current.nodeVersion||permit.issuedAtUnixSeconds>now+120||permit.expiresAtUnixSeconds<=now||permit.expiresAtUnixSeconds<=permit.issuedAtUnixSeconds||permit.expiresAtUnixSeconds-permit.issuedAtUnixSeconds>900)fail('Web launch permit is expired or does not match this frozen run.');
 verifySignature(permitPayload(permit),permit.signature,key);
}
export function prepareParentLaunch(permitFile:string,run:Run,state:string){const permit=readJson<Permit>(permitFile);validatePermit(permit,run,installationKey());
 const proof=randomBytes(32).toString('hex'),directory=join(state,'authorizations');noLinks(directory);mkdirSync(directory,{recursive:true});const claim=join(directory,permit.id+'.claim.json');if(!/^[a-f0-9-]{32,36}$/i.test(permit.id))fail('Invalid launch permit id.');
 writeFileSync(claim,JSON.stringify({runId:run.id,proofHash:sha(proof),createdAt:new Date().toISOString()}),{flag:'wx'});return {permitFile:resolve(permitFile),proof};}
export async function authorizeWorker(permitFile:string,proof:string,run:Run,state:string){
 const permit=readJson<Permit>(permitFile);const key=installationKey();validatePermit(permit,run,key);
 const claimPath=join(state,'authorizations',permit.id+'.claim.json');const claim=readJson<{runId:string;proofHash:string}>(claimPath);if(!/^[a-f0-9]{64}$/.test(proof)||claim.runId!==run.id||claim.proofHash!==sha(proof))fail('Apply must be launched by the confirmed Web parent.');
 writeFileSync(join(state,'authorizations',permit.id+'.worker.json'),JSON.stringify({pid:process.pid,runId:run.id}),{flag:'wx'});
 setDefaultCACertificates([...getCACertificates('default'),...getCACertificates('system')]);
 const response=await fetch(new URL('api/v1/platform/consume',WEB_SERVER),{method:'POST',redirect:'error',headers:{'content-type':'application/json'},body:JSON.stringify({permit,consumerNonce}),signal:AbortSignal.timeout(15000)});
 if(!response.ok)fail('Server refused launch consumption. Refresh status; do not replay apply.');if(!response.body)fail('Missing launch consumption response.');const reader=response.body!.getReader();const chunks:Buffer[]=[];let bytes=0;try{while(true){const part=await reader.read();if(part.done)break;bytes+=part.value.length;if(bytes>65536){await reader.cancel();fail('Invalid launch consumption response.');}chunks.push(Buffer.from(part.value));}}finally{reader.releaseLock();}const receipt=normalize<Consumption>(JSON.parse(Buffer.concat(chunks).toString('utf8')));
 verifySignature(consumptionPayload(receipt),receipt.signature,key);
 if(receipt.permitId!==permit.id||receipt.permitHash!==sha(permitPayload(permit))||receipt.accountId!==permit.accountId||receipt.runId!==run.id||receipt.consumerNonce!==consumerNonce||receipt.consumedAtUnixSeconds<permit.issuedAtUnixSeconds||receipt.consumedAtUnixSeconds>=permit.expiresAtUnixSeconds)fail('Launch consumption does not belong to this worker instance.');
 consumed={permit,receipt};run.web={...run.web!,authorizationId:permit.id,consumedAtUnixSeconds:receipt.consumedAtUnixSeconds};
}
export function requireWriteAuthorization(run:Run){validateRun(run);const h=run.handoff.web,w=run.web,p=consumed?.permit;if(!consumed||!p||!h||p.runId!==run.id||p.ifcSha256!==run.handoff.ifc_sha256||p.projectId!==run.project_id||p.projectId!==run.expected_project.project_id||p.targetModelId!==run.baseline.id||p.targetGeneration!==run.baseline.generation||p.exportPlanId!==run.handoff.export_id||p.documentSessionId!==h.document_session_id||p.receiptIdentityHash!==h.receipt_identity_hash||p.mainReleaseId!==w?.mainReleaseId||p.mainSourceCommit!==w?.mainSourceCommit||p.componentVersion!==w?.componentVersion||p.nodeVersion!==w?.nodeVersion||consumed.receipt.consumerNonce!==consumerNonce)fail('Bare apply, changed targets and replayed worker authorizations are forbidden.');}
