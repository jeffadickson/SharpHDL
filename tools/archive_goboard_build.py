"""Publish the just-built classroom image and reports with fresh content hashes."""
from pathlib import Path
import hashlib,json,shutil
r=Path(__file__).resolve().parents[1];out=r/'generated/goboard';out.mkdir(exist_ok=True)
for name in ('classroom.bin','synthesis.log','place-route.log'):shutil.copyfile(r/'build/goboard'/name,out/name)
p=out/'manifest.json';data=json.loads(p.read_text())
for name in data['sha256']:data['sha256'][name]=hashlib.sha256((r/name).read_bytes()).hexdigest()
p.write_text(json.dumps(data,indent=2)+'\n')
