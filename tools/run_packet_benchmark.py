"""Reproducible, equal-interface/equal-latency iCE40 core comparison."""
from pathlib import Path
import subprocess, os, json, re, hashlib
root=Path(__file__).resolve().parents[1]
os.chdir(root)
out=root/'generated/packet-benchmark';build=root/'build/packet-benchmark'
out.mkdir(parents=True,exist_ok=True);build.mkdir(parents=True,exist_ok=True)
celllib=Path(os.environ['YOSYS_DATDIR'])/'ice40/cells_sim.v'
tb='examples/PacketBenchmark/traffic_tb.v'
def run(args,log):
    p=subprocess.run(args,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True)
    log.write_text("\n".join(line.rstrip() for line in p.stdout.splitlines())+"\n")
    if p.returncode: raise RuntimeError(f'{args[0]} failed; see {log}\n{p.stdout[-2500:]}')
    return p.stdout

def simulate(source,label,mapped=False):
    exe=build/label
    run(['iverilog','-g2012','-s','tb','-o',str(exe)]+([str(celllib)] if mapped else [])+[str(source),tb],build/(label+'-compile.log'))
    trace=run(['vvp',str(exe)],out/(label+'-traffic.log'))
    if 'PASS packet traffic:' not in trace: raise RuntimeError('Missing traffic PASS')
    return next(line for line in trace.splitlines() if line.startswith('PASS packet traffic:'))

results={}
for name,source in [('sharp','generated/packet-benchmark/sharp.v'),('hand','examples/PacketBenchmark/handwritten.v')]:
    trace=simulate(source,name)
    net=build/(name+'.json');mapped=build/(name+'-mapped.v');stats=out/(name+'-stats.json')
    script=f'read_verilog {source}; synth_ice40 -top h_PacketChecksum -json {net}; check -assert; tee -o {stats} stat -json; write_verilog -noattr -noexpr {mapped}'
    run(['yosys','-Q','-T','-p',script],out/(name+'-synthesis.log'))
    mapped_trace=simulate(mapped,name+'-mapped',True)
    if trace!=mapped_trace: raise RuntimeError('Mapped traffic differs')
    stats.write_text(stats.read_text().rstrip()+'\n')
    cells=json.loads(stats.read_text())['modules']['\\h_PacketChecksum']['num_cells_by_type']
    routes=[]
    for seed in [1,2,3]:
        report=out/f'{name}-route-{seed}.json'
        run(['nextpnr-ice40','--hx1k','--package','vq100','--pcf-allow-unconstrained','--json',str(net),'--freq','25','--seed',str(seed),'--report',str(report)],out/f'{name}-route-{seed}.log')
        r=json.loads(report.read_text());freq=r['fmax']
        if len(freq)!=1: raise RuntimeError('Expected one constrained clock')
        timing=next(iter(freq.values()))
        if timing['achieved']<25: raise RuntimeError('25 MHz timing gate failed')
        routes.append({'seed':seed,'fmax_mhz':timing['achieved'],'utilization':r['utilization']})
    results[name]={'source_sha256':hashlib.sha256(Path(source).read_bytes()).hexdigest(),'cells':cells,'routes':routes,'traffic':trace}
    print(name,trace,flush=True)
if results['sharp']['traffic']!=results['hand']['traffic']: raise RuntimeError('Reference and generated traffic differ')
versions={t:subprocess.run([t,'-V' if t=='yosys' else '--version'],stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,check=True).stdout.strip() for t in ['yosys','nextpnr-ice40']}
summary={'device':'iCE40 HX1K','package':'vq100','target_mhz':25,'seeds':[1,2,3],'ports':'automatically assigned pins; core clock timing, no board pin or I/O timing constraints','latency_cycles':1,'initiation_interval_cycles':1,'payload_bits':16,'tools':versions,'testbench_sha256':hashlib.sha256(Path(tb).read_bytes()).hexdigest(),'results':results}
(out/'results.json').write_text(json.dumps(summary,indent=2)+'\n')
lines=['# Packet checksum: measured core comparison','','Device: iCE40 HX1K, VQ100; 25 MHz target; placement seeds 1, 2, 3.','Automatically assigned pins: routed core estimates, not board measurements.','','| Implementation | LUT4 | Flip-flops | Packed logic cells | Carry cells | RAM | DSP | Routed Fmax range (MHz) |','|---|---:|---:|---:|---:|---:|---:|---:|']
for name,r in results.items():
    c=r['cells'];fs=[p['fmax_mhz'] for p in r['routes']]
    lines.append(f"| {'SharpHDL CIL' if name=='sharp' else 'Handwritten RTL'} | {c.get('SB_LUT4',0)} | {sum(v for k,v in c.items() if k.startswith('SB_DFF'))} | {min(p['utilization']['ICESTORM_LC']['used'] for p in r['routes'])} | {c.get('SB_CARRY',0)} | {sum(v for k,v in c.items() if 'RAM' in k)} | {c.get('SB_MAC16',0)} | {min(fs):.2f}–{max(fs):.2f} |")
lines+=['','Both versions: latency **1 clock**, sustained **1 word/clock**. At the tested','25 MHz constraint this is **25 million 16-bit payload words/s = 400 Mbit/s**,','before transport overhead; it is calculated throughput, not a measured link rate.','','The independent traffic oracle passes for both RTL versions and both mapped','iCE40 cell netlists:','','```text',results['sharp']['traffic'],'```','','Fmax is nextpnr static timing for the routed register paths, not a measured','silicon limit. Inputs and outputs have no physical I/O delay budget. A board','wrapper and endpoint timing constraints are required before a system-rate claim.','','Tool versions:','```text',versions['yosys'],versions['nextpnr-ice40'],'```','']
(out/'RESULTS.md').write_text('\n'.join(lines))
print('PASS packet benchmark: RTL, mapped simulation, six place-and-route runs; reports generated.')
