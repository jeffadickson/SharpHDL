#!/usr/bin/env bash
# Open-source mapping evidence; not a replacement for Vivado/Quartus qualification.
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p build/memory-targets generated/memory-targets/mapping
yosys -Q -T -p 'read_verilog generated/memory-targets/RamXilinx7SeriesBlock.v; synth_xilinx -family xc7 -top h_RamXilinx7SeriesBlock; check -assert; tee -o generated/memory-targets/mapping/xilinx-stats.json stat -json' > generated/memory-targets/mapping/xilinx.log
yosys -Q -T -p 'read_verilog generated/memory-targets/RamIntelCycloneVBlock.v; synth_intel_alm -family cyclonev -top h_RamIntelCycloneVBlock; check -assert; tee -o generated/memory-targets/mapping/cyclonev-stats.json stat -json' > generated/memory-targets/mapping/cyclonev.log
python3 - <<'PY'
import json,pathlib
p=pathlib.Path('generated/memory-targets/mapping')
for name,top,cell in [('xilinx','RamXilinx7SeriesBlock','RAMB18E1'),('cyclonev','RamIntelCycloneVBlock','MISTRAL_M10K')]:
 data=json.loads((p/(name+'-stats.json')).read_text())
 count=data['modules']['\\h_'+top]['num_cells_by_type'].get(cell,0)
 if count!=1:raise SystemExit(f'Expected one {cell}, found {count}')
 print(f'PASS open-source mapping {name}: one {cell}')
for f in p.iterdir():f.write_text('\n'.join(line.rstrip() for line in f.read_text().splitlines()).rstrip()+'\n')
PY
