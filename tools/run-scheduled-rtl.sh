#!/usr/bin/env bash
# Local independent HDL replay with native, isolated Linux build artifacts.
set -euo pipefail
repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
test -d "$repo/tools/SharpHDL.AxiFirRtlDemo" && test -d /tmp
(($#<=1)) || { echo "Usage: $0 [all|narrow|storage|checked|checked-memory|aliases|guarded-rams]" >&2; exit 2; }
case ${1:-all} in
  all) designs=(ScheduledTable1 ScheduledTable4 ScheduledTable8 ScheduledCilLoop ScheduledCilShifts ScheduledRawCilShifts ScheduledBanksAuto ScheduledBanksXilinxBlock ScheduledPlannedBanks ScheduledIterativeBanks AxiConformanceLite AxiConformanceStream ScheduledNarrowI1 ScheduledNarrowU1 ScheduledNarrowI2 ScheduledNarrowU2 ScheduledCilNarrow); synthesis=(ScheduledCilShifts ScheduledRawCilShifts ScheduledPlannedBanks ScheduledIterativeBanks AxiConformanceLite AxiConformanceStream ScheduledNarrowI1 ScheduledNarrowU1 ScheduledNarrowI2 ScheduledNarrowU2 ScheduledCilNarrow);;
  narrow) designs=(ScheduledNarrowI1 ScheduledNarrowU1 ScheduledNarrowI2 ScheduledNarrowU2 ScheduledCilNarrow); synthesis=(ScheduledNarrowI1 ScheduledNarrowU1 ScheduledNarrowI2 ScheduledNarrowU2 ScheduledCilNarrow);;
  storage) designs=(); for type in SByte Byte Int16 UInt16 Char; do for mode in Argument Local; do designs+=("Storage$type$mode"); done; done; synthesis=("${designs[@]}");;
  checked) designs=(ScheduledChecked ScheduledCheckedMemory); synthesis=("${designs[@]}");;
  checked-memory) designs=(ScheduledCheckedMemory); synthesis=(ScheduledCheckedMemory);;
  aliases) designs=(ScheduledAliasUpdate ScheduledAliasNull); synthesis=("${designs[@]}");;
  guarded-rams) designs=(ScheduledGuardedRams); synthesis=(ScheduledGuardedRams);;
  memory-qualification) designs=(MemoryCollisionAuto MemoryCollisionXilinxBlock ScheduledSharedRam ScheduledDistinctRam ScheduledGuardedRams); synthesis=("${designs[@]}");;
  *) echo "Unknown HDL profile" >&2; exit 2;;
esac
if [[ ${1:-all} == all ]]; then
  designs+=(ScheduledChecked ScheduledCheckedMemory); synthesis+=(ScheduledChecked ScheduledCheckedMemory)
  designs+=(ScheduledAliasUpdate ScheduledAliasNull); synthesis+=(ScheduledAliasUpdate ScheduledAliasNull)
  designs+=(ScheduledGuardedRams); synthesis+=(ScheduledGuardedRams)
  designs+=(MemoryCollisionAuto MemoryCollisionXilinxBlock ScheduledSharedRam ScheduledDistinctRam); synthesis+=(MemoryCollisionAuto MemoryCollisionXilinxBlock ScheduledSharedRam ScheduledDistinctRam)
  for type in SByte Byte Int16 UInt16 Char; do
    for mode in Argument Local; do designs+=("Storage$type$mode"); synthesis+=("Storage$type$mode"); done
  done
fi
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
for design in "${designs[@]}"; do
  run_tool iverilog -g2012 -s tb -o "$output/$design" "$output/vectors/$design.v" "$output/vectors/${design}_tb.sv"
  run_tool vvp "$output/$design"
  run_tool verilator --binary --timing -j 2 -Wno-fatal --top-module tb --Mdir "$output/$design-obj" "$output/vectors/$design.v" "$output/vectors/${design}_tb.sv"
  run_tool "$output/$design-obj/Vtb"
done
for design in "${synthesis[@]}"; do
  run_tool yosys -Q -T -p "read_verilog $output/vectors/$design.v; hierarchy -check -top h_$design; proc; opt; check -assert; stat"
  printf 'PASS generic synthesis %s\n' "$design"
done
printf 'Independent replay artifacts retained at %s\n' "$output"
