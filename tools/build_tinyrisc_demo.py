from pathlib import Path
import json,base64
r=Path(__file__).resolve().parents[1];d=r/'docs/tinyrisc'
words=(r/'generated/tinyrisc/count.hex').read_text().splitlines();lines=[]
for s in (r/'examples/TinyRisc/count.asm').read_text().splitlines():
 s=s.split(';')[0].strip()
 if ':' in s:s=s.split(':',1)[1].strip()
 if s:lines.append(s)
assert len(words)==len(lines)
design=(r/'generated/tinyrisc/CountRun.v').read_text();tb=(d/'demo_tb.v').read_text();enc=lambda s:base64.urlsafe_b64encode(s.encode()).decode().rstrip('=')
data={'design':design,'tb':tb,'vcd':(d/'dump.vcd').read_text(),'log':(d/'demo-verification.txt').read_text(),'worker':(d/'engine-worker.js').read_text(),'program':[{'word':w.upper(),'asm':a}for w,a in zip(words,lines)],'verisim':'https://senolgulgonul.github.io/verisim/#d='+enc(design)+'&t='+enc(tb)}
html=(d/'template.html').read_text().replace('__DATA__',json.dumps(data).replace('<','\\u003c')).replace('__TRACE__',(d/'trace.js').read_text()).replace('__UI__',(d/'ui.js').read_text())
(d/'TinyRisc_Demo.html').write_text(html)
print('Built',len(html),'characters')
