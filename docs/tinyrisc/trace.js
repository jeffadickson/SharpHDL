/* TinyRisc trace reader. Values come only from VCD, never a second CPU emulator. */
(function(root){
'use strict';
function parseVCD(text){
 if(text.length>8000000)throw Error('VCD exceeds 8 MB limit');
 const signals={},ids={},scope=[],lines=text.split(/\r?\n/);let time=0,header=true,unit='ps';
 for(let i=0;i<lines.length;i++){
  let line=lines[i].trim();if(!line)continue;
  if(header){
   if(line.startsWith('$timescale')){let s=line;while(!s.includes('$end')&&i+1<lines.length)s+=' '+lines[++i];unit=s.replace('$timescale','').replace('$end','').trim().replace(/\s+/g,'');}
   else if(line.startsWith('$scope'))scope.push(line.split(/\s+/)[2]);
   else if(line.startsWith('$upscope'))scope.pop();
   else if(line.startsWith('$var')){const t=line.split(/\s+/),name=scope.concat(t[4]).join('.');const s={name,width:Number(t[2]),events:[]};signals[name]=s;(ids[t[3]]??=[]).push(s);}
   else if(line.startsWith('$enddefinitions'))header=false;
   continue;
  }
  if(line[0]==='#'){time=Number(line.slice(1));if(!Number.isSafeInteger(time)||time<0)throw Error('Invalid VCD time');continue;}
  let id,value;if(/^[01xz]/i.test(line)){value=line[0].toLowerCase();id=line.slice(1).trim();}
  else if(/^[bB]/.test(line)){const t=line.slice(1).split(/\s+/);value=t[0].toLowerCase();id=t[1];}
  else continue;
  if(!/^[01xz]+$/.test(value))throw Error('Invalid bit value');
  for(const s of ids[id]||[])s.events.push([time,value]);
 }
 if(header||!signals['tb.s_pc'])throw Error('Not a TinyRisc VCD: tb.s_pc is missing');
 return {signals,end:time,unit};
}
function bitsAt(signal,time){if(!signal)return 'x';let a=signal.events,lo=0,hi=a.length;while(lo<hi){let m=(lo+hi)>>1;if(a[m][0]<=time)lo=m+1;else hi=m;}return lo?a[lo-1][1]:'x';}
function numberAt(trace,name,time){const bits=bitsAt(trace.signals[name],time);return /^[01]+$/.test(bits)?parseInt(bits,2):null;}
function rising(trace,name){return (trace.signals[name]?.events||[]).filter(e=>e[1]==='1').map(e=>e[0]);}
const api={parseVCD,bitsAt,numberAt,rising};if(typeof module!=='undefined')module.exports=api;else root.TinyTrace=api;
})(typeof window!=='undefined'?window:globalThis);
