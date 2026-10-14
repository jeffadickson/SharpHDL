#!/usr/bin/env bash
# Resource mapping only; this does not replace vendor simulation or timing checks.
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p build/control-memory generated/control-memory/mapping
yosys -Q -T -p 'read_verilog generated/control-memory/BankedXilinx7Series.v; synth_xilinx -family xc7 -top h_BankedXilinx7Series; check -assert; tee -o generated/control-memory/mapping/xilinx-stats.json stat -json' > build/control-memory/xilinx-map.log
yosys -Q -T -p 'read_verilog generated/control-memory/BankedIntelCycloneVMapping.v; synth_intel_alm -family cyclonev -top h_BankedIntelCycloneVMapping; check -assert; tee -o generated/control-memory/mapping/cyclonev-stats.json stat -json' > build/control-memory/cyclonev-map.log
python3 - <<'PY'
import json,pathlib
p=pathlib.Path('generated/control-memory/mapping')
for name,top,cell in [('xilinx','BankedXilinx7Series','RAMB18E1'),('cyclonev','BankedIntelCycloneVMapping','MISTRAL_M10K')]:
 path=p/(name+'-stats.json')
 data=json.loads(path.read_text())
 count=data['modules']['\\h_'+top]['num_cells_by_type'].get(cell,0)
 if count!=4:raise SystemExit(f'Expected four {cell}, found {count}')
 path.write_text(json.dumps(data,indent=2,sort_keys=True)+'\n')
 print(f'PASS bank mapping {name}: four {cell}')
PY
