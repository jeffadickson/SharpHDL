'use strict';
const vscode=require('vscode');
const path=require('node:path');
const crypto=require('node:crypto');
const {parseVcd,valueAt,findSignals}=require('./vcd');
const {parseSourceMap}=require('./projectManifest');
const MAX_BYTES=12*1024*1024;
function htmlEscape(s){return String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));}
async function showWaveforms(entry,sourceSignals=[]) {
  const {folder,project}=entry;
  const waveforms=[];
  for(const name of project.waveforms) {
    const uri=vscode.Uri.joinPath(folder.uri,...name.split('/'));
    try {const stat=await vscode.workspace.fs.stat(uri);if(stat.size<=MAX_BYTES)waveforms.push({name,uri});}
    catch {/* No trace until testbench emits it. */}
  }
  if(!waveforms.length)throw new Error('No VCD waveform exists for this project; run a trace-emitting testbench first.');
  const choice=waveforms.length===1?waveforms[0]:
    (await vscode.window.showQuickPick(waveforms.map(w=>({label:w.name,w}))))?.w;
  if(!choice)return;
  if(!choice.name.toLowerCase().endsWith('.vcd')) {
    await vscode.commands.executeCommand('vscode.open',choice.uri);
    return;
  }
  const bytes=await vscode.workspace.fs.readFile(choice.uri);
  if(bytes.length>MAX_BYTES)throw new Error('VCD exceeds 12 MiB viewer limit');
  const vcd=parseVcd(Buffer.from(bytes).toString('utf8'));
  const relevant=findSignals(vcd,sourceSignals);
  const clocks=vcd.signals.filter(s=>s.width===1 && /(?:^|\\.)(clk|clock)$/i.test(s.name));
  const selected=relevant.length?[...new Set([...clocks,...relevant])]:vcd.signals.slice(0,24);
  const panel=vscode.window.createWebviewPanel('sharphdl.waveforms','SharpHDL Waveforms — '+project.label,
    vscode.ViewColumn.Beside,{enableScripts:true,retainContextWhenHidden:true});
  const nonce=crypto.randomBytes(16).toString('hex');
  const payload=JSON.stringify({timescale:vcd.timescale,duration:vcd.duration,signals:selected,clock:clocks[0]?.name||null})
    .replace(/</g,'\\u003c').replace(/>/g,'\\u003e').replace(/&/g,'\\u0026');
  panel.webview.html=`<!doctype html><html><head><meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'nonce-${nonce}'; script-src 'nonce-${nonce}';">
<style nonce="${nonce}">body{font:13px system-ui;color:var(--vscode-foreground);background:var(--vscode-editor-background);padding:12px}h2{margin:0 0 12px}.line{display:flex;gap:12px;align-items:center;margin:7px 0}.name{width:210px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;cursor:pointer}.wave{width:100%;height:30px;cursor:crosshair;border-bottom:1px solid var(--vscode-panel-border)}input{width:100%}button{color:var(--vscode-button-foreground);background:var(--vscode-button-background);border:0;padding:5px 10px;cursor:pointer}small{opacity:.75}</style></head>
<body><h2>SharpHDL waveform cross-probe</h2><small>VCD ticks: ${htmlEscape(vcd.timescale||'unknown timescale')} · Select a signal to navigate to mapped C#</small>
<div class="line"><label>Clock cycle <input id="cycle" type="number" min="0" step="1" style="width:90px"></label><button id="goCycle">Go to cycle</button></div><div class="line"><label>Time <strong id="time">0</strong></label><input id="slider" type="range" min="0" max="${Math.max(1,vcd.duration)}" value="0"></div><div id="signals"></div>
<script nonce="${nonce}">const data=${payload};const host=acquireVsCodeApi();
const slider=document.getElementById('slider'),clock=document.getElementById('time'),container=document.getElementById('signals');
function valueAt(changes,t){let lo=0,hi=changes.length;while(lo<hi){const mid=(lo+hi)>>1;if(changes[mid].time<=t)lo=mid+1;else hi=mid;}return lo?changes[lo-1].value:'?';}
function draw(){
 const t=Number(slider.value);clock.textContent=String(t);container.replaceChildren();
 for(const s of data.signals){
  const row=document.createElement('div');row.className='line';
  const name=document.createElement('button');name.className='name';name.textContent=s.name;
  name.title='Jump to mapped C# source';name.onclick=()=>host.postMessage({type:'source',signal:s.name});
  const value=document.createElement('strong');value.textContent=valueAt(s.changes,t);value.style.width='70px';
  const svg=document.createElementNS('http://www.w3.org/2000/svg','svg');svg.setAttribute('viewBox','0 0 800 30');svg.classList.add('wave');
  svg.onclick=e=>{const x=e.offsetX/e.currentTarget.getBoundingClientRect().width;slider.value=String(Math.round(x*data.duration));draw();};
  let points='0,15 ',lastValue='?';
  for(const change of s.changes){
   const x=800*change.time/Math.max(1,data.duration);
   const y=lastValue==='1'?5:lastValue==='0'?25:15;
   points+=x+','+y+' '+x+','+(change.value==='1'?5:change.value==='0'?25:15)+' ';
   lastValue=change.value;
  }
  points+='800,'+(lastValue==='1'?5:lastValue==='0'?25:15);
  const poly=document.createElementNS('http://www.w3.org/2000/svg','polyline');
  poly.setAttribute('points',points);poly.setAttribute('stroke','currentColor');poly.setAttribute('fill','none');poly.setAttribute('stroke-width','1.6');svg.append(poly);
  row.append(name,value,svg);container.append(row);
 }
}
const clockSignal=data.signals.find(s=>s.name===data.clock);
const rising=[];
if(clockSignal){let old='?';for(const change of clockSignal.changes){if(change.value==='1'&&old!=='1')rising.push(change.time);old=change.value;}}
document.getElementById('goCycle').onclick=()=>{
 const cycle=Number(document.getElementById('cycle').value);
 if(!Number.isSafeInteger(cycle)||cycle<0||cycle>=rising.length){alert('No captured rising edge for this cycle');return;}
 slider.value=String(rising[cycle]);draw();
};
slider.addEventListener('input',draw);draw();</script></body></html>`;
  panel.webview.onDidReceiveMessage(async message=>{
    if(message?.type!=='source'||typeof message.signal!=='string'||!project.sourceMap)return;
    try {
      const mapUri=vscode.Uri.joinPath(folder.uri,...project.sourceMap.split('/'));
      const map=parseSourceMap(Buffer.from(await vscode.workspace.fs.readFile(mapUri)).toString('utf8'));
      const short=message.signal.split('.').at(-1);
      const matches=map.filter(m=>m.signals.includes(short)||m.signals.includes(message.signal));
      if(!matches.length){vscode.window.showInformationMessage('No mapped C# source for '+short);return;}
      const selection=matches.length===1?matches[0]:
        (await vscode.window.showQuickPick(matches.map(m=>({label:m.source.file+':'+m.source.line,m}))))?.m;
      if(!selection)return;
      const uri=vscode.Uri.joinPath(folder.uri,...selection.source.file.split('/'));
      const doc=await vscode.workspace.openTextDocument(uri);
      const editor=await vscode.window.showTextDocument(doc,{preview:false,viewColumn:vscode.ViewColumn.One});
      const pos=new vscode.Position(selection.source.line-1,0);
      editor.selection=new vscode.Selection(pos,pos);editor.revealRange(new vscode.Range(pos,pos));
    }catch(e){vscode.window.showErrorMessage('Waveform cross-probe: '+e.message);}
  });
}
module.exports={showWaveforms};
