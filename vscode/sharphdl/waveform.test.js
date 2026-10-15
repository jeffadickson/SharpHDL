'use strict';
const assert=require('node:assert/strict');
const {test}=require('node:test');
const {parseVcd,valueAt,findSignals}=require('./vcd');
const {validateCondition,matches}=require('./hardwareBreakpoints');
const fixture='$timescale 1 ns $end\n$scope module top $end\n$var wire 1 ! clk $end\n$var wire 8 " data $end\n$upscope $end\n$enddefinitions $end\n#0\n0!\nb00000000 "\n#5\n1!\n#10\nb00001101 "\n0!\n#15\n1!\n';
test('VCD parse, hierarchical signals, and exact timestamp probing',()=>{
 const v=parseVcd(fixture);
 assert.equal(v.timescale,'1ns');
 assert.equal(v.duration,15);
 assert.equal(valueAt(findSignals(v,['data'])[0].changes,9),'00000000');
 assert.equal(valueAt(findSignals(v,['data'])[0].changes,10),'00001101');
 assert.equal(findSignals(v,['top.clk']).length,1);
});
test('VCD parser rejects runaway traces and malformed timestamp',()=>{
 assert.throws(()=>parseVcd(fixture,{maxEvents:2}),/limit/);
 assert.throws(()=>parseVcd(fixture.replace('#10','#invalid')),/timestamp/);
});
test('hardware conditions use actual post-edge state and validate indexes',()=>{
 assert.equal(matches({cycle:12},{kind:'cycle',value:12}),true);
 assert.equal(matches({outputStalled:true},{kind:'boolean',field:'outputStalled'}),true);
 assert.equal(matches({stageOccupied:[false,true]},{kind:'stage',index:1}),true);
 assert.throws(()=>validateCondition({kind:'boolean',field:'__proto__'}));
 assert.throws(()=>validateCondition({kind:'stage',index:-1}));
});
