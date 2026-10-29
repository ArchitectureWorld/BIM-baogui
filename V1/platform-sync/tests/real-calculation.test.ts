import test from 'node:test';import assert from 'node:assert/strict';
import {calculationInput,calculationState} from '../src/real-calculation.js';
const models=['00000000-0000-0000-0000-000000abcdef','00000000-0000-0000-0000-000000123456'];
function body(id='abcdef-32',project='p1'){return `--b\r\nContent-Disposition: form-data; name="projectId"\r\n\r\n${project}\r\n--b\r\nContent-Disposition: form-data; name="ifcFileArr"; filename="1000.json"\r\nContent-Type: application/json\r\n\r\n[{"locadId":"${id}","children":[]}]\r\n--b\r\nContent-Disposition: form-data; name="ifcFileArr"; filename="1001.json"\r\nContent-Type: application/json\r\n\r\n[]\r\n--b--\r\n`;}
test('calculation payload must include the new model under an unambiguous prefix and correct project',()=>{
 const result=calculationInput(body(),'multipart/form-data; boundary=b','p1',models,models[0]!);assert.match(result.input_hash,/^[a-f0-9]{64}$/);assert.deepEqual(result.included_model_ids,[models[0]]);
 assert.throws(()=>calculationInput(body('123456-1'),'multipart/form-data; boundary=b','p1',models,models[0]!));
 assert.throws(()=>calculationInput(body('abcdef-32','p2'),'multipart/form-data; boundary=b','p1',models,models[0]!));
 assert.throws(()=>calculationInput(body(),'multipart/form-data; boundary=b','p1',[...models,'other-abcdef'],models[0]!));
});
test('real calculation statuses distinguish accepted, running, successful and rejected',()=>{
 assert.equal(calculationState({CHECKSTATE:2}),'running');assert.equal(calculationState({CHECKSTATE:1}),'completed');assert.equal(calculationState({CHECKSTATE:3}),'completed');
 assert.equal(calculationState({CHECKSTATE:0}),'failed');assert.equal(calculationState({CHECKSTATE:4}),'failed');assert.throws(()=>calculationState({CHECKSTATE:99}));assert.throws(()=>calculationState('已完成'));
});
