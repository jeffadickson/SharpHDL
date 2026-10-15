'use strict';
const assert=require('node:assert/strict');
const vscode=require('vscode');
const {SharpHDLAdapter}=require('../extension');
const {showWaveforms}=require('../waveformViewer');
const extensionId='sharphdl.sharphdl-debug';

function waitForStopped(adapter) {
  return new Promise((resolve,reject)=>{
    const timeout=setTimeout(()=>{listener.dispose();reject(new Error('DAP stopped event timed out'));},10000);
    const listener=adapter.onDidSendMessage(message=>{
      if(message.type==='event'&&message.event==='stopped') {
        clearTimeout(timeout);listener.dispose();resolve(message);
      }
    });
  });
}
async function run(){
  const ext=vscode.extensions.getExtension(extensionId);
  assert.ok(ext,'SharpHDL VS Code extension not installed in test host');
  await ext.activate();
  const commands=await vscode.commands.getCommands(true);
  for(const name of ['sharphdl.openWaveforms','sharphdl.addHardwareBreakpoint',
    'sharphdl.clearHardwareBreakpoints','sharphdl.jumpToMapped','sharphdl.runDesign',
    'sharphdl.generateProvenance']) assert.ok(commands.includes(name),name);
  await vscode.commands.executeCommand('sharphdl.clearHardwareBreakpoints');

  // Exercise a real VS Code webview with a deterministic VCD, not merely command registration.
  const folder=vscode.workspace.workspaceFolders?.[0];
  assert.ok(folder,'Test host needs the SharpHDL workspace');
  const panel=await showWaveforms({folder,project:{
    label:'Waveform integration fixture',waveforms:['vscode/sharphdl/test/fixtures/crossprobe.vcd'],
    sourceMap:null
  }},['result']);
  assert.ok(panel);
  assert.match(panel.webview.html,/testbench\.result/);
  assert.match(panel.webview.html,/goCycle/);
  assert.match(panel.webview.html,/testbench\.clk/);
  panel.dispose();

  // Drive the actual DAP adapter through launch/continue/stepBack using a deterministic
  // fake localhost transport; verify the exact edge where a condition stops execution.
  const adapter=new SharpHDLAdapter([{kind:'cycle',value:5}]);
  let cycle=0;
  adapter.api=async(endpoint)=>{
    if(endpoint==='/api/state')return {cycle};
    if(endpoint==='/api/step')return {state:{cycle:++cycle,outputValid:cycle===5}};
    if(endpoint==='/api/stepBack')return {state:{cycle:--cycle}};
    throw new Error('Unexpected mock endpoint: '+endpoint);
  };
  try {
    await adapter.handleMessage({seq:1,command:'launch'});
    const stopped=waitForStopped(adapter);
    await adapter.handleMessage({seq:2,command:'continue'});
    const event=await stopped;
    assert.equal(event.body.reason,'breakpoint');
    assert.equal(adapter.state.cycle,5);
    await adapter.handleMessage({seq:3,command:'stepBack'});
    assert.equal(adapter.state.cycle,4);

    adapter.conditions=[{kind:'cycle',value:20000}];
    const paused=waitForStopped(adapter);
    await adapter.handleMessage({seq:4,command:'continue'});
    await new Promise(resolve=>setImmediate(resolve));
    await adapter.handleMessage({seq:5,command:'pause'});
    const pauseEvent=await paused;
    assert.equal(pauseEvent.body.reason,'pause');
    assert.ok(adapter.state.cycle<10000,'Pause should interrupt bounded Continue');
  } finally {adapter.dispose();}
  console.log('PASS VS Code host: activation, VCD webview, breakpoint, rewind and pause');
}
module.exports={run};
