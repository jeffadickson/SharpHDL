'use strict';
const assert=require('node:assert/strict');
const {setTimeout:delay}=require('node:timers/promises');
const vscode=require('vscode');

async function waitForExit(pid){
  // Task termination and OS process reaping are observed through separate APIs.
  for(let attempt=0;attempt<40;attempt++){
    try {process.kill(pid,0);}
    catch(error){if(error.code==='ESRCH')return;throw error;}
    await delay(50);
  }
  assert.fail('Cancelled fixture process remained alive: '+pid);
}

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
    assert.equal(outcome.exitCode,23);
    assert.deepEqual(operations,['Verilator build RailwayFixture']);

    const cancellation=new vscode.CancellationTokenSource();
    let subscription,timer,building;
    try {
      // Wait for process start so Busy and cancellation are tested while the
      // build owns the shared output, independent of executable startup time.
      const started=new Promise((resolve,reject)=>{
        timer=setTimeout(()=>reject(new Error('Hold task did not start')),10000);
        subscription=vscode.tasks.onDidStartTaskProcess(event=>{
          if(event.execution.task.name==='Verilator build Hold')resolve(event.processId);
        });
      });
      building=vscode.commands.executeCommand('sharphdl.buildDesign',
        {...selector,projectId:'Hold'},cancellation.token);
      const pid=await started;
      const overlap=await vscode.commands.executeCommand('sharphdl.buildDesign',
        {...selector,projectId:'Overlap'});
      assert.equal(overlap.status,'Busy',JSON.stringify(overlap));
      cancellation.cancel();
      const cancelled=await building;
      assert.equal(cancelled.status,'Cancelled',JSON.stringify(cancelled));
      assert.equal(cancelled.stage,'build');
      await waitForExit(pid);

      await vscode.workspace.fs.writeFile(vscode.Uri.joinPath(folder.uri,'allow-retry'),new Uint8Array());
      const retry=await vscode.commands.executeCommand('sharphdl.buildDesign',{...selector,projectId:'Hold'});
      assert.equal(retry.exitCode,23,JSON.stringify(retry));
      assert.deepEqual(operations,['Verilator build RailwayFixture','Verilator build Hold','Verilator build Hold']);
    } finally {
      clearTimeout(timer);
      subscription?.dispose();
      cancellation.cancel();
      cancellation.dispose();
      await building;
    }
  } finally {
    start.dispose();
  }
}
module.exports={run};
