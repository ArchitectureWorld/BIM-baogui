import {hash} from './security.js';
import {SyncError,type Progress} from './types.js';
const fail=(message:string):never=>{throw new SyncError('UNKNOWN',message);};
export function multipart(body:string,contentType:string):{name:string;filename?:string;value:string}[]{
 const boundary=/boundary=(?:"([^"]+)"|([^;\s]+))/i.exec(contentType);if(!boundary) return [];
 return body.split('--'+(boundary[1]??boundary[2])).flatMap(part=>{const i=part.indexOf('\r\n\r\n');if(i<0)return [];const head=part.slice(0,i),name=/\bname="([^"]+)"/.exec(head)?.[1];if(!name)return [];return [{name,filename:/\bfilename="([^"]+)"/.exec(head)?.[1],value:part.slice(i+4).replace(/\r\n$/,'')}];});
}
export function calculationInput(body:string,contentType:string,project:string,models:string[],target:string):{input_hash:string;included_model_ids:string[]}{
 const parts=multipart(body,contentType),p=parts.filter(x=>x.name==='projectId'),files=parts.filter(x=>x.name==='ifcFileArr');
 if(p.length!==1||p[0]!.value!==project||files.length!==2||files.filter(x=>x.filename==='1000.json').length!==1||files.filter(x=>x.filename==='1001.json').length!==1)fail('Calculation request project/files are unverified.');
 const prefix=new Map<string,string>();for(const id of models){const short=id.split('-').pop()!.slice(-6);if(prefix.has(short))fail('Model id prefixes collide in platform calculation data.');prefix.set(short,id);}
 const included=new Set<string>();let nodes:unknown,area:unknown;try{nodes=JSON.parse(files.find(f=>f.filename==='1000.json')!.value);area=JSON.parse(files.find(f=>f.filename==='1001.json')!.value);}catch{return fail('Calculation JSON is invalid.');}
 if(!Array.isArray(nodes)||!Array.isArray(area))fail('Calculation files must contain arrays.');
 const visit=(xs:unknown)=>{if(!Array.isArray(xs))return;for(const x of xs){if(!x||typeof x!=='object')continue;if(typeof x.locadId==='string'){const key=x.locadId.split('-')[0];if(prefix.has(key))included.add(prefix.get(key)!);}visit(x.children);}};visit(nodes);
 if(!models.includes(target)||!included.has(target))fail('New model does not appear in the actual calculation payload.');
 return {input_hash:hash(files.map(f=>f.filename+'\n'+f.value).sort().join('\n')),included_model_ids:[...included].sort()};
}
export function calculationState(value:unknown):Progress['calculation']{
 if(!value||typeof value!=='object'||!('CHECKSTATE' in value))return fail('Calculation result structure is unverified.');
 switch(value.CHECKSTATE){case 2:return 'running';case 1:case 3:return 'completed';case 0:case 4:return 'failed';default:return fail('Unknown calculation result state.');}
}
