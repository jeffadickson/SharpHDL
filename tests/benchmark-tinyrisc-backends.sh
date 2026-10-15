#!/usr/bin/env bash
# Full TinyRisc correctness first; separately report measured simulator timings.
set -euo pipefail
cd "$(dirname "$0")/.."
: "${DOTNET_ROOT:?Set DOTNET_ROOT to a .NET SDK directory}"
bash tests/run-tinyrisc.sh
"$DOTNET_ROOT/dotnet" build/tinyrisc/TinyRisc.dll --benchmark-compiled-tinyrisc | tee generated/tinyrisc/compiled-benchmark.log
# Verilator is a third backend and independent RTL comparator. Its separate
# wall-clock metric includes testbench and scheduler overhead; not cycles/sec parity.
if command -v verilator >/dev/null; then
  verilator --version | tee generated/tinyrisc/verilator-version.txt
  verilator --binary --timing -Wno-fatal --top-module tb_tinyrisc_uart \
    --Mdir build/tinyrisc/verilator_hello \
    generated/tinyrisc/HelloWorld.v examples/TinyRisc/hello_uart_top.v \
    examples/TinyRisc/hello_uart_tb.v
  /usr/bin/time -p build/tinyrisc/verilator_hello/Vtb_tinyrisc_uart \
    2>&1 | tee generated/tinyrisc/verilator-hello.log
  verilator --binary --timing -Wno-fatal --top-module tb_compiled_benchmark \
    --Mdir build/tinyrisc/verilator_cpu_bench \
    generated/tinyrisc/BenchmarkTinyRisc.v examples/TinyRisc/tinyrisc_benchmark_tb.sv
  /usr/bin/time -p build/tinyrisc/verilator_cpu_bench/Vtb_compiled_benchmark \
    2>&1 | tee generated/tinyrisc/verilator-cpu-benchmark.log
else
  echo "SKIP Verilator (not installed); no Verilator performance comparison claimed"
fi
