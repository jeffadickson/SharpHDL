const assert=require('node:assert/strict');
const {test}=require('node:test');
const {parseDiagnostics}=require('./verilator');
test('parses Verilator warning and error diagnostics',()=>{
 const source='%Warning-WIDTH: rtl/core.sv:13:7: Width mismatch\n%Error: rtl/core.sv:21:2: Syntax error\n- another context line\n';
 assert.deepEqual(parseDiagnostics(source),[
 {severity:'warning',code:'Warning-WIDTH',file:'rtl/core.sv',line:13,column:7,message:'Width mismatch'},
 {severity:'error',code:'Error',file:'rtl/core.sv',line:21,column:2,message:'Syntax error'}
 ]);
});
test('does not interpret arbitrary build output as a source diagnostic',()=>{
 assert.deepEqual(parseDiagnostics('Verilator 5.x\ncompiling\n'),[]);
});
