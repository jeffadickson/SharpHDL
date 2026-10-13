#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
yosys -Q -T -p 'read_verilog generated/tinyrisc/CountRun.v; hierarchy -check -top h_CountRun; proc -norom; opt; memory; opt; check -assert; stat; write_verilog -noattr generated/tinyrisc/CountRun_synth.v' > generated/tinyrisc/synthesis.log
iverilog -g2012 -s tb -o build/tinyrisc/synth generated/tinyrisc/CountRun_synth.v generated/tinyrisc/CountRun_tb.v
vvp build/tinyrisc/synth > generated/tinyrisc/synthesis-test.log
