"""Map and route the existing TinyRisc16 CountRun CPU, with observable debug state."""
from pathlib import Path
import subprocess,os,json,hashlib
root=Path(__file__).resolve().parents[1];os.chdir(root)
out=Path('generated/tinyrisc-benchmark');build=Path('build/tinyrisc-benchmark')
out.mkdir(exist_ok=True);build.mkdir(exist_ok=True)
source='generated/tinyrisc/CountRun.v';wrapper='examples/TinyRisc/benchmark/top.v'
celllib=Path(os.environ['YOSYS_DATDIR'])/'ice40/cells_sim.v'
def run(args,log):
 p=subprocess.run(args,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
 log.write_text('\n'.join(l.rstrip() for l in p.stdout.splitlines())+'\n')
 if p.returncode:raise RuntimeError(str(log)+'\n'+p.stdout[-2000:])
 return p.stdout
# First replay the established per-cycle CPU trace on the mapped CPU itself.
run(['yosys','-Q','-T','-p',f'read_verilog {source}; synth_ice40 -top h_CountRun; check -assert; write_verilog -noattr -noexpr {build}/core.v'],out/'core-synthesis.log')
run(['iverilog','-g2012','-s','tb','-o',str(build/'core'),str(celllib),str(build/'core.v'),'generated/tinyrisc/CountRun_tb.v'],build/'compile-core.log')
if 'PASS HDL CountRun' not in run(['vvp',str(build/'core')],out/'core-traffic.log'):raise RuntimeError('Missing core PASS')
# Retain all architecture debug outputs through the mux; pins auto-assigned.
run(['yosys','-Q','-T','-p',f'read_verilog -sv {source} {wrapper}; synth_ice40 -top TinyRiscBench -json {build}/design.json; check -assert; tee -o {out}/stats.json stat -json; write_verilog -noattr -noexpr {build}/mapped.v'],out/'synthesis.log')
run(['iverilog','-g2012','-s','tb','-o',str(build/'trace'),str(celllib),str(build/'mapped.v'),source,'examples/TinyRisc/benchmark/tb.v'],build/'compile-wrapper.log')
if 'PASS TinyRisc mapped wrapper' not in run(['vvp',str(build/'trace')],out/'wrapper-traffic.log'):raise RuntimeError('Missing wrapper PASS')
stats=out/'stats.json';stats.write_text(stats.read_text().rstrip()+'\n');cells=json.loads(stats.read_text())['modules']['\\TinyRiscBench']['num_cells_by_type']
routes=[]
for seed in [1,2,3]:
 report=out/f'route-{seed}.json'
 run(['nextpnr-ice40','--hx8k','--package','ct256','--pcf-allow-unconstrained','--json',str(build/'design.json'),'--freq','25','--seed',str(seed),'--report',str(report)],out/f'route-{seed}.log')
 r=json.loads(report.read_text());freq=r['fmax']
 if len(freq)!=1 or next(iter(freq.values()))['achieved']<25:raise RuntimeError('Clock gate failed')
 routes.append({'seed':seed,'fmax_mhz':next(iter(freq.values()))['achieved'],'utilization':r['utilization']})
versions={t:subprocess.run([t,'-V' if t=='yosys' else '--version'],text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,check=True).stdout.strip() for t in ['yosys','nextpnr-ice40']}
summary={'device':'iCE40 HX8K CT256','target_mhz':25,'program':'CountRun/count.asm','tools':versions,'source_sha256':hashlib.sha256(Path(source).read_bytes()).hexdigest(),'wrapper_sha256':hashlib.sha256(Path(wrapper).read_bytes()).hexdigest(),'cells':cells,'routes':routes,'scope':'Structural TinyRisc16 plus debug mux. No feedback-pipeline optimization applies. Automatically assigned pins; no board IO timing budgets or hardware measurements.'}
(out/'results.json').write_text(json.dumps(summary,indent=2)+'\n')
f=[r['fmax_mhz'] for r in routes]
(out/'RESULTS.md').write_text(f'''# TinyRisc16 mapped and routed baseline

Existing CountRun CPU, with every architectural register observable through a
16-bit debug mux. Target: **iCE40 HX8K CT256**, 25 MHz, placement seeds 1, 2, 3.
This is a different device from the HX1K packet test; timing is not directly comparable.

| LUT4 | Flip-flops | Carry cells | Block RAM | Routed core Fmax range |
|---:|---:|---:|---:|---:|
| {cells.get('SB_LUT4',0)} | {sum(v for k,v in cells.items() if k.startswith('SB_DFF'))} | {cells.get('SB_CARRY',0)} | {sum(v for k,v in cells.items() if 'RAM' in k)} | {min(f):.2f}–{max(f):.2f} MHz |

The established CountRun cycle trace passes on the mapped CPU. The mapped wrapper
also matches the source CPU over 1,000 cycles, probing all 16 debug selectors each
cycle under reset, run, step and peripheral controls. The full TinyRisc architecture
regression is a prerequisite of the runner. This is sampled simulation evidence,
not a formal equivalence proof.

TinyRisc uses explicit structural construction and does not call the CIL feedback
scheduler. Therefore the accepted-state optimization does not change this CPU;
these numbers establish a larger-design baseline, not a claimed CPU improvement.
The debug mux is included in the resource counts. The counting ROM image is fixed;
results do not establish costs for every program image.

Pins are assigned automatically, with no board-specific pin or external I/O timing
constraints. Fmax is a routed register-path estimate, not physical FPGA operation.
It is not instructions/second: instruction execution and stalls take multiple clocks.

Reproduce with `bash tests/run-tinyrisc-benchmark.sh` and the same environment as
the packet benchmark. Tool versions and hashes are in `results.json`.
''')
print('PASS TinyRisc benchmark: mapped core trace, mapped debug wrapper, three HX8K routes')
