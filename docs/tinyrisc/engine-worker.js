// Use VeriSim's hosted Icarus WebAssembly factories in an isolated worker.
// A fresh worker per run bounds lifetime; the front panel enforces a timeout.
self.onmessage=async ({data})=>{
 const log=[];
 try{
  const base=data.engineBase||'https://senolgulgonul.github.io/verisim/';
  self.postMessage({stage:'Loading VeriSim Icarus WebAssembly…'});
  const [{default:pp},{default:ivl},{default:vvp}]=await Promise.all(['ivlpp','ivl','vvp'].map(n=>import(base+n+'.js')));
  const opts={locateFile:n=>base+n,print:s=>log.push(s),printErr:s=>log.push(s)};
  const pre=[];const p=await pp({...opts,print:s=>pre.push(s)});
  p.FS.writeFile('/design.v',data.design+'\n');p.FS.writeFile('/tb.v',data.tb+'\n');
  p.callMain(['-L','/design.v','/tb.v']);
  self.postMessage({stage:'Compiling generated RTL…'});
  const c=await ivl(opts);
  c.FS.writeFile('/ivl.conf','basedir:/\nmodule:system.vpi\ngeneration:2012\ngeneration:no-specify\nout:/out.vvp\niwidth:32\nwidthcap:65536\nfunctor:cprop\nfunctor:nodangle\nflag:DLL=vvp.tgt\n');
  c.FS.writeFile('/src.v',pre.join('\n')+'\n');c.callMain(['-C/ivl.conf','--','/src.v']);
  const code=c.FS.readFile('/out.vvp');self.postMessage({stage:'Simulating RTL…'});
  const s=await vvp(opts);s.FS.writeFile('/sim.vvp',code);s.callMain(['/sim.vvp']);
  const vcd=s.FS.readFile('/dump.vcd',{encoding:'utf8'});
  if(!log.some(x=>x.includes(data.passToken||'PASS TinyRisc demo')))throw Error('Simulation did not report PASS');
  self.postMessage({vcd,log:log.join('\n')});
 }catch(e){self.postMessage({error:String(e),log:log.join('\n')});}
};
