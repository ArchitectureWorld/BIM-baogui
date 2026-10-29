import {DatabaseSync} from 'node:sqlite';
import {mkdirSync} from 'node:fs';
import {join} from 'node:path';
import {modelFileUrl} from './real-contract.js';
import {SyncError,type Operation} from './types.js';

export interface Evidence extends Operation {
 project_id:string;name?:string;source_path?:string;created_utc:string;
 request_seen?:boolean;calculation_started_utc?:string;calculation_models?:string[];
 calculation_saved_utc?:string;calculation_result_hash?:string;
 calculation_running_utc?:string;calculation_input_hash?:string;
 calculation_observation_version?:2;
}
type Input=Pick<Evidence,'project_id'>&Partial<Pick<Evidence,'sha256'|'name'|'model_id'|'generation'|'upload_id'>>;
const fail=(message:string):never=>{throw new SyncError('UNKNOWN',message);};
/** Local evidence is deliberately separate from a server operation id. No raw
 * request headers, credentials, IFC payload or personal project fields are saved. */
export class RealEvidence {
 private db:DatabaseSync;
 constructor(public readonly directory:string){mkdirSync(directory,{recursive:true});this.db=new DatabaseSync(join(directory,'real-evidence.sqlite'));this.db.exec('PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA busy_timeout=5000; CREATE TABLE IF NOT EXISTS evidence(id TEXT PRIMARY KEY,data TEXT NOT NULL)');}
 private save(e:Evidence){this.db.prepare('INSERT INTO evidence VALUES(?,?) ON CONFLICT(id) DO UPDATE SET data=excluded.data').run(e.id,JSON.stringify(e));}
 begin(id:string,kind:Operation['kind'],input:Input){if(this.get(id))fail('This real action already has an intent; reconcile without replay.');if(!input.project_id)fail('Project identity missing.');this.save({id,kind,status:'pending',...input,created_utc:new Date().toISOString()});}
 get(id:string):Evidence|undefined{const r=this.db.prepare('SELECT data FROM evidence WHERE id=?').get(id);return r?JSON.parse(String(r.data)):undefined;}
 private require(id:string,kind:Operation['kind']){const e=this.get(id);if(!e||e.kind!==kind)fail('Action intent is missing or mismatched.');return e!;}
 seen(id:string){const e=this.get(id);if(!e)fail('Action intent is missing.');e!.request_seen=true;this.save(e!);}
 deleted(id:string,request:Record<string,unknown>,response:unknown){const e=this.require(id,'delete');if(request.ID!==e.model_id||request.PROJECTID!==e.project_id||Number(request.MODELTYPE)!==2||response!==true)fail('Delete response cannot be correlated.');e.request_seen=true;e.status='completed';this.save(e);}
 uploaded(id:string,file:Record<string,unknown>,remoteHash:string){
  const e=this.require(id,'upload');if(!e.sha256||e.sha256!==remoteHash||file.NAME!==e.name||typeof file.ID!=='string'||!file.ID||typeof file.PATH!=='string')fail('Uploaded file identity or remote byte hash does not match.');
  modelFileUrl(file.PATH as string);e.upload_id=file.ID as string;e.source_path=file.PATH as string;e.status='completed';e.request_seen=true;this.save(e);
 }
 uploadedFile(uploadId:string):Evidence{const rows=this.db.prepare('SELECT data FROM evidence').all().map(r=>JSON.parse(String(r.data)) as Evidence).filter(e=>e.kind==='upload'&&e.status==='completed'&&e.upload_id===uploadId);if(rows.length!==1)fail('Upload receipt is missing or ambiguous.');return rows[0]!;}
 submitted(id:string,input:Record<string,unknown>,response:Record<string,unknown>,generation:string){
  const e=this.require(id,'submit'),u=this.uploadedFile(e.upload_id??'');
  if(u.project_id!==e.project_id||u.sha256!==e.sha256||u.name!==e.name||input.ID!==''||input.PROJECTID!==e.project_id||Number(input.MODELTYPE)!==2||input.NAME!==e.name||input.MODELPATH!==u.source_path||typeof response.ID!=='string'||!response.ID||!generation)fail('Submit response does not match the uploaded file and project.');
  e.model_id=response.ID as string;e.generation=generation;e.source_path=u.source_path;e.status='completed';e.request_seen=true;this.save(e);
 }
 operation(id:string):Operation{const e=this.get(id);if(!e)fail('No persisted evidence exists for this real operation.');return e!;}
 calculationStarted(id:string,project:string,models:string[]){
  const e=this.require(id,'submit');if(e.status!=='completed'||e.project_id!==project||!e.model_id||!models.includes(e.model_id)||new Set(models).size!==models.length)fail('Calculation start does not include this submitted model/project.');
  e.calculation_started_utc=new Date().toISOString();e.calculation_models=[...models].sort();e.calculation_running_utc=undefined;e.calculation_saved_utc=undefined;e.calculation_result_hash=undefined;e.calculation_observation_version=2;this.save(e);
 }
 calculationRunning(id:string){const e=this.require(id,'submit');if(!e.calculation_started_utc)fail('Calculation was not accepted in this run.');e.calculation_running_utc=new Date().toISOString();this.save(e);}
 calculationSaved(id:string,project:string,models:string[],resultHash:string){
  const e=this.require(id,'submit');if(e.project_id!==project||!e.calculation_started_utc||JSON.stringify([...models].sort())!==JSON.stringify(e.calculation_models)||!resultHash)fail('Calculation save is not correlated with this current start.');
  if(!e.calculation_running_utc||e.calculation_observation_version!==2)fail('Current running observation is missing.');
  if(e.calculation_saved_utc){if(e.calculation_result_hash!==resultHash)fail('Calculation result changed after its verified completion.');return;}
  e.calculation_saved_utc=new Date().toISOString();e.calculation_result_hash=resultHash;this.save(e);
 }
 calculationProof(id:string,overlay:boolean):string|undefined{const e=this.require(id,'submit');return !overlay&&e.calculation_observation_version===2&&e.calculation_started_utc&&e.calculation_running_utc&&e.calculation_saved_utc&&e.calculation_result_hash?'observed-save:'+e.calculation_result_hash:undefined;}
 close(){this.db.close();}
}
