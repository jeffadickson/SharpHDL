'use strict';
// Stable project-relative artifact names. Paths are resolved against a verified
// SharpHDL repository root; no arbitrary file paths are taken from reports.
const groups = Object.freeze([
  {id:'results',label:'Verification results',files:[
    {path:'generated/tinyrisc/verification.txt',label:'TinyRisc verification summary'},
    {path:'generated/tinyrisc/backend-benchmark/results.txt',label:'Three-backend benchmark summary'},
    {path:'generated/tinyrisc/backend-benchmark/dotnet.log',label:'.NET simulation benchmark log'},
    {path:'generated/tinyrisc/backend-benchmark/verilator-uart.log',label:'Verilator UART execution log'},
    {path:'generated/tinyrisc/conformance-oracles.log',label:'Independent oracle comparison'},
    {path:'generated/tinyrisc/conformance-rtl.log',label:'Icarus RTL comparison'}
  ]},
  {id:'rtl',label:'Generated hardware',files:[
    {path:'generated/tinyrisc/HelloWorld.v',label:'Hello World RTL'},
    {path:'generated/tinyrisc/backend-benchmark/BenchmarkTinyRisc.v',label:'Benchmark RTL'}
  ]},
  {id:'traces',label:'Waveforms',files:[
    {path:'generated/tinyrisc/hello_world.vcd',label:'TinyRisc waveform (if generated)'}
  ]}
]);
function safeRelative(relative) {
  if (typeof relative!=='string' || !relative ||
      relative.startsWith('/') || /^[A-Za-z]:/.test(relative) ||
      relative.split(/[\\/]/).includes('..')) throw new Error('Invalid artifact path');
  return relative.replaceAll('\\','/');
}
function evidenceSummary(text) {
  const lines=String(text).split(/\r?\n/).filter(x=>/\bPASS\b|\bFAIL\b|\bERROR\b/i.test(x));
  return lines.slice(-12);
}
module.exports={groups,safeRelative,evidenceSummary};
