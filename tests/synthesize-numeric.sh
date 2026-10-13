#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p build generated/synthesis
# Explicit case ROMs: proc -norom avoids relying on target memory inference.
for name in SkidBuffer16 QuantizedGain24 Float16_add Float16_mul Float16_div Float16_sqrt Float16_log2 Float16_cos; do
  yosys -Q -T -p "read_verilog generated/$name.v; hierarchy -check -top h_$name; proc -norom; opt; check -assert; stat; write_verilog -noattr generated/synthesis/${name}_rtl.v" > "generated/synthesis/$name.log"
  iverilog -g2012 -s tb -o "build/${name}_synth" "generated/synthesis/${name}_rtl.v" "generated/${name}_tb.v"
  vvp "build/${name}_synth"
done
