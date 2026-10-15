'use strict';
const assert=require('node:assert/strict');
const vscode=require('vscode');
const extensionId='sharphdl.sharphdl-debug';
async function run(){
  const ext=vscode.extensions.getExtension(extensionId);
  assert.ok(ext,'SharpHDL VS Code extension not installed in test host');
  await ext.activate();
  const commands=await vscode.commands.getCommands(true);
  for(const name of ['sharphdl.openWaveforms','sharphdl.addHardwareBreakpoint',
    'sharphdl.clearHardwareBreakpoints','sharphdl.jumpToMapped','sharphdl.runDesign',
    'sharphdl.generateProvenance']) assert.ok(commands.includes(name),name);
  await vscode.commands.executeCommand('sharphdl.clearHardwareBreakpoints');
  console.log('PASS SharpHDL graphical extension-host activation and command registration');
}
module.exports={run};
