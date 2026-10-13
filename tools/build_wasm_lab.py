"""Bundle workbook examples with real native-Icarus traces and the WASM worker."""
from pathlib import Path
import json, subprocess, tempfile
r=Path(__file__).resolve().parents[1];d=r/'docs/interactive'
fixtures=json.loads((r/'generated/learning/verisim/links.json').read_text())
lessons=json.loads((d/'wasm-lessons.json').read_text())
for key,lesson in lessons.items():
 item=fixtures[key];lesson.update(item)
 lesson['sourceText']=(r/lesson['source']).read_text()
 with tempfile.TemporaryDirectory() as td:
  p=Path(td);(p/'design.v').write_text(item['design']);(p/'tb.v').write_text(item['testbench'])
  subprocess.run(['iverilog','-g2012','-s','tb','-o',str(p/'sim'),str(p/'design.v'),str(p/'tb.v')],check=True)
  sim=subprocess.run(['vvp',str(p/'sim')],cwd=p,text=True,capture_output=True,check=True)
  assert 'PASS VeriSim '+key in sim.stdout,sim.stdout
  lesson['vcd']=(p/'dump.vcd').read_text();lesson['log']=sim.stdout
bundle={'lessons':lessons,'worker':(r/'docs/tinyrisc/engine-worker.js').read_text()}
(r/'generated/learning/wasm-lessons.json').write_text(json.dumps(bundle))
html=(d/'wasm-template.html').read_text().replace('__DATA__',json.dumps(bundle).replace('<','\\u003c')).replace('__TRACE__',(r/'docs/tinyrisc/trace.js').read_text()).replace('__UI__',(d/'wasm-ui.js').read_text())
(d/'SharpHDL_WASM_Lab.html').write_text(html)
print('Built workbook WASM lab:',len(lessons),'self-checking RTL examples')
