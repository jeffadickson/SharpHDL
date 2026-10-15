'use strict';
const path=require('node:path');
const NAME=/^[A-Za-z_][A-Za-z0-9_$]*$/;
function relativePath(value) {
  if(typeof value!=='string'||!value||value.includes('\0')||
     path.posix.isAbsolute(value)||path.win32.isAbsolute(value)||
     value.split(/[\\/]/).some(x=>x==='..'||x==='.')||
     value.includes('\\\\')) throw new Error('Unsafe project-relative path: '+value);
  return value.replaceAll('\\','/');
}
function fileList(values,name,required=false) {
  if(!Array.isArray(values)||(required&&values.length===0))throw new Error(name+' must be a nonempty array');
  return values.map(relativePath);
}
function parseProjectManifest(input) {
  const data=typeof input==='string'?JSON.parse(input):input;
  if(!data||data.version!==1||!Array.isArray(data.projects))throw new Error('Expected SharpHDL project manifest version 1');
  const seen=new Set();
  const projects=data.projects.map(p=>{
    if(!p||!NAME.test(p.id)||seen.has(p.id))throw new Error('Invalid/duplicate project ID');
    seen.add(p.id);
    if(!NAME.test(p.top))throw new Error('Invalid Verilator top module for '+p.id);
    const sources=fileList(p.sources,'sources',true);
    const outputDir=relativePath(p.outputDir);
    const waveforms=fileList(p.waveforms||[],'waveforms');
    const sourceMap=p.sourceMap?relativePath(p.sourceMap):null;
    return {id:p.id,label:typeof p.label==='string'&&p.label?p.label:p.id,
      top:p.top,sources,outputDir,waveforms,sourceMap};
  });
  return {version:1,projects};
}
function parseSourceMap(data) {
  const value=typeof data==='string'?JSON.parse(data):data;
  if(!value||value.version!==1||!Array.isArray(value.mappings))throw new Error('Invalid source map');
  return value.mappings.map(m=>{
    for(const x of [m?.source,m?.rtl]){
      relativePath(x?.file);
      if(!Number.isInteger(x.line)||x.line<1)throw new Error('Source map line must be positive');
    }
    return {source:{file:relativePath(m.source.file),line:m.source.line},
      rtl:{file:relativePath(m.rtl.file),line:m.rtl.line},
      signals:Array.isArray(m.signals)?m.signals.filter(s=>typeof s==='string') :[]};
  });
}
function mappedLocation(mappings,fromFile,fromLine,direction) {
  const from=direction==='rtlToSource'?'rtl':'source';
  const to=direction==='rtlToSource'?'source':'rtl';
  // Never guess the nearest preceding source line: a wrong RTL mapping is worse
  // than an explicit absence of provenance.
  return mappings.find(m=>m[from].file===fromFile && m[from].line===fromLine)?.[to]||null;
}
function verilogArgs(project) {
  return ['--binary','--timing','--trace','-Wno-fatal','--top-module',project.top,
    '--Mdir',project.outputDir,...project.sources];
}
function executablePath(project,platform=process.platform) {
  return path.join(project.outputDir,'V'+project.top+(platform==='win32'?'.exe':''));
}
module.exports={parseProjectManifest,parseSourceMap,mappedLocation,relativePath,verilogArgs,executablePath};
