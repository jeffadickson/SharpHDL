#!/usr/bin/env bash
# Core-only comparison: automatically assigned pins, no board pin assignments or board programming.
set -euo pipefail
cd "$(dirname "$0")/.."
: "${DOTNET_ROOT:?Set DOTNET_ROOT to a .NET 8 SDK}"
: "${YOSYS_DATDIR:?Set YOSYS_DATDIR to the Yosys share directory}"
for tool in iverilog vvp yosys nextpnr-ice40; do command -v "$tool" >/dev/null; done
mkdir -p build/packet-benchmark generated/packet-benchmark
python3 tools/build_test_suite.py "$DOTNET_ROOT" core
"$DOTNET_ROOT/dotnet" build/core/core.dll --packet-benchmark
python3 tools/run_packet_benchmark.py
