import test from 'node:test';
import assert from 'node:assert/strict';
import {parseProjectIdentity,parseModels,modelFileUrl,parseCalculationState} from '../src/real-contract.js';

const projectId='1717b620-0000-4000-8000-000000000001';
const project=()=>({ID:projectId,NAME:'Test school',PLATFORMPROCODE:'TEST-001',APPROVALSTATE:0});
const row=()=>({ID:'397a5298-0000-4000-8000-000000000002',PROJECTID:projectId,NAME:'OUT_Test_school_gate.ifc',MODELTYPE:2,MODELPATH:'Upload/Leave/20261026/123_OUT_Test_school_gate.ifc',ADDTIME:'2026-10-26T12:00:00',THREEVER:100,LIGHTSTATE:1,FILEGID:'frag-group',ADDITIONFILEARY:[]});

test('real identity reconciles project, active scene and visible form; approval state stays explicit',()=>{
 const form={name:'Test school',code:'TEST-001'};
 assert.deepEqual(parseProjectIdentity(project(),projectId,form),{project_id:projectId,project_name:'Test school',project_code:'TEST-001',approval_state:0,writable:true});
 for(const change of [{ID:''},{PLATFORMPROCODE:''},{NAME:'Different school'}])assert.throws(()=>parseProjectIdentity({...project(),...change},projectId,form));
 assert.throws(()=>parseProjectIdentity(project(),'another-scene',form));
 assert.equal(parseProjectIdentity({...project(),APPROVALSTATE:1},projectId,form).writable,false);
 assert.equal(parseProjectIdentity({...project(),APPROVALSTATE:3},projectId,form).writable,false);
 assert.throws(()=>parseProjectIdentity({...project(),APPROVALSTATE:undefined},projectId,form));
});

test('real model identity uses actual record metadata and refuses missing, duplicate or cross-project rows',()=>{
 const parsed=parseModels([row()],projectId)[0]!;
 assert.equal(parsed.id,row().ID);assert.equal(parsed.source_path,row().MODELPATH);assert.match(parsed.generation,/^record-sha256:[a-f0-9]{64}$/);
 assert.equal(parseModels([{...row(),LIGHTSTATE:2,FILEGID:'new-frag'}],projectId)[0]!.generation,parsed.generation);
 assert.notEqual(parseModels([{...row(),MODELPATH:'Upload/Leave/20261026/new.ifc'}],projectId)[0]!.generation,parsed.generation);
 assert.notEqual(parseModels([{...row(),MOVEINFO:'changed'}],projectId)[0]!.generation,parsed.generation);
 for(const value of [null,[{...row(),ID:''}],[{...row(),ADDTIME:null}],[{...row(),PROJECTID:'other'}],[row(),row()]])assert.throws(()=>parseModels(value,projectId));
});

test('IFC download addresses are confined to the observed platform file store',()=>{
 assert.equal(modelFileUrl('Upload/Leave/20261026/学校 大门.ifc'),'https://hbbim.hbcic.net.cn:9000/planning-review/api/Upload/Leave/20261026/%E5%AD%A6%E6%A0%A1%20%E5%A4%A7%E9%97%A8.ifc');
 for(const path of ['https://other.test/file.ifc','../file.ifc','Upload/Leave/../file.ifc','Upload/Leave/%2e%2e/file.ifc','Upload/Leave/a%2fb.ifc','Upload/Leave/x.frag','Upload/Leave/x.ifc?token=abc'])assert.throws(()=>modelFileUrl(path));
});

test('calculation reads are scoped to the project; historical completed status is not current-run evidence',()=>{
 assert.equal(parseCalculationState([{ProjectID:projectId,IFCCalcState:2}],projectId),'running');
 assert.equal(parseCalculationState([{ProjectID:projectId,IFCCalcState:1}],projectId),'completed');
 assert.equal(parseCalculationState([{ProjectID:projectId,IFCCalcState:-1}],projectId),'not_started');
 assert.throws(()=>parseCalculationState([{ProjectID:'another',IFCCalcState:1}],projectId));
 assert.throws(()=>parseCalculationState([{ProjectID:projectId,IFCCalcState:99}],projectId));
});
