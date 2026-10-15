#!/usr/bin/env bash
# Correctness gates first, then a matched workload with high-resolution timers.
set -euo pipefail
cd "$(dirname "$0")/.."
: "${DOTNET_ROOT:?Set DOTNET_ROOT to a .NET SDK directory}"
for tool in python3 iverilog vvp verilator g++ make; do
  command -v "$tool" >/dev/null || { echo "Required benchmark tool missing: $tool" >&2; exit 1; }
done
root="$PWD"
bash tests/run-tinyrisc.sh
python3 tests/test_simulator_benchmark_report.py
conformance="$root/generated/tinyrisc/compiled-conformance"
"$DOTNET_ROOT/dotnet" run --project tools/SharpHDL.SimulatorBench/SharpHDL.SimulatorBench.csproj \
  -c Release -- --conformance "$conformance" | tee generated/tinyrisc/conformance-oracles.log
for bench in "$conformance"/*_tb.v; do
  iverilog -g2012 -s tb_conformance -o "$conformance/test.vvp" "${bench%_tb.v}.v" "$bench"
  vvp "$conformance/test.vvp"
done | tee generated/tinyrisc/conformance-rtl.log
out="$root/generated/tinyrisc/backend-benchmark"
mkdir -p "$out"
edges="${SHARPHDL_BENCH_EDGES:-100000}"
trials="${SHARPHDL_BENCH_TRIALS:-5}"
{
  "$DOTNET_ROOT/dotnet" --version
  verilator --version
  g++ --version
  uname -a
} > "$out/tool-versions.txt"
# Hold JIT tiering fixed so tier promotion is not confused with backend speed.
DOTNET_TieredCompilation=0 "$DOTNET_ROOT/dotnet" run \
  --project tools/SharpHDL.SimulatorBench/SharpHDL.SimulatorBench.csproj -c Release -- \
  --tinyrisc generated/tinyrisc/hello_world.hex "$edges" "$trials" "$out" \
  | tee "$out/dotnet.log"

# Independent serial-pin verification remains separate from CPU clock timing.
verilator --binary --timing -Wno-fatal --top-module tb_tinyrisc_uart \
  --Mdir "$root/build/tinyrisc/verilator_hello" \
  generated/tinyrisc/HelloWorld.v examples/TinyRisc/hello_uart_top.v \
  examples/TinyRisc/hello_uart_tb.v > "$out/verilator-uart-build.log" 2>&1
"$root/build/tinyrisc/verilator_hello/Vtb_tinyrisc_uart" \
  | tee "$out/verilator-uart.log"

# The native driver times only identical clock loops, not compiler/process startup.
verilator --cc --exe --build -j 2 -Wno-fatal --top-module h_BenchmarkTinyRisc \
  --prefix VBenchmarkTinyRisc -CFLAGS "-O3" --Mdir "$out/native" \
  "$out/BenchmarkTinyRisc.v" "$root/examples/TinyRisc/tinyrisc_native_benchmark.cpp" \
  > "$out/verilator-build.log" 2>&1
"$out/native/VBenchmarkTinyRisc" "$edges" "$trials" > "$out/verilator.json"
python3 tools/compare-simulator-benchmarks.py "$out" | tee "$out/results.txt"
