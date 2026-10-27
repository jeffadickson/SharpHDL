#!/usr/bin/env bash
# Independent local HDL checks; does not enable or dispatch CI.
set -euo pipefail
repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
test -d "$repo/tools/SharpHDL.Z80Demo" && test -d /tmp
for tool in dotnet iverilog verilator yosys; do
  command -v "$tool" >/dev/null || { printf 'Missing required tool: %s\n' "$tool" >&2; exit 2; }
done
output=$(mktemp -d /tmp/sharphdl-z80-XXXXXX)
dotnet run --project "$repo/tools/SharpHDL.Z80Demo" -c Release --artifacts-path "$output/artifacts" -- "$output/vectors"
iverilog -g2012 -s tb -o "$output/z80" "$output/vectors/Z80TestCore.v" "$output/vectors/Z80TestCore_tb.sv"
(cd "$output/vectors" && "$output/z80")
verilator --binary --timing -j 4 -Wno-fatal --top-module tb --Mdir "$output/obj" "$output/vectors/Z80TestCore.v" "$output/vectors/Z80TestCore_tb.sv" > "$output/verilator-build.log" 2>&1
(cd "$output/vectors" && "$output/obj/Vtb")
yosys -Q -T -p "read_verilog $output/vectors/Z80TestCore.v; hierarchy -check -top h_Z80TestCore; proc; opt; check -assert; stat" > "$output/yosys.log" 2>&1
bash "$repo/tools/run-z80-protocol.sh" "$output/vectors"
printf 'PASS Z80 generic synthesis; artifacts retained at %s\n' "$output"
