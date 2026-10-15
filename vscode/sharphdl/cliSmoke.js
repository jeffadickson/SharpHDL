#!/usr/bin/env node
'use strict';
// Actual multi-file Verilator acceptance without VS Code UI. Uses exactly the
// same manifest and process arguments as the extension, and checks exit codes.
const fs=require('node:fs');
const path=require('node:path');
const {spawnSync}=require('node:child_process');
const {parseProjectManifest,verilogArgs,executablePath}=require('./projectManifest');
const {parseVcd}=require('./vcd');
const root=path.resolve(__dirname,'../..');
const manifest=parseProjectManifest(fs.readFileSync(path.join(root,'sharphdl.project.json'),'utf8'));
for(const project of manifest.projects) {
  if(project.runnable===false)continue; // HLS source-only projects have no executable testbench.
  for(const source of project.sources) {
    if(!fs.existsSync(path.join(root,source)))throw new Error('Missing source '+source);
  }
  fs.mkdirSync(path.join(root,project.outputDir),{recursive:true});
  const executable=process.env.VERILATOR||'verilator';
  const build=spawnSync(executable,verilogArgs(project),{cwd:root,stdio:'inherit',shell:false});
  if(build.error)throw build.error;
  if(build.status!==0)throw new Error('Verilator build failed for '+project.id+': '+build.status);
  const binary=path.join(root,executablePath(project));
  const run=spawnSync(binary,['+sharphdl_trace'],{cwd:root,stdio:'inherit',shell:false});
  if(run.error)throw run.error;
  if(run.status!==0)throw new Error('Verilator test failed for '+project.id+': '+run.status);
  for(const file of project.waveforms||[]) {
    const target=path.join(root,file);
    if(!fs.existsSync(target))throw new Error('Expected VCD trace not emitted: '+file);
    const vcd=parseVcd(fs.readFileSync(target,'utf8'));
    if(!vcd.signals.length || vcd.duration<=0)throw new Error('Empty VCD trace: '+file);
    console.log('PASS waveform '+file+' ('+vcd.signals.length+' signals, '+vcd.duration+' ticks)');
  }
  console.log('PASS multi-file Verilator project: '+project.id);
}
