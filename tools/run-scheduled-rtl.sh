#!/usr/bin/env bash
# Local independent HDL replay with native, isolated Linux build artifacts.
set -euo pipefail
repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
test -d "$repo/tools/SharpHDL.AxiFirRtlDemo" && test -d /tmp
output=$(mktemp -d /tmp/sharphdl-rtl-XXXXXX)
test -d "$output"
mkdir -- "$output/runs"
dotnet build "$repo/tools/SharpHDL.ToolCli" -c Release --artifacts-path "$output/runner-artifacts" -m:1 -nr:false -p:UseSharedCompilation=false
runner="$output/runner-artifacts/bin/SharpHDL.ToolCli/release/SharpHDL.ToolCli.dll"
sequence=0
run_tool() {
  sequence=$((sequence+1))
  dotnet "$runner" run "$output/runs/$sequence" "$output/vectors" 120000 "$@"
  cat "$output/runs/$sequence/stdout.bin"
}
dotnet run --project "$repo/tools/SharpHDL.AxiFirRtlDemo" -c Release --artifacts-path "$output/artifacts" -- --suite "$output/vectors"
for design in ScheduledTable1 ScheduledTable4 ScheduledTable8 ScheduledCilLoop ScheduledCilShifts ScheduledRawCilShifts ScheduledBanksAuto ScheduledBanksXilinxBlock ScheduledPlannedBanks ScheduledIterativeBanks AxiConformanceLite AxiConformanceStream; do
  run_tool iverilog -g2012 -s tb -o "$output/$design" "$output/vectors/$design.v" "$output/vectors/${design}_tb.sv"
  run_tool vvp "$output/$design"
  run_tool verilator --binary --timing -j 2 -Wno-fatal --top-module tb --Mdir "$output/$design-obj" "$output/vectors/$design.v" "$output/vectors/${design}_tb.sv"
  run_tool "$output/$design-obj/Vtb"
done
for design in ScheduledCilShifts ScheduledRawCilShifts ScheduledPlannedBanks ScheduledIterativeBanks AxiConformanceLite AxiConformanceStream; do
  run_tool yosys -Q -T -p "read_verilog $output/vectors/$design.v; hierarchy -check -top h_$design; proc; opt; check -assert; stat"
  printf 'PASS generic synthesis %s\n' "$design"
done
printf 'Independent replay artifacts retained at %s\n' "$output"
