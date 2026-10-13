const fs=require('fs'),path=require('path'),assert=require('assert'),vm=require('vm');
const root=path.resolve(__dirname,'..'),dir=path.join(root,'docs/tinyrisc'),T=require(path.join(dir,'trace.js'));
const html=fs.readFileSync(path.join(dir,'TinyRisc_Demo.html'),'utf8');
const inline=html.match(/<script>([\s\S]*?)<\/script>/)[1];new vm.Script(inline);
const data=JSON.parse(inline.match(/^\s*const DATA=(.*);\n/)[1]);assert.equal(data.design,fs.readFileSync(path.join(root,'generated/tinyrisc/CountRun.v'),'utf8'));assert.equal(data.tb,fs.readFileSync(path.join(dir,'demo_tb.v'),'utf8'));
const u=new URL(data.verisim);const query=new URLSearchParams(u.hash.slice(1));for(const [key,source]of [['d',data.design],['t',data.tb]])assert.equal(Buffer.from(query.get(key),'base64url').toString(),source);
const trace=T.parseVCD(data.vcd);const val=(n,t)=>T.numberAt(trace,'tb.'+n,t);let checks=0;
const retire=T.rising(trace,'tb.s_retired');assert.equal(retire.length,22);checks++;
const expectedPC=[0,1,2,3,4,2,3,4,2,3,4,2,3,4,2,3,4,5,6,7,8,9];
retire.forEach((t,i)=>{assert.equal(val('s_last_pc',t),expectedPC[i]);assert.equal(val('s_r0',t),0);assert.equal(val('s_fault',t),0);checks+=3;});
const expectedSums=[5,9,12,14,15];retire.filter(t=>val('s_last_pc',t)===2).forEach((t,i)=>{assert.equal(val('s_r2',t),expectedSums[i]);checks++;});
for(const [name,value] of Object.entries({s_pc:10,s_r1:0,s_r2:15,s_r3:15,s_r4:65280,s_leds:15,s_halted:1,s_fault:0})){assert.equal(val(name,trace.end),value);checks++;}
assert.equal(T.numberAt({signals:{x:{events:[[0,'x'],[2,'z'],[4,'101']]}}},'x',2),null);checks++;
assert.equal(T.numberAt({signals:{x:{events:[[0,'x'],[2,'z'],[4,'101']]}}},'x',4),5);checks++;
assert.throws(()=>T.parseVCD('not a vcd'));checks++;
console.log('PASS TinyRisc HTML/VCD: '+checks+' assertions; inline JS syntax; exact VeriSim payloads.');
