"""Guard the generated artifacts and actual-output excerpts after regeneration."""
from pathlib import Path
import hashlib,json,re,zipfile
r=Path(__file__).resolve().parents[1]
for path in (r/'generated').rglob('*.v'):
 assert not re.search(r'\bn_\d+\b',path.read_text()),f'Legacy temporary names: {path}'
for doc,rtl in [('HOW_THE_COMPILER_WORKS.md','generated/counter.v'),('examples/ThreeLanguages/README.md','generated/three-languages/AddTwo_CSharp.v')]:
 source=(r/doc).read_text(); output=(r/rtl).read_text()
 code=re.findall(r'```verilog\n(.*?)```',source,re.S)[0]
 for line in code.splitlines():
  assert line.strip() in output,f'{doc}: stale output line {line}'
source=(r/'HOW_THE_COMPILER_WORKS.md').read_text();output=(r/'generated/fsharp/FsFir.v').read_text()
code=re.findall(r'```verilog\n(.*?)```',source,re.S)[1]
for line in code.splitlines():assert line in output,f'Stale FIR excerpt: {line}'
manifest=json.loads((r/'generated/goboard/manifest.json').read_text())
for path,expected in manifest['sha256'].items():assert hashlib.sha256((r/path).read_bytes()).hexdigest()==expected,path
with zipfile.ZipFile(r/'docs/SharpHDL_Classroom_Proposal.docx') as z:
 text=z.read('word/document.xml').decode()
 assert 'Names on the wires' in text and 'n_effective_address' in text
 for title in ['Browser lab  Follow the clock','Browser lab  Guide and extension cards']:
  assert title in text, title
 rels=z.read('word/_rels/document.xml.rels').decode()
 for key in ['addtwo','counter','fsm','forloop','sequential']:
  assert 'SharpHDL_WASM_Lab.html#'+key in rels,key
print('PASS generated net names, actual Verilog excerpts, board hashes and workbook naming and WASM sections')
