'use strict';
const vscode=require('vscode');
const path=require('node:path');
const {groups,safeRelative}=require('./verificationManifest');

async function isFile(uri) {
  try { return (await vscode.workspace.fs.stat(uri)).type & vscode.FileType.File; }
  catch { return false; }
}
async function findSharpHDLRoot() {
  for (const folder of vscode.workspace.workspaceFolders||[]) {
    const make=vscode.Uri.joinPath(folder.uri,'Makefile');
    const script=vscode.Uri.joinPath(folder.uri,'tests/benchmark-tinyrisc-backends.sh');
    if(await isFile(make) && await isFile(script)) return folder.uri;
  }
  return null;
}
class VerificationExplorer {
  constructor() { this.changed=new vscode.EventEmitter();this.onDidChangeTreeData=this.changed.event; }
  refresh() { this.changed.fire(undefined); }
  dispose() { this.changed.dispose(); }
  async getChildren(node) {
    const root=await findSharpHDLRoot();
    if(!root) return node?[]:[this.item('Open the SharpHDL repository as a workspace','info')];
    if(!node) return [
      this.item('Run TinyRisc verification','action','sharphdl.verifyTinyRisc'),
      this.item('Provision development tools','action','sharphdl.setupTools'),
      ...groups.map(g=>this.item(g.label,'group',null,g.id)),
      this.item('Refresh verification files','action','sharphdl.refreshVerification')
    ];
    if(node.kind!=='group')return [];
    const group=groups.find(g=>g.id===node.group);
    if(!group)return [];
    const entries=[];
    for(const file of group.files) {
      const uri=vscode.Uri.joinPath(root,...safeRelative(file.path).split('/'));
      if(await isFile(uri)) {
        const child=this.item(file.label,'file','sharphdl.openVerificationFile');
        child.resourceUri=uri;
        child.tooltip=file.path;
        child.command={command:'sharphdl.openVerificationFile',title:'Open verification artifact',arguments:[uri]};
        entries.push(child);
      }
    }
    if(!entries.length)entries.push(this.item('No generated files yet','info'));
    return entries;
  }
  item(label,kind,command,group) {
    const item=new vscode.TreeItem(label,kind==='group'?
      vscode.TreeItemCollapsibleState.Collapsed:vscode.TreeItemCollapsibleState.None);
    item.kind=kind;item.group=group;
    item.contextValue=kind;
    if(kind==='action') {
      item.iconPath=new vscode.ThemeIcon('play');
      item.command={command,title:label};
    } else if(kind==='file')item.iconPath=new vscode.ThemeIcon('file');
    else if(kind==='group')item.iconPath=new vscode.ThemeIcon('folder');
    else item.iconPath=new vscode.ThemeIcon('info');
    return item;
  }
}
async function executeMakeTarget(target,provider) {
  const root=await findSharpHDLRoot();
  if(!root) { vscode.window.showErrorMessage('Open the SharpHDL repository folder before running verification.');return; }
  const execution=new vscode.ProcessExecution('make',[target],{cwd:root.fsPath});
  const task=new vscode.Task({type:'sharphdl',task:target},vscode.TaskScope.Workspace,
    'SharpHDL '+target,'SharpHDL',execution);
  task.presentationOptions={reveal:vscode.TaskRevealKind.Always,panel:vscode.TaskPanelKind.Dedicated};
  const started=await vscode.tasks.executeTask(task);
  const subscription=vscode.tasks.onDidEndTaskProcess(event=>{
    if(event.execution!==started)return;
    subscription.dispose();
    provider.refresh();
    if(event.exitCode===0) vscode.window.showInformationMessage('SharpHDL '+target+' completed. Open Verification Explorer for artifacts.');
    else vscode.window.showErrorMessage('SharpHDL '+target+' failed (exit '+event.exitCode+'). Inspect the task terminal.');
  });
}
function registerVerificationExplorer(context) {
  const provider=new VerificationExplorer();
  const tree=vscode.window.createTreeView('sharphdl.verification',{treeDataProvider:provider,showCollapseAll:true});
  context.subscriptions.push(provider,tree,
    vscode.commands.registerCommand('sharphdl.verifyTinyRisc',()=>executeMakeTarget('benchmark-tinyrisc',provider)),
    vscode.commands.registerCommand('sharphdl.setupTools',()=>executeMakeTarget('setup',provider)),
    vscode.commands.registerCommand('sharphdl.refreshVerification',()=>provider.refresh()),
    vscode.commands.registerCommand('sharphdl.openVerificationFile',uri=>vscode.window.showTextDocument(uri,{preview:false})));
  for(const pattern of ['generated/tinyrisc/**','generated/tinyrisc/backend-benchmark/**']) {
    const watcher=vscode.workspace.createFileSystemWatcher(pattern);
    watcher.onDidCreate(()=>provider.refresh());
    watcher.onDidDelete(()=>provider.refresh());
    context.subscriptions.push(watcher);
  }
}
module.exports={registerVerificationExplorer};
