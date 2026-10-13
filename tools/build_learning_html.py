"""Package actual graph dumps, original sources and RTL into a standalone lesson."""
from pathlib import Path
import json,re,html
root=Path(__file__).resolve().parents[1]
allowed={'const','+','-','*','&','|','^','~','==','<','slt','mux','<<','>>','asr','slice','resize','signextend','concat'}
def graph(path):
 nodes={}
 for line in path.read_text().splitlines():
  m=re.fullmatch(r'(input|output|wire|reg) (\w+) : bits\[(\d+)\](?: reset=(\d+))?',line)
  if m:
   kind,name,w,reset=m.groups();nodes[name]={'op':kind,'width':int(w),'reset':reset or '0'};continue
  m=re.fullmatch(r'(e\d+) : bits\[(\d+)\] (\S+)(.*)',line)
  if m:
   name,w,op,tail=m.groups()
   if op not in allowed:raise ValueError('Unsupported graph node '+line)
   args=re.search(r'\((.*?)\)',tail);off=re.search(r'offset=(\d+)',tail)
   nodes[name]={'op':op,'width':int(w),'args':args.group(1).split(', ') if args else [],'offset':int(off.group(1)) if off else 0}
   if op=='const':nodes[name]['literal']=tail.strip()
   continue
  m=re.fullmatch(r'(\w+)(?:\.next)? <- (\w+)',line)
  if m:nodes[m.group(1)]['driver']=m.group(2)
 return {'nodes':nodes}
files={'fsm':root/'generated/learning/DrillController','fixed':root/'generated/learning/FixedShifts','forloop':root/'generated/behavioral/CilCsLoop12','whileloop':root/'generated/behavioral/CilCsWhile4'}
data={key:{'graph':graph(Path(str(p)+'.graph.txt')),'verilog':Path(str(p)+'.v').read_text()} for key,p in files.items()}
data['fsm']['source']=(root/'examples/LearningLab/DrillController.cs').read_text()
(root/'generated/learning/graphs.json').write_text(json.dumps(data))
def render_reference(text):
 # Small renderer for this repository-owned reference: headings, tables, code and paragraphs.
 result=[];paragraph=[];table=False;code=False
 def flush():
  if paragraph:result.append('<p>'+html.escape(' '.join(paragraph))+'</p>');paragraph.clear()
 for line in text.splitlines():
  if line.startswith('```'):
   flush()
   if code:result.append('</code></pre>');code=False
   else:result.append('<pre><code>');code=True
   continue
  if code:result.append(html.escape(line)+'\n');continue
  if line.startswith('|'):
   flush()
   cells=line.strip('|').split('|')
   if all(set(c.strip())<=set('-: ') for c in cells):continue
   tag='td' if table else 'th'
   if not table:result.append('<div class="tablewrap"><table>');table=True
   result.append('<tr>'+''.join('<'+tag+'>'+html.escape(c.strip())+'</'+tag+'>' for c in cells)+'</tr>');continue
  if table:result.append('</table></div>');table=False
  if line.startswith('#'):
   flush();result.append('<h3>'+html.escape(line.lstrip('# '))+'</h3>')
  elif line.strip():paragraph.append(line)
  else:flush()
 flush()
 if table:result.append('</table></div>')
 return ''.join(result)
template=(root/'docs/interactive/template.html').read_text().replace('__REQUIREMENTS__',render_reference((root/'CONSTRUCTS_AND_REQUIREMENTS.md').read_text()))
engine=(root/'docs/interactive/graph-sim.js').read_text()
links=json.loads((root/'generated/learning/verisim/links.json').read_text())
launch='<h2>Run real Verilog in VeriSim</h2><p><a href="SharpHDL_WASM_Lab.html">Open the workbook WASM lab with built-in waveforms</a></p><p>Open a prepared experiment, then run its teacher-owned testbench. Watch the waveforms: the unrolled adder settles without a clock; the sequential loop stays busy across many edges.</p><div class="controls">'
for key,label in [('addtwo','Add two and wrap'),('counter','Enabled counter'),('fsm','Hockey controller'),('forloop','Unrolled additions'),('sequential','Clocked while loop'),('direct','Without a buffer'),('skid','With a skid buffer')]:
 launch+='<a target="_blank" rel="noopener" href="'+html.escape(links[key]['url'],quote=True)+'">'+label+' · Open in VeriSim</a> '
launch+='</div><p class="small">VeriSim runs Icarus WebAssembly in a separate browser tab. Initial loading needs network access. RTL and testbench travel in the URL fragment. These exact payloads passed local Icarus; the hosted UI has not been end-to-end verified here. The offline graph lessons above remain separate.</p>'
template=template.replace('__VERISIM__',launch)
result=template.replace('__GRAPH_DATA__',json.dumps(data).replace('<','\\u003c')).replace('__GRAPH_ENGINE__',engine)
(root/'docs/interactive/SharpHDL_Learning_Lab.html').write_text(result)
print(root/'docs/interactive/SharpHDL_Learning_Lab.html')
