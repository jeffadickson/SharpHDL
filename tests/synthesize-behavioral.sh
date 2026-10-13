#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
: "${YOSYS_DATDIR:?Set YOSYS_DATDIR to the Yosys share directory}"
mkdir -p build/behavioral-check
for name in CilCsMinimum CilCsCheckedAdd CilCsLoop12 CilCsHalf CilMixedPipeline Ram256; do
  yosys -Q -T -q -l "generated/behavioral/$name-synthesis.log" -p "read_verilog generated/behavioral/$name.v; hierarchy -top h_$name; proc; opt; memory_collect; stat; synth_ice40 -top h_$name; stat; write_verilog -noattr build/behavioral-check/$name-mapped.v"
  iverilog -g2012 -s tb -o build/behavioral-check/mapped "build/behavioral-check/$name-mapped.v" "generated/behavioral/${name}_tb.v" "$YOSYS_DATDIR/ice40/cells_sim.v"
  vvp build/behavioral-check/mapped
done
