import {readJson,permitPayload,verifySignature,receiptIdentity,type Permit} from '../src/web-runtime.js';
const file=process.argv[2];if(!file)throw Error('Pass the public-only .NET interop fixture.');
const fixture=readJson<{key:{modulus:string;exponent:string};permit:Permit;receipt:Parameters<typeof receiptIdentity>[0];receiptIdentity:string}>(file);
verifySignature(permitPayload(fixture.permit),fixture.permit.signature,fixture.key);
if(receiptIdentity(fixture.receipt)!==fixture.receiptIdentity)throw Error('C#/Node receipt identity mismatch.');
console.log('PASS actual C# signed launch permit and native receipt identity verified by Node');
