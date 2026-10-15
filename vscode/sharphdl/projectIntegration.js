'use strict';
const vscode=require('vscode');
const path=require('node:path');
const {parseProjectManifest,parseSourceMap,mappedLocation,relativePath,verilogArgs,executablePath}=require('./projectManifest');
const MANIFEST='sharphdl.project.json';
async function exists(uri) {try{await vscode.workspace.fs.stat(uri);return true;}catch{return false;}}
async function discover() {
  const projects=[];
  for(const folder of vscode.workspace.workspaceFolders||[]) {
    const uri=vscode.Uri.joinPath(folder.uri,MANIFEST);
    if(!await exists(uri))continue;
    try {
      const manifest=parseProjectManifest(Buffer.from(await vscode.workspace.fs.readFile(uri)).toString('utf8'));
      for(const project of manifest.projects)projects.push({folder,project});
    } catch(e) {vscode.window.showErrorMessage(MANIFEST+': '+e.message);}
  }
  return projects;
}
function projectUri(folder,relative) {
  return vscode.Uri.joinPath(folder.uri,...relativePath(relative).split('/'));
}
async function taskProcess(folder,name,command,args) {
  const execution=new vscode.ProcessExecution(command,args,{cwd:folder.uri.fsPath});
  const task=new vscode.Task({type:'sharphdl-verilator',name},folder,
    name,'SharpHDL Verilator',execution);
  task.problemMatchers=['$sharphdlVerilator'];
  task.presentationOptions={reveal:vscode.TaskRevealKind.Always,panel:vscode.TaskPanelKind.Dedicated};
  return new Promise(async(resolve,reject)=>{
    let running,settled=false;
    const done=(err,code)=>{
      if(settled)return;settled=true;
      ended.dispose();terminated.dispose();
      if(err)reject(err);else resolve(code);
    };
    const ended=vscode.tasks.onDidEndTaskProcess(e=>{
      if(e.execution===running)done(null,e.exitCode);
    });
    const terminated=vscode.tasks.onDidEndTask(e=>{
      if(e.execution===running)done(new Error(name+' was cancelled or ended without process status'));
    });
    try{running=await vscode.tasks.executeTask(task);}
    catch(e){done(e);}
  });
}
async function validateInputs(folder,project) {
  for(const source of project.sources) {
    if(!await exists(projectUri(folder,source)))throw new Error('Missing HDL source: '+source);
  }
  // Never execute arbitrary commands from project manifests.
  if(!folder.uri.fsPath)throw new Error('A local filesystem workspace is required');
}
function executionCommand(folder,program,args) {
  const config=vscode.workspace.getConfiguration('sharphdl.verilator',folder.uri);
  const runner=config.get('runner','docker');
  if(runner==='host')return {command:program,args};
  if(runner!=='docker')throw new Error('Unsupported Verilator runner: '+runner);
  const dockerArgs=['run','--rm','--mount',
    'type=bind,source='+folder.uri.fsPath+',target=/workspace',
    '-w','/workspace'];
  if(typeof process.getuid==='function'&&typeof process.getgid==='function')
    dockerArgs.push('--user',process.getuid()+':'+process.getgid());
  dockerArgs.push('sharphdl-dev:local',program,...args);
  return {command:'docker',args:dockerArgs};
}
async function buildProject(entry) {
  const {folder,project}=entry;
  await validateInputs(folder,project);
  await vscode.workspace.fs.createDirectory(projectUri(folder,project.outputDir));
  const executable=vscode.workspace.getConfiguration('sharphdl.verilator',folder.uri).get('path','verilator');
  const processArgs=executionCommand(folder,executable,verilogArgs(project));
  const status=await taskProcess(folder,'Verilator build '+project.id,processArgs.command,processArgs.args);
  if(status!==0)throw new Error('Verilator build failed for '+project.id+' (exit '+status+')');
}
async function runProject(entry) {
  if(entry.project.runnable===false)throw new Error('This source-mapped design has no testbench; build only.');
  await buildProject(entry);
  const {folder,project}=entry;
  const binary=projectUri(folder,executablePath(project));
  if(!await exists(binary))throw new Error('Verilator did not produce '+binary.fsPath);
  const runner=vscode.workspace.getConfiguration('sharphdl.verilator',folder.uri).get('runner','docker');
  const program=runner==='docker'?'/workspace/'+executablePath(project).replaceAll('\\','/'):binary.fsPath;
  const processArgs=executionCommand(folder,program,[]);
  const status=await taskProcess(folder,'Verilator test '+project.id,processArgs.command,processArgs.args);
  if(status!==0)throw new Error('Verilator test failed for '+project.id+' (exit '+status+')');
}
async function chooseProject() {
  const projects=await discover();
  if(!projects.length)throw new Error('No '+MANIFEST+' found in an open workspace');
  if(projects.length===1)return projects[0];
  const selection=await vscode.window.showQuickPick(projects.map(entry=>({
    label:entry.project.label,description:entry.folder.name+' / '+entry.project.id,entry
  })),{placeHolder:'Choose SharpHDL design'});
  return selection?.entry;
}
async function jumpToMapped(entry) {
  const editor=vscode.window.activeTextEditor;
  if(!editor)throw new Error('Open a C# or generated RTL source file first');
  const {folder,project}=entry;
  if(!project.sourceMap)throw new Error('No sourceMap defined for '+project.id);
  const uri=projectUri(folder,project.sourceMap);
  if(!await exists(uri))throw new Error('Source map not generated: '+project.sourceMap);
  const mappings=parseSourceMap(Buffer.from(await vscode.workspace.fs.readFile(uri)).toString('utf8'));
  const current=path.relative(folder.uri.fsPath,editor.document.uri.fsPath).replaceAll('\\','/');
  if(current.startsWith('..')||path.isAbsolute(current))throw new Error('Source file is outside workspace');
  const rtl=project.sources.includes(current)||current.endsWith('.v')||current.endsWith('.sv');
  const target=mappedLocation(mappings,current,editor.selection.active.line+1,rtl?'rtlToSource':'sourceToRtl');
  if(!target)throw new Error('No matching provenance for '+current+':'+(editor.selection.active.line+1));
  const doc=await vscode.workspace.openTextDocument(projectUri(folder,target.file));
  const destination=await vscode.window.showTextDocument(doc,{preview:false});
  const pos=new vscode.Position(target.line-1,0);
  destination.selection=new vscode.Selection(pos,pos);
  destination.revealRange(new vscode.Range(pos,pos),vscode.TextEditorRevealType.InCenter);
}
/** Correlate the current C# or generated RTL line with an exact mapped net.
 * Signal names can be copied into a waveform viewer's signal search; VS Code
 * does not expose a standard cross-extension waveform-selection API.
 */
async function showMappedSignals(entry) {
  const editor=vscode.window.activeTextEditor;
  if(!editor)throw new Error('Open mapped C# or Verilog source first');
  const {folder,project}=entry;
  if(!project.sourceMap)throw new Error('No compiler source map configured for '+project.id);
  const uri=projectUri(folder,project.sourceMap);
  if(!await exists(uri))throw new Error('Compiler source map not generated yet');
  const mappings=parseSourceMap(Buffer.from(await vscode.workspace.fs.readFile(uri)).toString('utf8'));
  const current=path.relative(folder.uri.fsPath,editor.document.uri.fsPath).replaceAll('\\','/');
  const line=editor.selection.active.line+1;
  const matches=mappings.filter(m=>(m.source.file===current&&m.source.line===line)||
    (m.rtl.file===current&&m.rtl.line===line));
  const signals=[...new Set(matches.flatMap(m=>m.signals))];
  if(!signals.length)throw new Error('No exact mapped signals at '+current+':'+line);
  const selection=await vscode.window.showQuickPick(signals.map(label=>({label})),{
    placeHolder:'Select mapped signal to copy for VCD/FST waveform search'
  });
  if(selection) {
    await vscode.env.clipboard.writeText(selection.label);
    vscode.window.showInformationMessage('Copied '+selection.label+'; paste into your waveform viewer signal search.');
  }
}
class ProjectExplorer {
  constructor(){this.emitter=new vscode.EventEmitter();this.onDidChangeTreeData=this.emitter.event;}
  refresh(){this.emitter.fire(undefined);}
  dispose(){this.emitter.dispose();}
  async getChildren(item) {
    if(!item) {
      const entries=await discover();
      if(!entries.length)return [new vscode.TreeItem('Add sharphdl.project.json to your workspace')];
      return entries.map(entry=>{
        const node=new vscode.TreeItem(entry.project.label,vscode.TreeItemCollapsibleState.Collapsed);
        node.entry=entry;node.iconPath=new vscode.ThemeIcon('circuit-board');return node;
      });
    }
    if(item.entry) {
      const entry=item.entry, actions=[];
      if(entry.project.id==='scheduledPolynomial') {
        const node=new vscode.TreeItem('Generate C# source map and RTL');
        node.command={command:'sharphdl.generateProvenance',title:'Generate provenance'};
        actions.push(node);
      }
      for(const [label,command] of [['Build Verilator design','sharphdl.buildDesign'],...(entry.project.runnable===false?[]:[['Run Verilator test','sharphdl.runDesign']]),['Go to mapped source / RTL','sharphdl.jumpToMapped'],['Find waveform signals','sharphdl.showMappedSignals']]){
        const node=new vscode.TreeItem(label);
        node.iconPath=new vscode.ThemeIcon(command==='sharphdl.jumpToMapped'?'go-to-file':'play');
        node.command={title:label,command,arguments:[entry]};
        actions.push(node);
      }
      for(const relative of [...entry.project.sources,...entry.project.waveforms]) {
        const uri=projectUri(entry.folder,relative);
        if(await exists(uri)) {
          const node=new vscode.TreeItem(relative);
          node.resourceUri=uri;node.iconPath=new vscode.ThemeIcon('file');
          node.command={title:'Open '+relative,command:'vscode.open',arguments:[uri]};
          actions.push(node);
        }
      }
      return actions;
    }
    return [];
  }
}
function registerProjectIntegration(context) {
  const explorer=new ProjectExplorer();
  const view=vscode.window.createTreeView('sharphdl.projects',{treeDataProvider:explorer});
  async function invoke(fn,entry) {
    try {
      const selected=entry||await chooseProject();
      if(selected)await fn(selected);
      explorer.refresh();
    }catch(e){vscode.window.showErrorMessage('SharpHDL: '+e.message);}
  }
  context.subscriptions.push(explorer,view,
    vscode.commands.registerCommand('sharphdl.buildDesign',entry=>invoke(buildProject,entry)),
    vscode.commands.registerCommand('sharphdl.runDesign',entry=>invoke(runProject,entry)),
    vscode.commands.registerCommand('sharphdl.jumpToMapped',entry=>invoke(jumpToMapped,entry)),
    vscode.commands.registerCommand('sharphdl.showMappedSignals',entry=>invoke(showMappedSignals,entry)),
    vscode.commands.registerCommand('sharphdl.refreshProjects',()=>explorer.refresh()),
    vscode.commands.registerCommand('sharphdl.generateProvenance',async()=>{
      const folder=(vscode.workspace.workspaceFolders||[])[0];
      if(!folder)return vscode.window.showErrorMessage('Open the SharpHDL repository first');
      try {
        const status=await taskProcess(folder,'Generate HLS source provenance','make',['vscode-provenance']);
        explorer.refresh();
        if(status!==0)vscode.window.showErrorMessage('Provenance generation failed; see task terminal');
      }catch(e){vscode.window.showErrorMessage(String(e.message||e));}
    }));
  const watcher=vscode.workspace.createFileSystemWatcher('**/sharphdl.project.json');
  watcher.onDidChange(()=>explorer.refresh());
  watcher.onDidCreate(()=>explorer.refresh());
  watcher.onDidDelete(()=>explorer.refresh());
  context.subscriptions.push(watcher);
}
module.exports={registerProjectIntegration};
