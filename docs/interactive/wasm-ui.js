'use strict';
const $=id=>document.getElementById(id),lessons=BUNDLE.lessons;
let key,trace,worker,workerUrl,timer;
const esc=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
function status(text,error=false){$('status').textContent=text;$('status').classList.toggle('error',error);}
function stop(){if(worker)worker.terminate();worker=null;if(workerUrl)URL.revokeObjectURL(workerUrl);workerUrl=null;clearTimeout(timer);$('run').disabled=false;$('cancel').disabled=true;}
function value(name,t){return TinyTrace.numberAt(trace,'tb.'+name,t);}
function load(vcd){const candidate=TinyTrace.parseVCD(vcd,null);for(const n of lessons[key].signals)if(!candidate.signals['tb.'+n])throw Error('Missing signal '+n);trace=candidate;trace.vcd=vcd;$('cursor').max=trace.end;$('cursor').value=0;draw();}
function draw(){
 const time=Number($('cursor').value),names=lessons[key].signals,end=Math.max(1,trace.end),x=t=>145+850*t/end;
 $('time').textContent=`Time ${time} × ${trace.unit}`;
 $('values').innerHTML=names.map(n=>{const s=trace.signals['tb.'+n],b=TinyTrace.bitsAt(s,time);return `<tr><td>${esc(n)}</td><td>${value(n,time)??'unknown'}</td><td><code>${esc(b.padStart(s.width,/^[01]+$/.test(b)?'0':b[0]))}</code></td></tr>`;}).join('');
 let svg=`<svg role="img" aria-label="Waveforms; time cursor at ${time}" viewBox="0 0 1020 ${names.length*48+35}">`;
 names.forEach((n,i)=>{const s=trace.signals['tb.'+n],y=25+i*48;svg+=`<text x="8" y="${y+4}" font-size="13">${esc(n)}</text>`;
 s.events.forEach(([t,b],j)=>{const next=s.events[j+1]?.[0]??end,a=x(t),z=x(next),known=/^[01]+$/.test(b),color=known?'#08727c':'#aa523b';if(s.width===1){const level=b==='1'?y-11:y+11;svg+=`<path d="M${a},${y-11}V${y+11}M${a},${level}H${z}" stroke="${color}" fill="none"/>`;}else{svg+=`<rect x="${a}" y="${y-13}" width="${Math.max(0,z-a)}" height="26" fill="#e3f0f1" stroke="${color}"/>`;if(z-a>24)svg+=`<text x="${a+4}" y="${y+4}" font-size="12">${esc(known?parseInt(b,2):b)}</text>`;}});
 });svg+=`<path d="M${x(time)},4V${names.length*48+12}" stroke="#c05716" stroke-width="2"/><text x="145" y="${names.length*48+28}" font-size="12">0</text><text x="890" y="${names.length*48+28}" font-size="12">${trace.end} × ${esc(trace.unit)}</text></svg>`;
 $('wave').innerHTML=svg;$('wave').querySelector('svg').onclick=e=>{const rect=e.currentTarget.getBoundingClientRect();$('cursor').value=Math.round(Math.max(0,Math.min(end,((e.clientX-rect.left)/rect.width*1020-145)/850*end)));draw();};
}
function choose(){stop();key=$('lesson').value;const l=lessons[key];location.hash=key;$('title').textContent=l.title;$('where').textContent=l.lesson;$('predict').textContent=l.predict;$('explain').textContent=l.explain;$('sourcePath').textContent=l.source;$('source').textContent=l.sourceText;$('rtl').textContent=l.design;$('bench').textContent=l.testbench;$('external').href=l.url;$('log').textContent=l.log;load(l.vcd);status('Recorded native-Icarus trace — press Run RTL for a fresh WebAssembly simulation.');}
for(const [k,l] of Object.entries(lessons))$('lesson').add(new Option(l.title,k));
$('lesson').value=lessons[location.hash.slice(1)]?location.hash.slice(1):'addtwo';$('lesson').onchange=choose;
$('cursor').oninput=draw;$('first').onclick=()=>{$('cursor').value=0;draw();};$('last').onclick=()=>{$('cursor').value=trace.end;draw();};
$('next').onclick=()=>{const times=lessons[key].signals.flatMap(n=>trace.signals['tb.'+n].events.map(e=>e[0])).filter(t=>t>Number($('cursor').value));$('cursor').value=times.length?Math.min(...times):trace.end;draw();};
$('recorded').onclick=choose;$('cancel').onclick=()=>{stop();status('Simulation cancelled. The displayed trace is unchanged.');};
$('download').onclick=()=>{const a=document.createElement('a'),url=URL.createObjectURL(new Blob([trace.vcd],{type:'text/plain'}));a.href=url;a.download=key+'.vcd';a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);};
$('run').onclick=()=>{stop();$('run').disabled=true;$('cancel').disabled=false;status('Starting WebAssembly…');
 try{workerUrl=URL.createObjectURL(new Blob([BUNDLE.worker],{type:'text/javascript'}));worker=new Worker(workerUrl,{type:'module'});
 worker.onmessage=({data})=>{if(data.stage){status(data.stage);return;}if(data.log)$('log').textContent=data.log;
  try{if(data.error)throw Error(data.error);load(data.vcd);for(const [name,expected]of Object.entries(lessons[key].expected))if(value(name,trace.end)!==expected)throw Error('Unexpected final '+name);status('Fresh WebAssembly simulation passed. Explore its waveforms below.');}catch(e){status(String(e),true);}stop();};
 worker.onerror=e=>{status('Engine error: '+e.message+'. Serve this page over HTTP; check engine access.',true);stop();};
 timer=setTimeout(()=>{stop();status('Simulation timed out after 60 seconds.',true);},60000);
 worker.postMessage({design:lessons[key].design,tb:lessons[key].testbench,passToken:'PASS VeriSim '+key,engineBase:$('local').checked?new URL('../tinyrisc/verisim/',location.href).href:'https://senolgulgonul.github.io/verisim/'});
 }catch(e){stop();status(String(e),true);}
};choose();
