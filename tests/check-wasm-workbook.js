const fs=require('fs'),path=require('path'),assert=require('assert'),vm=require('vm');
const root=path.resolve(__dirname,'..'),Trace=require('../docs/tinyrisc/trace.js');
const bundle=JSON.parse(fs.readFileSync(path.join(root,'generated/learning/wasm-lessons.json')));
const html=fs.readFileSync(path.join(root,'docs/interactive/SharpHDL_WASM_Lab.html'),'utf8');
for(const s of html.matchAll(/<script>([\s\S]*?)<\/script>/g))new vm.Script(s[1]);
assert(!/__DATA__|__TRACE__|__UI__|__WAVE__/.test(html));
for(const [key,l]of Object.entries(bundle.lessons)){
 const t=Trace.parseVCD(l.vcd,null);
 for(const n of l.signals)assert(t.signals['tb.'+n],key+' missing '+n);
 for(const [n,v]of Object.entries(l.expected))assert.equal(Trace.numberAt(t,'tb.'+n,t.end),v,key+' '+n);
 assert(l.log.includes('PASS VeriSim '+key));
 const url=new URL(l.url),params=new URLSearchParams(url.hash.slice(1));
 assert.equal(Buffer.from(params.get('d'),'base64url').toString(),l.design);
 assert.equal(Buffer.from(params.get('t'),'base64url').toString(),l.testbench);
}
const Wave=require('../docs/interactive/waveform.js');
assert.equal(Wave.timeLabel({unit:'1ps'},28000),'28 ns');
assert.equal(Wave.timeLabel({unit:'10ps'},200),'2 ns');
assert.deepEqual(Wave.rising({signals:{'tb.clk':{events:[[0,'x'],[1,'1'],[2,'0'],[3,'1']]}}}),[3]);
const a=Trace.parseVCD(bundle.lessons.addtwo.vcd,null);
assert.equal(Trace.numberAt(a,'tb.s_a',0),2);assert.equal(Trace.numberAt(a,'tb.s_a',5000),9);assert.equal(Trace.numberAt(a,'tb.s_a',10000),0);
const c=Trace.parseVCD(bundle.lessons.counter.vcd,null);
for(const [time,value]of [[2000,0],[6000,1],[10000,2],[14000,3],[18000,3],[22000,3],[26000,4]])assert.equal(Trace.numberAt(c,'tb.s_value',time),value);
const stream=Trace.parseVCD(bundle.lessons.skid.vcd,null);
for(const [time,ds,bs,received]of [[8000,0,1,0],[12000,0,2,0],[16000,0,2,0],[21000,1,2,1],[25000,2,3,2],[29000,3,3,3],[33000,3,3,3]]){
 assert.equal(Trace.numberAt(stream,'tb.direct_sent',time),ds);
 assert.equal(Trace.numberAt(stream,'tb.buffered_sent',time),bs);
 for(const lane of ['direct','buffered'])assert.equal(Trace.numberAt(stream,'tb.'+lane+'_received',time),received);
}
assert.equal(Trace.numberAt(stream,'tb.s_direct_in_tready',17000),1);
assert.equal(Trace.numberAt(stream,'tb.s_buffered_in_tready',17000),0);
console.log('PASS seven workbook RTL fixtures, expected values, counter timing, inline JS and VeriSim payloads');
if(process.argv.includes('--browser'))(async()=>{
 const {chromium}=require('playwright'),http=require('http');
 const docs=path.join(root,'docs');const server=http.createServer((req,res)=>{
  const file=path.resolve(docs,'.'+new URL(req.url,'http://localhost').pathname);
  if(!file.startsWith(docs+path.sep)||!fs.existsSync(file)){res.writeHead(404);res.end();return;}
  res.setHeader('Content-Type',file.endsWith('.js')?'application/javascript':file.endsWith('.wasm')?'application/wasm':'text/html');res.end(fs.readFileSync(file));
 });await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
 let browser;
 try{
  browser=await chromium.launch({headless:true,...(process.env.CHROME_BIN?{executablePath:process.env.CHROME_BIN}:{}),args:['--no-sandbox']});const page=await browser.newPage({viewport:{width:1280,height:960}});const errors=[];page.on('pageerror',e=>errors.push(String(e)));
  if(process.env.VERISIM_ENGINE_DIR)await page.context().route('https://senolgulgonul.github.io/verisim/**',async route=>{const name=new URL(route.request().url()).pathname.split('/').pop();await route.fulfill({status:200,headers:{'access-control-allow-origin':'*','content-type':name.endsWith('.wasm')?'application/wasm':'application/javascript'},body:fs.readFileSync(path.join(process.env.VERISIM_ENGINE_DIR,name))});});
  await page.goto('http://127.0.0.1:'+server.address().port+'/interactive/SharpHDL_WASM_Lab.html');if(fs.existsSync(path.join(docs,'tinyrisc/verisim/ivl.wasm')))await page.check('#local');
  for(const [key,l]of Object.entries(bundle.lessons)){
   await page.selectOption('#lesson',key);assert.equal(await page.locator('#title').innerText(),l.title);
   const edges=Wave.rising(Trace.parseVCD(l.vcd,null));
   if(edges.length){await page.click('#edge');assert.equal(Number(await page.locator('#cursor').inputValue()),edges[0]);await page.click('#first');}else assert(await page.locator('#edge').isDisabled());
   await page.click('#next');assert(Number(await page.locator('#cursor').inputValue())>0);
   await page.click('#run');await page.waitForFunction(()=>!document.getElementById('run').disabled,null,{timeout:75000});
   assert((await page.locator('#status').innerText()).includes('Fresh WebAssembly simulation passed'),await page.locator('#status').innerText());
   const observed=await page.evaluate(()=>({signals:trace.signals,end:trace.end}));const reference=Trace.parseVCD(l.vcd,null);
   assert.equal(observed.end,reference.end);
   for(const n of l.signals){const name='tb.'+n,actual=observed.signals[name],expected=reference.signals[name];assert.equal(actual.width,expected.width);for(const t of new Set([...actual.events,...expected.events].map(e=>e[0])))assert.equal(Trace.numberAt(observed,name,t),Trace.numberAt(reference,name,t),key+' '+name+' at '+t);}
   if(key==='direct'||key==='skid'){
    await page.click('#first');
    const accepted=key==='direct'?[null,null,null,10,11,12,null]:[10,11,null,null,12,null,null];
    const delivered=[null,null,null,10,11,12,null];
    await page.click('#edge');assert((await page.locator('#edgeNumber').innerText()).endsWith('after-reset edge 0'));
    for(let i=0;i<7;i++){
     await page.click('#edge');assert((await page.locator('#edgeNumber').innerText()).endsWith('after-reset edge '+(i+1)));
     const summary=await page.locator('#transfer').innerText();
     assert(summary.includes(accepted[i]===null?'input no transfer':'input accepted '+accepted[i]),summary);
     assert(summary.includes(delivered[i]===null?'output no transfer':'output delivered '+delivered[i]),summary);
    }
    assert(await page.locator('#edge').isDisabled());
   }
   await page.click('#last');if(process.env.QA_DIR&&(key==='counter'||key==='skid')){await page.screenshot({path:path.join(process.env.QA_DIR,'wasm-'+key+'-desktop.png'),fullPage:true});await page.locator('#wave').screenshot({path:path.join(process.env.QA_DIR,'wasm-'+key+'-wave.png')});}
   console.log('PASS actual workbook WASM and native trace agreement:',key);
  }
  await page.setViewportSize({width:390,height:844});assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));if(process.env.QA_DIR)await page.screenshot({path:path.join(process.env.QA_DIR,'wasm-mobile.png'),fullPage:true});
  await page.click('#run');await page.click('#cancel');assert((await page.locator('#status').innerText()).includes('cancelled'));
  await page.click('#recorded');assert((await page.locator('#status').innerText()).includes('Recorded native-Icarus'));
  assert.deepEqual(errors,[]);console.log('PASS workbook controls, cancellation and mobile layout');
 }finally{if(browser)await browser.close();await new Promise(resolve=>server.close(resolve));}
})().catch(e=>{console.error(e);process.exitCode=1;});
