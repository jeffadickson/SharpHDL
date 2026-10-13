#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p build generated/synthesis
# Override YOSYS_DATDIR for a packaged/local installation.
yosys_data="${YOSYS_DATDIR:-$(yosys-config --datdir)}"
for name in Multiply8UnsignedParallel Multiply8UnsignedBitSerial Multiply8SignedParallel Multiply8SignedBitSerial; do
  yosys -Q -T -p "read_verilog generated/$name.v; hierarchy -check -top h_$name; synth_ice40 -top h_$name; check -assert; stat; write_verilog -noattr generated/synthesis/${name}_ice40.v" > "generated/synthesis/${name}_ice40.log"
  iverilog -g2012 -s tb -o build/multiplier-synth "generated/synthesis/${name}_ice40.v" "$yosys_data/ice40/cells_sim.v" "generated/${name}_tb.v"
  vvp build/multiplier-synth
 done
