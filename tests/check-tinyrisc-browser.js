// npm install playwright; set CHROME_BIN if using an existing Chromium binary.
// Set VERISIM_ENGINE_DIR to fetched assets for offline test routing.
const {chromium}=require('playwright'),fs=require('fs'),path=require('path'),http=require('http'),assert=require('assert');
(async()=>{
 const dir=path.resolve(__dirname,'../docs/tinyrisc');
 const server=http.createServer((req,res)=>{res.setHeader('Content-Type','text/html');res.end(fs.readFileSync(path.join(dir,'TinyRisc_Demo.html')));});
 await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
 const browser=await chromium.launch({headless:true,...(process.env.CHROME_BIN?{executablePath:process.env.CHROME_BIN}:{}),args:['--no-sandbox']});
 try{
  const page=await browser.newPage({viewport:{width:1380,height:1050}}),errors=[];page.on('pageerror',e=>errors.push(String(e)));
  if(process.env.VERISIM_ENGINE_DIR)await page.context().route('https://senolgulgonul.github.io/verisim/**',async route=>{const name=new URL(route.request().url()).pathname.split('/').pop();await route.fulfill({status:200,headers:{'access-control-allow-origin':'*','content-type':name.endsWith('.wasm')?'application/wasm':'application/javascript'},body:fs.readFileSync(path.join(process.env.VERISIM_ENGINE_DIR,name))});});
  await page.goto('http://127.0.0.1:'+server.address().port+'/TinyRisc_Demo.html');
  await page.locator('#instruction').click();assert.equal(await page.locator('#pc').innerText(),'1');
  for(let i=0;i<21;i++)await page.locator('#instruction').click();assert.equal(await page.locator('#ledValue').innerText(),'15');
  await page.locator('#zoomIn').click();await page.locator('#fit').click();
  if(process.env.QA_DIR)await page.screenshot({path:path.join(process.env.QA_DIR,'desktop.png'),fullPage:true});
  await page.setViewportSize({width:390,height:844});if(process.env.QA_DIR)await page.screenshot({path:path.join(process.env.QA_DIR,'mobile.png'),fullPage:true});
  await page.setViewportSize({width:1380,height:1050});await page.locator('#simulate').click();
  await page.waitForFunction(()=>!document.getElementById('simulate').disabled,null,{timeout:75000});
  const status=await page.locator('#engineStatus').innerText();assert(status.includes('Fresh simulation'),status);
  for(let i=0;i<22;i++)await page.locator('#instruction').click();assert.equal(await page.locator('#pc').innerText(),'10');assert.equal(await page.locator('#ledValue').innerText(),'15');assert.deepEqual(errors,[]);
  console.log('PASS Chromium: stepping, waveform controls, responsive rendering and actual VeriSim WASM result. Engine assets: '+(process.env.VERISIM_ENGINE_DIR?'locally routed verified downloads':'live upstream'));
 }finally{await browser.close();server.close();}
})().catch(e=>{console.error(e);process.exit(1)});
