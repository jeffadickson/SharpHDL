'use strict';
const path=require('node:path');
const fs=require('node:fs');
const {runTests,downloadAndUnzipVSCode}=require('@vscode/test-electron');
async function main(){
  let executable=await downloadAndUnzipVSCode();
  // VS Code 1.142 macOS bundles name the executable Code, while some
  // versions of @vscode/test-electron still resolve Contents/MacOS/Electron.
  if(process.platform==='darwin'&&!fs.existsSync(executable)) {
    const candidate=path.join(path.dirname(executable),'Code');
    if(fs.existsSync(candidate))executable=candidate;
  }
  await runTests({
    vscodeExecutablePath:executable,
    extensionDevelopmentPath:path.resolve(__dirname,'..'),
    extensionTestsPath:path.resolve(__dirname,'extensionHost.test.js'),
    launchArgs:[path.resolve(__dirname,'../../..'),'--disable-workspace-trust','--no-sandbox']
  });
}
main().catch(e=>{console.error(e);process.exitCode=1;});
