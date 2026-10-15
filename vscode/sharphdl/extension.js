const vscode = require('vscode');
const {spawn} = require('node:child_process');
const path = require('node:path');
const {parseDiagnostics} = require('./verilator');

class SharpHDLAdapter {
  constructor() {
    this.emitter = new vscode.EventEmitter();
    this.onDidSendMessage = this.emitter.event;
    this.sequence = 1;
    this.cycle = 0;
    this.state = null;
    this.base = 'http://127.0.0.1:8093';
  }
  send(message) { this.emitter.fire({seq:this.sequence++,...message}); }
  response(req,body={}) { this.send({type:'response',request_seq:req.seq,success:true,command:req.command,body}); }
  error(req,message) { this.send({type:'response',request_seq:req.seq,success:false,command:req.command,message}); }
  event(event,body={}) { this.send({type:'event',event,body}); }
  async api(endpoint,method='GET',data) {
    const r=await fetch(this.base+endpoint,{method,headers:{'Content-Type':'application/json'},body:data?JSON.stringify(data):undefined});
    if(!r.ok) throw new Error('SharpHDL API '+r.status+': '+await r.text());
    return r.json();
  }
  async handleMessage(req) {
    try {
      switch(req.command) {
        case 'initialize':
          this.response(req,{supportsConfigurationDoneRequest:true,supportsEvaluateForHovers:false,supportsSetVariable:false,supportsStepBack:true});
          this.event('initialized'); break;
        case 'launch':
          this.state=await this.api('/api/state');
          this.response(req);
          this.event('stopped',{reason:'entry',threadId:1,allThreadsStopped:true});break;
        case 'configurationDone': this.response(req);break;
        case 'threads':this.response(req,{threads:[{id:1,name:'Hardware clock'}]});break;
        case 'stackTrace':
          this.response(req,{stackFrames:[{id:1,name:'SharpHDL pipeline cycle '+(this.state?.cycle??0),line:1,column:1}],totalFrames:1});break;
        case 'scopes':this.response(req,{scopes:[{name:'Hardware state',variablesReference:1,expensive:false}]});break;
        case 'variables':
          this.state=await this.api('/api/state');
          const out=[];
          for(const [key,value] of Object.entries(this.state)) {
            if(key==='graph')continue;
            out.push({name:key,value:JSON.stringify(value),variablesReference:0});
          }
          this.response(req,{variables:out});break;
        case 'next':
        case 'stepIn':
        case 'stepOut':
          {
            const result=await this.api('/api/step','POST',{valid:false,data:0,ready:true});
            this.state=result.state;
            this.response(req);
            this.event('stopped',{reason:'step',threadId:1,allThreadsStopped:true});
          }break;
        case 'stepBack':
          {
            const result=await this.api('/api/stepBack','POST',{});
            this.state=result.state;
            this.response(req);
            this.event('stopped',{reason:'step',description:'Restored prior hardware clock state',threadId:1,allThreadsStopped:true});
          }break;
        case 'continue':
          this.response(req,{allThreadsContinued:false});
          this.event('stopped',{reason:'pause',description:'Continuous running is not supported in this adapter; use Step.',threadId:1});break;
        case 'pause':this.response(req);this.event('stopped',{reason:'pause',threadId:1});break;
        case 'setBreakpoints':
          this.response(req,{breakpoints:(req.arguments?.breakpoints||[]).map(()=>({verified:false,message:'C# hardware source breakpoints not implemented yet'}))});break;
        case 'disconnect':this.response(req);this.event('terminated');break;
        default:this.response(req);
      }
    } catch(e) { this.error(req,String(e.message||e)); }
  }
  dispose() { this.emitter.dispose(); }
}

/** Run Verilator without a shell and surface errors in VS Code Problems.
 * This complements HDL language extensions; it is not a second HDL language server.
 */
function runVerilatorLint(document, diagnostics, output) {
  if (!document || !['verilog', 'systemverilog'].includes(document.languageId)) {
    vscode.window.showWarningMessage('Open a Verilog or SystemVerilog file first.');
    return;
  }
  if (document.isDirty) {
    vscode.window.showWarningMessage('Save the HDL file before running Verilator lint.');
    return;
  }
  const config = vscode.workspace.getConfiguration('sharphdl.verilator',document.uri);
  const executable = config.get('path','verilator');
  const extra = config.get('arguments',[]);
  const root = vscode.workspace.getWorkspaceFolder(document.uri)?.uri.fsPath || path.dirname(document.uri.fsPath);
  const args = ['--lint-only','--sv','--Wall','-Wno-fatal',...extra,document.uri.fsPath];
  output.appendLine('Running Verilator: '+executable+' '+args.join(' '));
  diagnostics.clear();
  const proc = spawn(executable,args,{cwd:root,shell:false,windowsHide:true});
  let text='';
  proc.stdout.on('data',d=>{text+=d.toString();});
  proc.stderr.on('data',d=>{text+=d.toString();});
  proc.on('error',error=>{
    output.appendLine(String(error));
    vscode.window.showErrorMessage('Verilator failed to start: '+error.message);
  });
  proc.on('close',code=>{
    output.appendLine(text);
    const byUri=new Map();
    for(const item of parseDiagnostics(text)) {
      const file=path.isAbsolute(item.file)?item.file:path.resolve(root,item.file);
      const uri=vscode.Uri.file(file);
      const key=uri.toString();
      if(!byUri.has(key))byUri.set(key,{uri,items:[]});
      const range=new vscode.Range(Math.max(0,item.line-1),Math.max(0,item.column-1),
        Math.max(0,item.line-1),Math.max(0,item.column));
      const diagnostic=new vscode.Diagnostic(range,item.message,
        item.severity==='error'?vscode.DiagnosticSeverity.Error:vscode.DiagnosticSeverity.Warning);
      diagnostic.source='Verilator';
      diagnostic.code=item.code;
      byUri.get(key).items.push(diagnostic);
    }
    for(const {uri,items} of byUri.values())diagnostics.set(uri,items);
    output.appendLine('Verilator exit '+code+'; '+[...byUri.values()].reduce((n,v)=>n+v.items.length,0)+' diagnostics');
    if(code!==0 && !byUri.size) vscode.window.showErrorMessage('Verilator failed without parsed diagnostics; see SharpHDL Verilator output.');
  });
}
function activate(context) {
  const diagnostics=vscode.languages.createDiagnosticCollection('SharpHDL Verilator');
  const output=vscode.window.createOutputChannel('SharpHDL Verilator');
  context.subscriptions.push(diagnostics,output,
    vscode.commands.registerCommand('sharphdl.verilatorLint',()=>
      runVerilatorLint(vscode.window.activeTextEditor?.document,diagnostics,output)));
  context.subscriptions.push(vscode.debug.registerDebugAdapterDescriptorFactory('sharphdl',{
    createDebugAdapterDescriptor() {
      return new vscode.DebugAdapterInlineImplementation(new SharpHDLAdapter());
    }
  }));
}
function deactivate() {}
module.exports={activate,deactivate};
