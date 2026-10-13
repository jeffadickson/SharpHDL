/* Display helpers; time and edge selection come from the simulator's VCD. */
(function(root){
'use strict';
function nanoseconds(trace,t){
 const m=/^(\d+)?(s|ms|us|ns|ps|fs)$/.exec(trace.unit);
 return m?t*Number(m[1]||1)*({s:1e9,ms:1e6,us:1e3,ns:1,ps:1e-3,fs:1e-6}[m[2]]):null;
}
function timeLabel(trace,t){const ns=nanoseconds(trace,t);return ns===null?`${t} × ${trace.unit}`:`${Number(ns.toPrecision(12))} ns`;}
function rising(trace){let previous='x';return (trace.signals['tb.clk']?.events||[]).filter(([t,b])=>{const yes=previous==='0'&&b==='1';previous=b;return yes;}).map(e=>e[0]);}
const api={nanoseconds,timeLabel,rising};if(typeof module!=='undefined')module.exports=api;else root.WorkbookWave=api;
})(typeof window!=='undefined'?window:globalThis);
