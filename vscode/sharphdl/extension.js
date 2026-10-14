const vscode = require('vscode');

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
          this.response(req,{supportsConfigurationDoneRequest:true,supportsEvaluateForHovers:false,supportsSetVariable:false,supportsStepBack:false});
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

function activate(context) {
  context.subscriptions.push(vscode.debug.registerDebugAdapterDescriptorFactory('sharphdl',{
    createDebugAdapterDescriptor() {
      return new vscode.DebugAdapterInlineImplementation(new SharpHDLAdapter());
    }
  }));
}
function deactivate() {}
module.exports={activate,deactivate};
