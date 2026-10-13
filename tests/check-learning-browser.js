const fs=require('fs'),path=require('path'),assert=require('assert');
const root=path.resolve(__dirname,'..'),GraphSim=require('../docs/interactive/graph-sim.js');
const data=JSON.parse(fs.readFileSync(path.join(root,'generated/learning/graphs.json')));
const rows=name=>fs.readFileSync(path.join(root,'generated/learning',name),'utf8').trim().split('\n').slice(1).map(x=>x.split(',').map(Number));
let count=0,s=new GraphSim(data.fsm.graph);
for(const [reset,start,enable,stop,phase,ticks,running,done] of rows('fsm-trace.csv')){s.set('start',start);s.set('enable',enable);s.set('stop',stop);const o=s.tick(!!reset);for(const [key,value] of Object.entries({phase,ticks,running,done})){assert.equal(o[key],BigInt(value));count++;}}
s.state.drill_state=3n;s.tick();assert.equal(s.outputs().phase,0n);count++;
s=new GraphSim(data.fixed.graph);
for(const [raw,n,left,floor,even] of rows('fixed-trace.csv')){s.set('x',raw);for(const [key,value] of Object.entries({left,floor,even})){assert.equal(s.eval(key+n),BigInt(value));count++;}}
for(const [key,n] of [['forloop',12],['whileloop',4]]){s=new GraphSim(data[key].graph);for(let a=-16;a<=16;a++)for(let b=-16;b<=16;b++){s.set('a',a);s.set('b',b);assert.equal(s.signed(s.outputs().y,32),BigInt(a+n*b));count++;}}
console.log('PASS '+count+' browser-engine/reference comparisons');
