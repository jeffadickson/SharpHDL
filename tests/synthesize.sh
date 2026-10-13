#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p build generated/synthesis
for core in PacketRouter Cordic RomReciprocalMantissa PolynomialLog2Mantissa CrcPacket Arbiter; do
  yosys -Q -T -p "read_verilog generated/$core.v; hierarchy -check -top h_$core; proc -norom; opt; memory; opt; check -assert; stat; write_verilog -noattr generated/synthesis/${core}_rtl.v" > "generated/synthesis/$core.log"
  iverilog -g2012 -s tb -o "build/${core}_synth" "generated/synthesis/${core}_rtl.v" "generated/${core}_tb.v"
  vvp "build/${core}_synth"
done
