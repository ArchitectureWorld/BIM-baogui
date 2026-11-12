import {test} from 'node:test';
import assert from 'node:assert/strict';
import {assertRequiredInputValidation} from '../src/handoff.js';
import {receiptIdentity, type RequiredInputProof} from '../src/web-runtime.js';

const proof: RequiredInputProof = {
  schema: 'hifc.required-input-review/1',
  rule_profile_id: 'hubei-420626-planning-20261020',
  rule_revision: 'hbbim-420626-required-20261105',
  rule_evidence_sha256: '19f623d8d8309d13eb8b40e581f375813d3afd1aa35115f7898689213394d335',
  complete: true, model_kind: '总图', checked_objects: 2, checked_fields: 10, blocking_count: 0,
};

test('new platform handoff requires a complete current input review', () => {
  assert.doesNotThrow(() => assertRequiredInputValidation(proof));
  for (const changed of [undefined, {...proof, complete: false}, {...proof, blocking_count: 1},
    {...proof, checked_objects: 0}, {...proof, checked_fields: 0}, {...proof, rule_evidence_sha256: '0'.repeat(64)}]) {
    assert.throws(() => assertRequiredInputValidation(changed));
  }
});

test('receipt identity binds the input review fields', () => {
  const receipt = {planId: 'plan', documentSessionId: 'doc', ifcHash: 'hash', snapshotBefore: 'snapshot',
    snapshotAfter: 'snapshot', sourceRestored: true, exported: true, inputValidation: proof};
  assert.notEqual(receiptIdentity(receipt), receiptIdentity({...receipt, inputValidation: {...proof, checked_fields: 11}}));
  assert.notEqual(receiptIdentity(receipt), receiptIdentity({...receipt, inputValidation: undefined}));
});
