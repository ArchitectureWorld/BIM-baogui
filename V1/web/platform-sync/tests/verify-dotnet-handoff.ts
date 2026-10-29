import {verifyHandoff} from '../src/handoff.js';
const file=process.argv[2];if(!file)throw Error('Pass the frozen WPF/.NET handoff fixture.');
const result=verifyHandoff(file);if(result.handoff.web?.schema!=='hifc.web-platform-export/1')throw Error('Web native receipt marker missing.');
console.log('PASS actual .NET frozen IFC/native receipt handoff verified by Node');
