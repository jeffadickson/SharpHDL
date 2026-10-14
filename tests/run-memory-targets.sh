#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
: "${DOTNET_ROOT:?Set DOTNET_ROOT to the .NET 8 SDK}"
mkdir -p build/memory-targets generated/memory-targets
for mode in debug optimized; do
 flags=(); if [[ "$mode" == debug ]]; then flags+=(--debug); fi
 python3 tools/build_test_suite.py "$DOTNET_ROOT" core "${flags[@]}"
 "$DOTNET_ROOT/dotnet" build/core/core.dll --memory-targets
 for bench in generated/memory-targets/*_tb.v; do
  iverilog -g2012 -s tb -o build/memory-targets/replay "${bench%_tb.v}.v" "$bench"
  vvp build/memory-targets/replay
 done
 echo "PASS memory targets and native execution $mode"
done
