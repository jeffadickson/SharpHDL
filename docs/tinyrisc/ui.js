'use strict';
const $=id=>document.getElementById(id),T=TinyTrace;
let trace,cursor=0,viewStart=0,viewEnd=1,previous={},timer=null,worker=null,deadline=null,workerUrl=null,vcd=DATA.vcd;
const names=['clk','s_pc','s_phase','s_retired','s_r1','s_r2','s_r3','s_r4','s_leds','s_halted','s_fault'];
const selected=new Set(['clk','s_pc','s_phase','s_retired','s_r1','s_r2','s_leds']);
const get=n=>T.numberAt(trace,'tb.'+n,cursor),hex=n=>n===null?'X/Z':n.toString(16).toUpperCase().padStart(4,'0');
function stopPlay(){clearInterval(timer);timer=null;$('play').textContent='Play trace';}
function load(text,label){const parsed=T.parseVCD(text);for(const n of ['clk','s_ir','s_phase','s_leds',...Array.from({length:8},(_,i)=>'s_r'+i)])if(!parsed.signals['tb.'+n])throw Error('Missing '+n);stopPlay();trace=parsed;vcd=text;cursor=0;viewStart=0;viewEnd=Math.max(1,trace.end);previous={};$('cursor').max=trace.end;$('engineStatus').textContent=label;draw();}
function setTime(t){cursor=Math.max(0,Math.min(trace.end,Math.round(t)));draw();}
function draw(){
 $('cursor').value=cursor;$('time').textContent=cursor+' × '+trace.unit;
 const pc=get('s_pc'),ir=get('s_ir'),phase=get('s_phase'),led=get('s_leds');
 $('pc').textContent=pc??'X/Z';$('ir').textContent=hex(ir);$('phase').textContent=['Fetch','Execute','RAM load'][phase]??'Unknown';$('ledValue').textContent=led??'X/Z';
 const halted=get('s_halted'),fault=get('s_fault');
 $('explanation').textContent=fault?'Fault: execution stopped.':halted?'HALT completed. The output is 15; PC now points past the program.':phase===0?'At an instruction boundary. The next active clock fetches the word at PC.':phase===1?'The instruction is decoded. Its selected arithmetic, branch, or store will complete on an execution edge.':'The processor is completing a synchronous RAM load.';
 $('program').innerHTML=DATA.program.map((p,i)=>`<tr class="${pc===i?'current':''}"><td>${i}</td><td class="code">${p.word}</td><td class="code">${p.asm}</td></tr>`).join('');
 $('haltNote').textContent=halted?'Halted · last completed instruction address '+get('s_last_pc'):get('s_retired')?'Just completed instruction at address '+get('s_last_pc'):'The PC and instruction register are separate pieces of state.';
 $('registers').innerHTML=Array.from({length:8},(_,i)=>{const n=get('s_r'+i),changed=previous[i]!==undefined&&n!==previous[i];previous[i]=n;return `<tr class="${changed?'changed':''}"><td>R${i}</td><td>${n??'X/Z'}</td><td class="code">${hex(n)}</td><td class="code">${n===null?'unknown':n.toString(2).padStart(16,'0').replace(/(.{4})(?=.)/g,'$1 ')}</td></tr>`;}).join('');
 $('leds').innerHTML=Array.from({length:16},(_,i)=>{const bit=led===null?'?':(led>>(15-i))&1;return `<span class="led ${bit===1?'on':''}" title="bit ${15-i}">${bit}</span>`;}).join('');drawWave();
}
function drawWave(){
 const ns='http://www.w3.org/2000/svg',svg=$('wave');svg.replaceChildren();const list=names.filter(n=>selected.has(n)),width=1120,left=130,right=1100,row=38,height=40+row*list.length;svg.setAttribute('viewBox',`0 0 ${width} ${height}`);
 const node=(tag,attrs,text)=>{let e=document.createElementNS(ns,tag);for(const [k,v]of Object.entries(attrs))e.setAttribute(k,v);if(text!==undefined)e.textContent=text;svg.append(e);return e;};
 const x=t=>left+(t-viewStart)/(viewEnd-viewStart)*(right-left);
 for(let i=0;i<=8;i++){let t=viewStart+(viewEnd-viewStart)*i/8,xx=x(t);node('line',{x1:xx,y1:25,x2:xx,y2:height,stroke:'#2f4057'});node('text',{x:xx,y:16,'text-anchor':'middle'},Math.round(t));}
 list.forEach((n,index)=>{let sig=trace.signals['tb.'+n],y=38+index*row;node('text',{x:8,y:y+15},n.replace('s_',''));node('text',{x:80,y:y+15},sig.width===1?T.bitsAt(sig,cursor):hex(T.numberAt(trace,'tb.'+n,cursor)));
 let points=[[viewStart,T.bitsAt(sig,viewStart)],...sig.events.filter(e=>e[0]>viewStart&&e[0]<viewEnd),[viewEnd,T.bitsAt(sig,viewEnd)]];
 for(let i=0;i<points.length-1;i++){let [t,b]=points[i],xx=x(t),end=x(points[i+1][0]),known=/^[01]+$/.test(b);if(sig.width===1&&known){let yy=y+(b==='1'?2:22);node('path',{d:`M ${xx} ${y+2} V ${y+22} M ${xx} ${yy} H ${end}`,stroke:'#66e3bf',fill:'none'});}else{node('rect',{x:xx,y:y+2,width:Math.max(0,end-xx),height:22,fill:known?'#223c51':'#67422b',stroke:'#66aaca'});if(end-xx>30)node('text',{x:xx+4,y:y+18},known?parseInt(b,2).toString(16).toUpperCase():'X/Z');}}
 });if(cursor>=viewStart&&cursor<=viewEnd)node('line',{x1:x(cursor),y1:22,x2:x(cursor),y2:height,stroke:'#ffcf82','stroke-width':2});
 svg.onclick=e=>{const rect=svg.getBoundingClientRect();setTime(viewStart+((e.clientX-rect.left)/rect.width*width-left)/(right-left)*(viewEnd-viewStart));};
}
function next(name){const time=T.rising(trace,'tb.'+name).find(t=>t>cursor);if(time===undefined){stopPlay();setTime(trace.end);}else setTime(time);}
$('rewind').onclick=()=>{stopPlay();setTime(0);};$('clock').onclick=()=>next('clk');$('instruction').onclick=()=>next('s_retired');$('cursor').oninput=e=>setTime(Number(e.target.value));
$('play').onclick=()=>{if(timer){stopPlay();return;}$('play').textContent='Pause trace';timer=setInterval(()=>next('clk'),Number($('speed').value));};
function zoom(f){let span=Math.min(trace.end,Math.max(10,(viewEnd-viewStart)*f));viewStart=Math.max(0,Math.min(trace.end-span,cursor-span/2));viewEnd=viewStart+span;drawWave();}
$('zoomIn').onclick=()=>zoom(.5);$('zoomOut').onclick=()=>zoom(2);$('fit').onclick=()=>{viewStart=0;viewEnd=trace.end;drawWave();};
for(const n of names){const label=document.createElement('label'),box=document.createElement('input');box.type='checkbox';box.checked=selected.has(n);box.onchange=()=>{box.checked?selected.add(n):selected.delete(n);drawWave();};label.append(box,document.createTextNode(' '+n.replace('s_','')));$('signals').append(label);}
$('rtl').textContent=DATA.design;$('tb').textContent=DATA.tb;$('log').textContent=DATA.log;$('external').href=DATA.verisim;
$('download').onclick=()=>{const u=URL.createObjectURL(new Blob([vcd],{type:'text/plain'})),a=document.createElement('a');a.href=u;a.download='TinyRisc.vcd';a.click();setTimeout(()=>URL.revokeObjectURL(u),1000);};
$('import').onchange=async e=>{try{const f=e.target.files[0];if(!f)return;if(f.size>8000000)throw Error('VCD exceeds 8 MB limit');load(await f.text(),'Showing imported VCD; not independently verified by this page.');}catch(err){$('engineStatus').textContent=String(err);}};
function endWorker(){clearTimeout(deadline);if(worker)worker.terminate();worker=null;if(workerUrl)URL.revokeObjectURL(workerUrl);workerUrl=null;$('simulate').disabled=false;$('cancel').disabled=true;}
$('cancel').onclick=()=>{endWorker();$('engineStatus').textContent='Simulation stopped. The previous trace remains available.';};
$('simulate').onclick=()=>{stopPlay();$('simulate').disabled=true;$('cancel').disabled=false;$('engineStatus').textContent='Starting VeriSim WebAssembly worker…';try{workerUrl=URL.createObjectURL(new Blob([DATA.worker],{type:'text/javascript'}));worker=new Worker(workerUrl,{type:'module'});worker.onmessage=({data})=>{if(data.stage)$('engineStatus').textContent=data.stage;if(data.log)$('log').textContent=data.log;if(data.error){endWorker();$('engineStatus').textContent='Browser simulation failed: '+data.error+'. Try serving this file over HTTP, or use Open in VeriSim.';}else if(data.vcd){try{load(data.vcd,'Fresh simulation: VeriSim Icarus WebAssembly reported PASS.');}catch(e){$('engineStatus').textContent=String(e);}endWorker();}};worker.onerror=e=>{endWorker();$('engineStatus').textContent='Worker could not run. Serve this file over HTTP or use Open in VeriSim. '+e.message;};deadline=setTimeout(()=>{endWorker();$('engineStatus').textContent='Simulation stopped after the 60-second time limit. Previous trace retained.';},60000);worker.postMessage({design:DATA.design,tb:DATA.tb,engineBase:$('localEngine').checked?new URL('verisim/',location.href).href:undefined});}catch(e){endWorker();$('engineStatus').textContent=String(e);}};
load(DATA.vcd,'Bundled trace: native Icarus simulation passed. Press Run RTL to simulate with VeriSim WebAssembly.');
