'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const {createDesignCommands}=require('./designCommands');

test('missing and malformed selectors never start work',async()=>{
  let calls=0;
  const dispatch=createDesignCommands(async()=>{calls++;});
  assert.equal((await dispatch('sharphdl.buildDesign')).status,'NeedsInput');
  for(const input of [null,[],{}, {workspaceUri:'file:///repo',projectId:'../outside'}])
    assert.equal((await dispatch('sharphdl.runDesign',input)).status,'InvalidInput');
  assert.equal(calls,0);
});

test('execution receives only the validated selector and preserves failures',async()=>{
  const dispatch=createDesignCommands(async(operation,selector)=>{
    assert.equal(operation,'sharphdl.runDesign');
    assert.deepEqual(selector,{workspaceUri:'file:///repo',projectId:'design'});
    return {status:'Failed',stage:'build',exitCode:23};
  });
  const result=await dispatch('sharphdl.runDesign',{workspaceUri:'file:///repo',projectId:'design',command:'untrusted'});
  assert.deepEqual(result,{operation:'sharphdl.runDesign',status:'Failed',stage:'build',exitCode:23});
});

test('pre-cancelled requests leave execution untouched',async()=>{
  const dispatch=createDesignCommands(async()=>assert.fail('Cancelled request started work'));
  assert.equal((await dispatch('sharphdl.buildDesign',{workspaceUri:'file:///repo',projectId:'design'},
    {isCancellationRequested:true,onCancellationRequested(){}})).status,'Cancelled');
});

test('unexpected execution faults retain diagnostics',async()=>{
  const dispatch=createDesignCommands(async()=>{throw new Error('injected fault');});
  const result=await dispatch('sharphdl.buildDesign',{workspaceUri:'file:///repo',projectId:'design'});
  assert.equal(result.status,'UnexpectedFailure');
  assert.match(result.diagnostic,/injected fault/);
});
