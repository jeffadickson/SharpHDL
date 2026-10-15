'use strict';
const assert=require('node:assert/strict');
const {test}=require('node:test');
const {parseProjectManifest,parseSourceMap,mappedLocation,verilogArgs,executablePath}=require('./projectManifest');
const sample={version:1,projects:[{id:'uart',label:'UART',top:'tb_uart',
  sources:['rtl/top.sv','tb/test.sv'],outputDir:'build/uart',waveforms:['build/uart/trace.vcd'],
  sourceMap:'build/uart/provenance.json'}]};
test('valid multi-file design generates direct process arguments',()=>{
 const p=parseProjectManifest(sample).projects[0];
 assert.deepEqual(verilogArgs(p),['--binary','--timing','--trace','-Wno-fatal',
   '--top-module','tb_uart','--Mdir','build/uart','rtl/top.sv','tb/test.sv']);
 assert.ok(executablePath(p,'win32').endsWith('Vtb_uart.exe'));
});
test('reject traversal, absolute paths, malformed top and duplicate IDs',()=>{
 for(const bad of ['../secret.v','/tmp/file.v','C:\\secret.v']) {
  assert.throws(()=>parseProjectManifest({...sample,projects:[{...sample.projects[0],sources:[bad]}]}));
 }
 assert.throws(()=>parseProjectManifest({...sample,projects:[{...sample.projects[0],top:'tb; rm -rf .'}]}));
 assert.throws(()=>parseProjectManifest({...sample,projects:[sample.projects[0],sample.projects[0]]}));
 assert.throws(()=>parseProjectManifest({...sample,version:2}));
});
test('maps a source line to generated RTL and back with explicit provenance',()=>{
 const mappings=parseSourceMap({version:1,mappings:[{
   source:{file:'src/Counter.cs',line:12},
   rtl:{file:'generated/Counter.v',line:24},signals:['counter']
 }]});
 assert.deepEqual(mappedLocation(mappings,'src/Counter.cs',12,'sourceToRtl'),{file:'generated/Counter.v',line:24});
 assert.deepEqual(mappedLocation(mappings,'generated/Counter.v',24,'rtlToSource'),{file:'src/Counter.cs',line:12});
 assert.equal(mappedLocation(mappings,'src/Counter.cs',11,'sourceToRtl'),null);
});
