'use strict';
/** Bounded VCD parser for the VS Code cross-probe viewer.
 * It preserves simulation timestamps exactly as VCD integer ticks; time units
 * are metadata, not assumed to be nanoseconds or hardware clock cycles.
 */
function parseVcd(text,{maxEvents=150000,maxSignals=512}={}) {
  const input=String(text);
  const lines=input.split(/\r?\n/);
  const signals=[],byId=new Map(),scope=[];
  let timescale='',inDefinitions=true,time=0,events=0;
  for(let i=0;i<lines.length;i++) {
    const line=lines[i].trim();
    if(inDefinitions) {
      if(line.startsWith('$scope ')){const m=/^\$scope\s+\S+\s+(\S+)/.exec(line);if(m)scope.push(m[1]);}
      else if(line.startsWith('$upscope'))scope.pop();
      else if(line.startsWith('$timescale')) {
        const block=line.includes('$end')?line:line+' '+(lines[++i]||'').trim();
        timescale=block.replace('$timescale','').replace('$end','').trim().replace(/\s+/g,'');
      }
      else if(line.startsWith('$var ')) {
        const m=/^\$var\s+\S+\s+(\d+)\s+(\S+)\s+(\S+)/.exec(line);
        if(m) {
          if(signals.length>=maxSignals)throw new Error('VCD signal limit exceeded');
          const signal={name:[...scope,m[3]].join('.'),id:m[2],width:Number(m[1]),changes:[]};
          signals.push(signal);
          if(!byId.has(m[2]))byId.set(m[2],[]);
          byId.get(m[2]).push(signal);
        }
      } else if(line.startsWith('$enddefinitions'))inDefinitions=false;
      continue;
    }
    if(line.startsWith('#')) {
      time=Number(line.slice(1));
      if(!Number.isSafeInteger(time)||time<0)throw new Error('Invalid VCD timestamp');
      continue;
    }
    let id,value;
    const scalar=/^([01xXzZ])(\S+)$/.exec(line);
    const vector=/^[bB]([01xXzZ]+)\s+(\S+)$/.exec(line);
    if(scalar){value=scalar[1].toLowerCase();id=scalar[2];}
    else if(vector){value=vector[1].toLowerCase();id=vector[2];}
    else continue;
    for(const signal of byId.get(id)||[]) {
      if(++events>maxEvents)throw new Error('VCD event limit exceeded');
      signal.changes.push({time,value});
    }
  }
  if(inDefinitions)throw new Error('VCD missing $enddefinitions');
  return {timescale,signals,duration:time};
}
function valueAt(changes,time) {
  let lo=0,hi=changes.length;
  while(lo<hi){const mid=(lo+hi)>>1;if(changes[mid].time<=time)lo=mid+1;else hi=mid;}
  return lo?changes[lo-1].value:'?';
}
function findSignals(vcd,names) {
  const wanted=new Set(names);
  return vcd.signals.filter(s=>wanted.has(s.name)||wanted.has(s.name.split('.').at(-1)));
}
module.exports={parseVcd,valueAt,findSignals};
