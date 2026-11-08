'use strict';
const vscode=require('vscode');
const path=require('node:path');
const {randomUUID}=require('node:crypto');
const {createDesignCommands}=require('./designCommands');
const {parseProjectManifest,parseSourceMap,mappedLocation,relativePath,verilogArgs,executablePath}=require('./projectManifest');
const MANIFEST='sharphdl.project.json';
const {showWaveforms}=require('./waveformViewer');
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
async function taskProcess(folder,name,command,args,token,reveal=vscode.TaskRevealKind.Never) {
  const execution=new vscode.ProcessExecution(command,args,{cwd:folder.uri.fsPath});
  const invocation=randomUUID();
  const task=new vscode.Task({type:'sharphdl-verilator',name,invocation},folder,
    name,'SharpHDL Verilator',execution);
  task.problemMatchers=['$sharphdlVerilator'];
  task.presentationOptions={reveal,panel:vscode.TaskPanelKind.Dedicated};
  return new Promise((resolve,reject)=>{
    let running,cancelled,settled=false,grace=null;
    const done=(err,code)=>{
      if(settled)return;settled=true;
      if(grace!==null)clearTimeout(grace);
      ended.dispose();terminated.dispose();cancelled?.dispose();
      if(err)reject(err);else resolve(code);
    };
    const ended=vscode.tasks.onDidEndTaskProcess(e=>{
      if(e.execution.task.definition.invocation===invocation)done(null,e.exitCode);
    });
    const terminated=vscode.tasks.onDidEndTask(e=>{
      // Task-end can arrive before the process-exit event. Give the latter a
      // short opportunity to report the actual exit code before failing.
      if(e.execution.task.definition.invocation===invocation&&!settled&&grace===null)
        grace=setTimeout(()=>done(null,undefined),500);
    });
    try{
      cancelled=token?.onCancellationRequested(()=>running?.terminate());
      if(token?.isCancellationRequested){done(null,undefined);return;}
      Promise.resolve(vscode.tasks.executeTask(task)).then(execution=>{
        running=execution;
        if(!settled&&token?.isCancellationRequested)running.terminate();
      }).catch(done);
    }
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
async function buildProject(entry,token) {
  const {folder,project}=entry;
  if(token?.isCancellationRequested)return taskOutcome('build',undefined,token);
  await vscode.workspace.fs.createDirectory(projectUri(folder,project.outputDir));
  const executable=vscode.workspace.getConfiguration('sharphdl.verilator',folder.uri).get('path','verilator');
  const processArgs=executionCommand(folder,executable,verilogArgs(project));
  const status=await taskProcess(folder,'Verilator build '+project.id,processArgs.command,processArgs.args,token);
  return taskOutcome('build',status,token);
}
async function runProject(entry,token) {
  if(entry.project.runnable===false)return {status:'InvalidInput',stage:'validate',diagnostic:'This design has no testbench.'};
  const build=await buildProject(entry,token);
  if(build.status!=='Success')return build;
  const {folder,project}=entry;
  const binary=projectUri(folder,executablePath(project));
  if(!await exists(binary))return {status:'Failed',stage:'run',diagnostic:'Verilator did not produce '+binary.fsPath};
  const runner=vscode.workspace.getConfiguration('sharphdl.verilator',folder.uri).get('runner','docker');
  const program=runner==='docker'?'/workspace/'+executablePath(project).replaceAll('\\','/'):binary.fsPath;
  const processArgs=executionCommand(folder,program,[]);
  const status=await taskProcess(folder,'Verilator test '+project.id,processArgs.command,[...processArgs.args,'+sharphdl_trace'],token);
  return taskOutcome('run',status,token);
}
function taskOutcome(stage,exitCode,token) {
  if(token?.isCancellationRequested)return {status:'Cancelled',stage,exitCode};
  return exitCode===0?{status:'Success',stage,exitCode}:
    {status:'Failed',stage,exitCode,diagnostic:'Verilator '+stage+' did not succeed. See the task terminal.'};
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
/** Select mapped waveform nets for the currently active source line. */
async function openMappedWaveforms(entry) {
  const editor=vscode.window.activeTextEditor;
  const {folder,project}=entry;
  let signals=[];
  if(editor&&project.sourceMap) {
    try {
      const mapUri=projectUri(folder,project.sourceMap);
      const mappings=parseSourceMap(Buffer.from(await vscode.workspace.fs.readFile(mapUri)).toString('utf8'));
      const file=path.relative(folder.uri.fsPath,editor.document.uri.fsPath).replaceAll('\\','/');
      const line=editor.selection.active.line+1;
      signals=[...new Set(mappings.filter(m=>
        (m.source.file===file&&m.source.line===line)||
        (m.rtl.file===file&&m.rtl.line===line)).flatMap(m=>m.signals))];
    }catch {/* A missing map means show ordinary trace signals instead. */}
  }
  return showWaveforms(entry,signals);
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
      for(const [label,command] of [['Build Verilator design','sharphdl.selectAndBuildDesign'],...(entry.project.runnable===false?[]:[['Run Verilator test','sharphdl.selectAndRunDesign']]),['Go to mapped source / RTL','sharphdl.jumpToMapped'],['Find waveform signals','sharphdl.showMappedSignals'],['Open waveform cross-probe','sharphdl.openWaveforms']]){
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
  const activeOutputs=new Set();
  const dispatch=createDesignCommands(async(operation,selector,token)=>{
    const folder=(vscode.workspace.workspaceFolders||[]).find(f=>f.uri.toString()===selector.workspaceUri);
    if(!folder)return {status:'InvalidInput',stage:'validate',diagnostic:'Workspace is not open.'};
    let project;
    try {
      const uri=projectUri(folder,MANIFEST);
      const manifest=parseProjectManifest(Buffer.from(await vscode.workspace.fs.readFile(uri)).toString('utf8'));
      project=manifest.projects.find(p=>p.id===selector.projectId);
      if(!project)return {status:'InvalidInput',stage:'validate',diagnostic:'Design is absent from the workspace manifest.'};
      await validateInputs(folder,project);
    }catch(error){return {status:'Failed',stage:'validate',diagnostic:String(error.message||error)};}
    if(token?.isCancellationRequested)return {status:'Cancelled',stage:'validate'};
    const outputPath=path.normalize(projectUri(folder,project.outputDir).fsPath);
    const output=process.platform==='win32'?outputPath.toLowerCase():outputPath;
    if(activeOutputs.has(output))return {status:'Busy',stage:'validate',diagnostic:'A command already owns this output directory.'};
    activeOutputs.add(output);
    try{
      return await (operation==='sharphdl.runDesign'?runProject:buildProject)({folder,project},token);
    }finally{
      activeOutputs.delete(output);
      explorer.refresh();
    }
  });
  async function selectAndExecute(operation,entry) {
    try {
      const selected=entry||await chooseProject();
      if(!selected)return {operation,status:'Cancelled',stage:'select'};
      const result=await dispatch(operation,selected);
      if(result.diagnostic&&result.status!=='Cancelled')void vscode.window.showErrorMessage('SharpHDL: '+result.diagnostic);
      return result;
    }catch(error){
      const diagnostic=String(error.message||error);
      void vscode.window.showErrorMessage('SharpHDL: '+diagnostic);
      return {operation,status:'Failed',stage:'select',diagnostic};
    }
  }
  async function invoke(fn,entry) {
    try {
      const selected=entry||await chooseProject();
      if(selected)await fn(selected);
      explorer.refresh();
    }catch(e){vscode.window.showErrorMessage('SharpHDL: '+e.message);}
  }
  context.subscriptions.push(explorer,view,
    vscode.commands.registerCommand('sharphdl.buildDesign',(entry,token)=>dispatch('sharphdl.buildDesign',entry,token)),
    vscode.commands.registerCommand('sharphdl.runDesign',(entry,token)=>dispatch('sharphdl.runDesign',entry,token)),
    vscode.commands.registerCommand('sharphdl.selectAndBuildDesign',entry=>selectAndExecute('sharphdl.buildDesign',entry)),
    vscode.commands.registerCommand('sharphdl.selectAndRunDesign',entry=>selectAndExecute('sharphdl.runDesign',entry)),
    vscode.commands.registerCommand('sharphdl.jumpToMapped',entry=>invoke(jumpToMapped,entry)),
    vscode.commands.registerCommand('sharphdl.showMappedSignals',entry=>invoke(showMappedSignals,entry)),
    vscode.commands.registerCommand('sharphdl.openWaveforms',entry=>invoke(openMappedWaveforms,entry)),
    vscode.commands.registerCommand('sharphdl.refreshProjects',()=>explorer.refresh()),
    vscode.commands.registerCommand('sharphdl.generateProvenance',async()=>{
      const folder=(vscode.workspace.workspaceFolders||[])[0];
      if(!folder)return vscode.window.showErrorMessage('Open the SharpHDL repository first');
      try {
        const status=await taskProcess(folder,'Generate HLS source provenance','make',['vscode-provenance'],
          undefined,vscode.TaskRevealKind.Always);
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
