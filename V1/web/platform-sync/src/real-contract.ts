import {hash} from './security.js';
import {SyncError,type Model,type Progress} from './types.js';
export interface RealModel extends Model { source_path:string; conversion_state:number }
export const REAL_ORIGIN='https://hbbim.hbcic.net.cn:9000';
export const API_PATH='/planning-review/api';
const fail=(message:string):never=>{throw new SyncError('BLOCKED',message);};
function object(value:unknown):Record<string,unknown>{if(!value||typeof value!=='object'||Array.isArray(value))return fail('Platform response must be an object.');return value as Record<string,unknown>;}
function required(o:Record<string,unknown>,key:string):string{const v=o[key];if(typeof v!=='string'||!v.trim()||v!==v.trim()||/[\x00-\x1f]/.test(v))return fail('Platform identity field missing/invalid: '+key);return v;}
export function parseProjectIdentity(value:unknown,scene:string,form:{name:string;code:string}){
 const r=object(value),id=required(r,'ID'),name=required(r,'NAME'),code=required(r,'PLATFORMPROCODE');
 if(scene!==id||form.name!==name||form.code!==code)fail('Platform project, visible form and active scene disagree.');
 if(!Number.isInteger(r.APPROVALSTATE)||![0,1,2,3,4].includes(r.APPROVALSTATE as number))fail('Platform editability is unverified.');
 const state=r.APPROVALSTATE as number;
 return {project_id:id,project_name:name,project_code:code,approval_state:state,writable:state===0||state===2};
}
export function modelFileUrl(path:string):string{
 if(typeof path!=='string'||!path.startsWith('Upload/Leave/')||!path.toLowerCase().endsWith('.ifc')||/[\\?#:\x00-\x1f]/.test(path))return fail('IFC path is outside the verified platform file store.');
 const parts=path.split('/');
 for(const part of parts){let decoded:string;try{decoded=decodeURIComponent(part);}catch{return fail('Invalid encoded IFC path.');}if(!part||['.','..'].includes(decoded)||/[\\/\x00-\x1f]/.test(decoded))fail('Invalid IFC path segment.');}
 return REAL_ORIGIN+API_PATH+'/'+parts.map(encodeURIComponent).join('/');
}
export function parseModels(value:unknown,project:string):RealModel[]{
 if(!Array.isArray(value))return fail('Platform model list is not an array.');
 const seen=new Set<string>();
 return value.map(v=>{
  const r=object(v),id=required(r,'ID'),name=required(r,'NAME'),path=required(r,'MODELPATH'),date=required(r,'ADDTIME');
  if(seen.has(id)||required(r,'PROJECTID')!==project||r.MODELTYPE!==2||!name.toLowerCase().endsWith('.ifc')||!Number.isFinite(Date.parse(date))||!Number.isInteger(r.THREEVER))fail('Platform model identity is duplicated, incomplete or outside the project.');
  seen.add(id);modelFileUrl(path);
  // This is a fingerprint of observed immutable/model-position metadata, not a
  // claimed server version. File bytes are independently downloaded and hashed.
  const identity=['ID','PROJECTID','NAME','MODELPATH','MODELTYPE','ADDTIME','THREEVER','LYAREID','MOVEINFO','SHOWINFO','BIMPROPERTY','GLBSHOWINFO','LANDNO','TODWGID','TODWGNAME','TOENTCODE','TONUMBER'].map(k=>[k,r[k]??null]);
  if(![0,1,2].includes(r.LIGHTSTATE as number))fail('Unknown IFC conversion status.');
  return {id,name,generation:'record-sha256:'+hash(JSON.stringify(identity)),source_path:path,conversion_state:r.LIGHTSTATE as number};
 });
}
export function parseCalculationState(value:unknown,project:string):Progress['calculation']{
 if(!Array.isArray(value))return fail('Calculation project list is invalid.');
 const matches=value.map(object).filter(r=>r.ProjectID===project);if(matches.length!==1)fail('Calculation project identity is missing or ambiguous.');
 const state=matches[0]!.IFCCalcState;
 if(state===2)return 'running';if(state===1)return 'completed';if(state===-1)return 'not_started';
 return fail('Unknown calculation status; historical text cannot establish success.');
}
