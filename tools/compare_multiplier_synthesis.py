#!/usr/bin/env python3
"""Compare generic Yosys netlist cell counts; NOT FPGA DSP mapping or Fmax."""
import collections
import json
from pathlib import Path
import subprocess
import sys

root = Path(__file__).resolve().parents[1]
out = root / "generated" / "multipliers"
out.mkdir(parents=True, exist_ok=True)
architectures = ["Operator", "ShiftAdd", "BoothRadix4", "WallaceTree", "DspPreferred"]
rows = []
for architecture in architectures:
    top = "h_Mul" + architecture
    rtl = out / ("Mul" + architecture + ".v")
    netlist = out / ("Mul" + architecture + ".yosys.json")
    if not rtl.exists():
        raise SystemExit(f"Missing {rtl}; run make multiplier-rtl first")
    script = f"read_verilog {rtl}; synth -top {top}; write_json {netlist}"
    result = subprocess.run(["yosys", "-Q", "-T", "-p", script],
                            text=True, capture_output=True)
    (out / (architecture + ".yosys.log")).write_text(result.stdout + result.stderr)
    if result.returncode:
        raise SystemExit(f"Yosys failed for {architecture}: {result.stderr[-1500:]}")
    cells = json.loads(netlist.read_text())["modules"][top]["cells"]
    counts = collections.Counter(cell["type"] for cell in cells.values())
    rows.append({"architecture": architecture, "generic_cells": len(cells),
                 "cell_types": dict(sorted(counts.items()))})
report = {"measurement": "generic Yosys synth gate count; not FPGA resources or timing",
          "architectures": rows}
(out / "comparison.json").write_text(json.dumps(report, indent=2) + "\n")
for row in rows:
    print(f'{row["architecture"]:16} {row["generic_cells"]:8} generic cells')
print("Wrote", out / "comparison.json")
