#!/usr/bin/env bash
# Functional replay after iCE40 cell mapping. Does not claim place/route or board timing.
set -euo pipefail
vectors=${1:?Supply an existing Z80 replay-vector directory}
test -d "$vectors" && test -f "$vectors/Z80TestCore.v" && test -d /tmp
library=/usr/share/yosys/ice40/cells_sim.v
test -f "$library" || { echo 'Missing Yosys iCE40 functional cell library' >&2; exit 2; }
output=$(mktemp -d /tmp/sharphdl-z80-mapped-XXXXXX)
yosys -Q -T -p "read_verilog $vectors/Z80TestCore.v; synth_ice40 -top h_Z80TestCore -json $output/z80.json; check -assert; write_verilog -noattr $output/z80.v; stat" > "$output/yosys.log" 2>&1
iverilog -g2012 -DNO_ICE40_DEFAULT_ASSIGNMENTS -s tb -o "$output/replay" "$library" "$output/z80.v" "$vectors/Z80TestCore_tb.sv"
(cd "$vectors" && "$output/replay")
printf 'PASS Z80 iCE40 mapped functional replay; artifacts retained at %s\n' "$output"
