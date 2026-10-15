'use strict';
const assert=require('node:assert/strict');
const {test}=require('node:test');
const {groups,safeRelative,evidenceSummary}=require('./verificationManifest');
test('every artifact path is workspace-relative and unique',()=>{
 const all=groups.flatMap(g=>g.files.map(f=>safeRelative(f.path)));
 assert.equal(new Set(all).size,all.length);
 assert.ok(all.some(f=>f.endsWith('verification.txt')));
 assert.ok(all.some(f=>f.endsWith('results.txt')));
});
test('rejects paths that could escape the workspace',()=>{
 for(const bad of ['/etc/passwd','../secrets','rtl/../../secret','C:\\secret','']) {
   assert.throws(()=>safeRelative(bad));
 }
});
test('summarizes actual verification evidence without guessing a pass state',()=>{
 assert.deepEqual(evidenceSummary('building\nPASS cpu\nFAIL uart\n'),
 ['PASS cpu','FAIL uart']);
 assert.deepEqual(evidenceSummary('building only'),[]);
});
