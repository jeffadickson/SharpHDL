/* Two-state, single-clock interpreter for the exported teaching graphs.
   This does not parse Verilog or implement event-driven/four-state HDL simulation. */
class GraphSim {
 constructor(graph){this.graph=graph;this.state={};this.inputs={};for(const [id,n] of Object.entries(graph.nodes)){if(n.op==='input')this.inputs[id]=0n;if(n.op==='reg')this.state[id]=BigInt(n.reset);} }
 mask(w){return (1n<<BigInt(w))-1n;}
 signed(x,w){return x&(1n<<BigInt(w-1))?x-(1n<<BigInt(w)):x;}
 set(name,value){const n=this.graph.nodes[name];if(!n||n.op!=='input')throw Error('Not an input: '+name);this.inputs[name]=BigInt(value)&this.mask(n.width);}
 eval(id,cache={}){
  if(Object.prototype.hasOwnProperty.call(cache,id))return cache[id];
  const n=this.graph.nodes[id];if(!n)throw Error('Missing graph node '+id);
  let v;const args=n.args||[],a=()=>this.eval(args[0],cache),b=()=>this.eval(args[1],cache),off=BigInt(n.offset||0);
  switch(n.op){
   case 'input':v=this.inputs[id];break;case 'reg':v=this.state[id];break;
   case 'output':case 'wire':v=this.eval(n.driver,cache);break;
   case 'const':v=BigInt(n.literal);break;
   case '+':v=a()+b();break;case '-':v=a()-b();break;case '*':v=a()*b();break;
   case '&':v=a()&b();break;case '|':v=a()|b();break;case '^':v=a()^b();break;case '~':v=~a();break;
   case '==':v=a()===b()?1n:0n;break;case '<':v=a()<b()?1n:0n;break;
   case 'slt':v=this.signed(a(),this.graph.nodes[args[0]].width)<this.signed(b(),this.graph.nodes[args[1]].width)?1n:0n;break;
   case 'mux':v=this.eval(args[a()!==0n?1:2],cache);break;
   case '<<':v=a()<<off;break;case '>>':case 'slice':v=a()>>off;break;
   case 'asr':v=this.signed(a(),this.graph.nodes[args[0]].width)>>off;break;
   case 'resize':v=a();break;case 'signextend':v=this.signed(a(),this.graph.nodes[args[0]].width);break;
   case 'concat':v=(a()<<BigInt(this.graph.nodes[args[1]].width))|b();break;
   default:throw Error('Unsupported teaching graph operation: '+n.op);
  }
  return cache[id]=v&this.mask(n.width);
 }
 tick(reset=false){const next={},cache={};for(const [id,n] of Object.entries(this.graph.nodes))if(n.op==='reg')next[id]=reset?BigInt(n.reset):this.eval(n.driver,cache);this.state=next;return this.outputs();}
 outputs(){const r={},cache={};for(const [id,n] of Object.entries(this.graph.nodes))if(n.op==='output')r[id]=this.eval(id,cache);return r;}
}
if(typeof module!=='undefined')module.exports=GraphSim;
