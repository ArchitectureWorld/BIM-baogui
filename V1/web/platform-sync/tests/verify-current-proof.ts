import {readFileSync} from 'node:fs';
import assert from 'node:assert/strict';
import {assertRequiredInputValidation} from '../src/handoff.js';
import {inputProofIdentity, type RequiredInputProof} from '../src/web-runtime.js';

const input = JSON.parse(readFileSync(process.argv[2]!, 'utf8').replace(/^\uFEFF/, '')) as {proof: RequiredInputProof; identity: string};
assertRequiredInputValidation(input.proof);
assert.equal(inputProofIdentity(input.proof), input.identity);
console.log('PASS current .NET rule proof is accepted and identically hashed by the packaged Web CLI');
