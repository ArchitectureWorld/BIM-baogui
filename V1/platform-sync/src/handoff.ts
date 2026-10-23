import {readFileSync,writeFileSync,copyFileSync,mkdirSync,renameSync,lstatSync} from 'node:fs';
import {dirname,join,resolve,isAbsolute,win32} from 'node:path';
import {randomUUID} from 'node:crypto';
import {hash,containedFile} from './security.js';
import {SyncError,type Handoff} from './types.js';

const fail=(message:string):never=>{throw new SyncError('BLOCKED',message);};
export function assertIfc(bytes:Buffer):void {
 if(bytes.length<40||!bytes.subarray(0,4096).toString('utf8').replace(/^\uFEFF/,'').trimStart().startsWith('ISO-10303-21;')||!bytes.subarray(Math.max(0,bytes.length-4096)).toString('utf8').includes('END-ISO-10303-21;'))fail('Backup/payload is not a complete STEP IFC.');
}
export function verifyHandoff(manifestPath:string):{handoff:Handoff;ifcPath:string;receiptPath:string} {
 try{
  if(lstatSync(manifestPath).isSymbolicLink())fail('Manifest links are forbidden.');
  const h=JSON.parse(readFileSync(manifestPath,'utf8').replace(/^\uFEFF/,'')) as Handoff;
  if(h.schema!=='hifc.platform-sync-handoff/1'||!/^[a-f0-9-]{32,36}$/i.test(h.export_id)||!h.project_unique_id||!h.document_path||!(isAbsolute(h.document_path)||win32.isAbsolute(h.document_path))||!Number.isFinite(Date.parse(h.created_utc)))fail('Invalid export handoff identity.');
  if(!h.original_name||!h.original_name.toLowerCase().endsWith('.ifc')||/[\\/:\x00-\x1f]/.test(h.original_name)||h.original_name==='..')fail('Invalid IFC original filename.');
  const dir=dirname(resolve(manifestPath)),ifcPath=containedFile(dir,h.ifc_file),receiptPath=containedFile(dir,h.receipt_file);
  if(lstatSync(ifcPath).isSymbolicLink()||lstatSync(receiptPath).isSymbolicLink())fail('Artifact links are forbidden.');
  const bytes=readFileSync(ifcPath),receipt=readFileSync(receiptPath);
  if(hash(bytes)!==h.ifc_sha256||hash(receipt)!==h.receipt_sha256)fail('Frozen file SHA256 hash mismatch.');
  assertIfc(bytes);
  const r=JSON.parse(receipt.toString('utf8').replace(/^\uFEFF/,''));
  if(!['hifc.native-export-receipt/1.5','hifc.native-export-receipt/1.6'].includes(r.schema_version)||r.export_id!==h.export_id||r.ifc_sha256!==h.ifc_sha256||!r.source_snapshot_before||r.source_snapshot_before!==r.source_snapshot_after||r.model_transaction!=='RolledBack'||r.export_scope!=='CURRENT_PROJECT'||r.doc_export_calls!==1||r.ifc_postprocessed!==false||r.original_export_server_restored!==true||r.temporary_type_schedules_rolled_back!==true)fail('Incomplete or mismatched native export receipt.');
  return {handoff:h,ifcPath,receiptPath};
 }catch(e){if(e instanceof SyncError)throw e;return fail('Cannot verify export handoff files.');}
}
export function freezeHandoff(manifestPath:string,spool:string):string {
 const source=verifyHandoff(manifestPath);mkdirSync(spool,{recursive:true});
 const dir=join(spool,randomUUID());mkdirSync(dir);
 copyFileSync(source.ifcPath,join(dir,'model.ifc'));copyFileSync(source.receiptPath,join(dir,'native.receipt.json'));
 const h={...source.handoff,ifc_file:'model.ifc',receipt_file:'native.receipt.json'};
 const pending=join(dir,'handoff.pending'),ready=join(dir,'handoff.json');writeFileSync(pending,JSON.stringify(h,null,2),{flag:'wx'});
 verifyHandoff(pending);renameSync(pending,ready);return ready;
}
export function documentKey(path:string):string {return win32.isAbsolute(path)?win32.normalize(path).toLowerCase():resolve(path);}
