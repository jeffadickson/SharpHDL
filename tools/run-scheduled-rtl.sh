#!/usr/bin/env bash
# Local independent HDL replay with native, isolated Linux build artifacts.
set -euo pipefail
repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
test -d "$repo/tools/SharpHDL.AxiFirRtlDemo" && test -d /tmp
output=$(mktemp -d /tmp/sharphdl-rtl-XXXXXX)
dotnet run --project "$repo/tools/SharpHDL.AxiFirRtlDemo" -c Release --artifacts-path "$output/artifacts" -- --suite "$output/vectors"
for design in ScheduledTable1 ScheduledTable4 ScheduledTable8 ScheduledCilLoop ScheduledCilShifts ScheduledRawCilShifts ScheduledBanksAuto ScheduledBanksXilinxBlock; do
  iverilog -g2012 -s tb -o "$output/$design" "$output/vectors/$design.v" "$output/vectors/${design}_tb.sv"
  (cd "$output/vectors" && "$output/$design")
  verilator --binary --timing -j 4 -Wno-fatal --top-module tb --Mdir "$output/$design-obj" "$output/vectors/$design.v" "$output/vectors/${design}_tb.sv" > "$output/$design-verilator-build.log" 2>&1
  (cd "$output/vectors" && "$output/$design-obj/Vtb")
done
for design in ScheduledCilShifts ScheduledRawCilShifts; do
  yosys -Q -T -p "read_verilog $output/vectors/$design.v; hierarchy -check -top h_$design; proc; opt; check -assert; stat" > "$output/$design-yosys.log" 2>&1
  printf 'PASS generic synthesis %s\n' "$design"
done
printf 'Independent replay artifacts retained at %s\n' "$output"
