'use strict';
const path=require('node:path');
const {runTests}=require('@vscode/test-electron');
async function main(){
  await runTests({
    extensionDevelopmentPath:path.resolve(__dirname,'..'),
    extensionTestsPath:path.resolve(__dirname,'extensionHost.test.js'),
    launchArgs:[path.resolve(__dirname,'../../..'),'--disable-workspace-trust','--no-sandbox']
  });
}
main().catch(e=>{console.error(e);process.exitCode=1;});
