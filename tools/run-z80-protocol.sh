#!/usr/bin/env bash
# Two-state temporal induction over the actual generated request signals.
set -euo pipefail
vectors=${1:?Supply an existing Z80 replay-vector directory}
test -d "$vectors" && test -f "$vectors/Z80BusFormal.v" && test -d /tmp
output=$(mktemp -d /tmp/sharphdl-z80-protocol-XXXXXX)
timeout 180 yosys -Q -T -p "read_verilog -formal $vectors/Z80TestCore.v $vectors/Z80BusFormal.v; prep -top Z80BusFormal -flatten; opt; dffunmap; opt_clean; sat -verify -prove-asserts -tempinduct -seq 2 -maxsteps 12" > "$output/proof.log" 2>&1
# A deliberately inverted valid assertion must fail from a reset trace. This
# checks that the stall contract is reachable rather than vacuously disabled.
grep -q 'assert(s_bus_valid);' "$vectors/Z80BusFormal.v"
sed 's/assert(s_bus_valid);/assert(!s_bus_valid);/' "$vectors/Z80BusFormal.v" > "$output/negative.v"
if timeout 180 yosys -Q -T -p "read_verilog -formal $vectors/Z80TestCore.v $output/negative.v; prep -top Z80BusFormal -flatten; opt; dffunmap; opt_clean; sat -verify -prove-asserts -seq 6 -set-at 1 rst 1 -dump_vcd $output/negative.vcd" > "$output/negative.log" 2>&1; then
  echo 'Negative control unexpectedly proved' >&2; exit 1
fi
grep -q 'proof did fail' "$output/negative.log" || { echo 'Negative control failed for an unexpected reason' >&2; exit 1; }
printf 'PASS Z80 stalled-request temporal induction; artifacts retained at %s\n' "$output"
