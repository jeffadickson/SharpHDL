#!/usr/bin/env bash
# Local independent HDL replay with native, isolated Linux build artifacts.
set -euo pipefail
repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
test -d "$repo/tools/SharpHDL.AxiFirRtlDemo" && test -d /tmp
output=$(mktemp -d /tmp/sharphdl-rtl-XXXXXX)
dotnet run --project "$repo/tools/SharpHDL.AxiFirRtlDemo" -c Release --artifacts-path "$output/artifacts" -- --suite "$output/vectors"
for design in ScheduledTable1 ScheduledTable4 ScheduledTable8 ScheduledCilLoop ScheduledCilShifts ScheduledBanksAuto ScheduledBanksXilinxBlock; do
  iverilog -g2012 -s tb -o "$output/$design" "$output/vectors/$design.v" "$output/vectors/${design}_tb.sv"
  (cd "$output/vectors" && "$output/$design")
  verilator --binary --timing -j 4 -Wno-fatal --top-module tb --Mdir "$output/$design-obj" "$output/vectors/$design.v" "$output/vectors/${design}_tb.sv" > "$output/$design-verilator-build.log" 2>&1
  (cd "$output/vectors" && "$output/$design-obj/Vtb")
done
printf 'Independent replay artifacts retained at %s\n' "$output"
