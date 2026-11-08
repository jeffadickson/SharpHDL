'use strict';
const assert=require('node:assert/strict');
const vscode=require('vscode');

/** Run with an isolated workspace containing the RailwayFixture manifest. */
async function run(){
  const folder=vscode.workspace.workspaceFolders?.[0];
  assert.ok(folder,'Expected an isolated fixture workspace');
  const selector={workspaceUri:folder.uri.toString(),projectId:'RailwayFixture'};
  const operations=[];
  const start=vscode.tasks.onDidStartTask(e=>operations.push(e.execution.task.name));
  try {
    // The fixture selects a real executable that rejects the Verilator arguments.
    // The run must stop at the failed build and must not launch a second task.
    const outcome=await vscode.commands.executeCommand('sharphdl.runDesign',selector);
    assert.equal(outcome.status,'Failed',JSON.stringify(outcome));
    assert.equal(outcome.stage,'build');
    assert.ok(Number.isInteger(outcome.exitCode)&&outcome.exitCode!==0);
    assert.deepEqual(operations,['Verilator build RailwayFixture']);
  } finally {
    start.dispose();
  }
}
module.exports={run};
