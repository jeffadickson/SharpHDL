from pathlib import Path
import base64,json,subprocess,tempfile
from urllib.parse import urlparse,parse_qs
root=Path(__file__).resolve().parents[1]
data=json.loads((root/'generated/learning/verisim/links.json').read_text())
html=(root/'docs/interactive/SharpHDL_Learning_Lab.html').read_text()
for name,item in data.items():
 fragment=parse_qs(urlparse(item['url']).fragment)
 decode=lambda k:base64.urlsafe_b64decode(fragment[k][0]+'='*(-len(fragment[k][0])%4)).decode()
 assert decode('d')==item['design'] and decode('t')==item['testbench']
 assert item['url'].replace('&','&amp;') in html
 with tempfile.TemporaryDirectory() as td:
  d=Path(td);(d/'design.v').write_text(decode('d'));(d/'tb.v').write_text(decode('t'))
  subprocess.run(['iverilog','-g2012','-s','tb','-o',str(d/'test'),str(d/'design.v'),str(d/'tb.v')],check=True)
  result=subprocess.run(['vvp',str(d/'test')],cwd=d,check=True,text=True,capture_output=True)
  assert 'PASS VeriSim '+name in result.stdout
 print('PASS VeriSim payload and RTL',name)
