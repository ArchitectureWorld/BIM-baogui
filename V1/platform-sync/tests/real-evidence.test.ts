import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtempSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {RealEvidence} from '../src/real-evidence.js';

const project='p1',sha='a'.repeat(64);
const open=()=>new RealEvidence(mkdtempSync(join(tmpdir(),'real-proof-')));
test('intent survives restart and a second invocation cannot replay a write',()=>{
 const e=open();const dir=e.directory;e.begin('r:upload','upload',{project_id:project,sha256:sha,name:'gate.ifc'});e.close();
 const reread=new RealEvidence(dir);assert.equal(reread.get('r:upload')?.status,'pending');assert.throws(()=>reread.begin('r:upload','upload',{project_id:project,sha256:sha,name:'gate.ifc'}),/already/);reread.close();
});
test('upload needs an actual returned file record and independently verified remote hash',()=>{
 const e=open();e.begin('r:upload','upload',{project_id:project,sha256:sha,name:'gate.ifc'});
 assert.throws(()=>e.uploaded('r:upload',{ID:'file',NAME:'other.ifc',PATH:'Upload/Leave/20261026/gate.ifc'},sha));
 assert.throws(()=>e.uploaded('r:upload',{ID:'file',NAME:'gate.ifc',PATH:'Upload/Leave/20261026/gate.ifc'},'b'.repeat(64)));
 assert.equal(e.operation('r:upload').status,'pending');
 e.uploaded('r:upload',{ID:'file',NAME:'gate.ifc',PATH:'Upload/Leave/20261026/gate.ifc'},sha);
 assert.equal(e.operation('r:upload').upload_id,'file');e.close();
});
test('a submit receipt must match the uploaded path, name, project and new model id',()=>{
 const e=open();e.begin('r:upload','upload',{project_id:project,sha256:sha,name:'gate.ifc'});e.uploaded('r:upload',{ID:'file',NAME:'gate.ifc',PATH:'Upload/Leave/20261026/gate.ifc'},sha);
 e.begin('r:submit','submit',{project_id:project,sha256:sha,name:'gate.ifc',upload_id:'file'});
 const input={ID:'',MODELTYPE:2,PROJECTID:project,MODELPATH:'Upload/Leave/20261026/gate.ifc',NAME:'gate.ifc'};
 assert.throws(()=>e.submitted('r:submit',{...input,PROJECTID:'p2'}, {ID:'new'},'gen'));
 e.submitted('r:submit',input,{ID:'new'},'gen');assert.equal(e.operation('r:submit').model_id,'new');assert.equal(e.operation('r:submit').upload_id,'file');e.close();
});
test('historical completed text, uncorrelated result or visible overlay cannot prove current calculation',()=>{
 const e=open();e.begin('r:upload','upload',{project_id:project,sha256:sha,name:'gate.ifc'});e.uploaded('r:upload',{ID:'file',NAME:'gate.ifc',PATH:'Upload/Leave/20261026/gate.ifc'},sha);
 e.begin('r:submit','submit',{project_id:project,sha256:sha,name:'gate.ifc',upload_id:'file'});e.submitted('r:submit',{ID:'',MODELTYPE:2,PROJECTID:project,MODELPATH:'Upload/Leave/20261026/gate.ifc',NAME:'gate.ifc'},{ID:'new'},'gen');
 assert.equal(e.calculationProof('r:submit',false),undefined);
 assert.throws(()=>e.calculationStarted('r:submit','p2',['new']));
 e.calculationStarted('r:submit',project,['new']);
 assert.equal(e.calculationProof('r:submit',false),undefined);
 assert.throws(()=>e.calculationSaved('r:submit',project,['other'],'result-hash'));
 assert.throws(()=>e.calculationSaved('r:submit',project,['new'],'result-hash'),/running/i);
 e.calculationRunning('r:submit');
 e.calculationSaved('r:submit',project,['new'],'result-hash');
 assert.equal(e.calculationProof('r:submit',true),undefined);
 assert.match(e.calculationProof('r:submit',false)!,/^observed-save:/);e.close();
});
