"""Download upstream engine assets for offline use; verify the tested SHA256 set.
Run from any working directory. These GPL Icarus-derived assets remain separate
from SharpHDL source. See docs/tinyrisc/README.md for attribution and provenance.
"""
from pathlib import Path
import hashlib,json,urllib.request
root=Path(__file__).resolve().parents[1];d=root/'docs/tinyrisc';out=d/'verisim';out.mkdir(exist_ok=True)
for name,digest in json.loads((d/'engine-sha256.json').read_text()).items():
 target=out/name
 if target.exists() and hashlib.sha256(target.read_bytes()).hexdigest()==digest:continue
 with urllib.request.urlopen('https://senolgulgonul.github.io/verisim/'+name,timeout=60) as response:data=response.read()
 if hashlib.sha256(data).hexdigest()!=digest:raise SystemExit('Upstream changed: SHA256 mismatch for '+name+'. Review the new engine before updating the manifest.')
 target.write_bytes(data)
print('Verified VeriSim engine assets:',out)
