#!/usr/bin/env python3
"""Select a multiplier architecture using measured FPGA implementation evidence.

Input JSON:
{"device":"xcku...", "results":[
 {"architecture":"Operator","lut":123,"dsp":2,"fmax_mhz":250,
  "latency":3,"initiation_interval":1}, ...]}
Missing metrics are NOT treated as zero. Generic Yosys gate counts do not qualify.
"""
import argparse
import json
import math
from pathlib import Path

def select_architecture(report, objective, target_mhz=None, max_lut=None, max_dsp=None):
    if not report.get("device") or not isinstance(report.get("results"), list):
        raise ValueError("Target device and measured results are required")
    candidates=[]
    fields=("lut","dsp","fmax_mhz","latency","initiation_interval")
    for row in report["results"]:
        if not all(field in row and isinstance(row[field], (int,float))
                   and math.isfinite(row[field]) for field in fields):
            continue
        if row["lut"]<0 or row["dsp"]<0 or row["fmax_mhz"]<=0 or row["latency"]<0 or row["initiation_interval"]<1:
            continue
        if target_mhz is not None and row["fmax_mhz"]<target_mhz:
            continue
        if max_lut is not None and row["lut"]>max_lut:
            continue
        if max_dsp is not None and row["dsp"]>max_dsp:
            continue
        candidates.append(row)
    if not candidates:
        raise ValueError("No measured architecture satisfies the constraints")
    if objective=="area":
        key=lambda r:(r["lut"],r["dsp"],r["latency"],r["architecture"])
    elif objective=="throughput":
        key=lambda r:(-r["fmax_mhz"]/r["initiation_interval"],r["lut"],r["architecture"])
    elif objective=="latency":
        key=lambda r:(r["latency"]/r["fmax_mhz"],r["lut"],r["architecture"])
    else:
        raise ValueError("Unknown objective")
    return min(candidates,key=key)

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("report",type=Path)
    parser.add_argument("--objective",choices=("area","throughput","latency"),required=True)
    parser.add_argument("--target-mhz",type=float)
    parser.add_argument("--max-lut",type=int)
    parser.add_argument("--max-dsp",type=int)
    args=parser.parse_args()
    report=json.loads(args.report.read_text())
    winner=select_architecture(report,args.objective,args.target_mhz,args.max_lut,args.max_dsp)
    print(json.dumps({"device":report["device"],"objective":args.objective,
                      "selected":winner},indent=2))

if __name__=="__main__":
    main()
