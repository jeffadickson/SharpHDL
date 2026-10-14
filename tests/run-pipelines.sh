#!/usr/bin/env bash
# Check both debug and optimized CIL. Leave reproducible optimized fixtures last.
set -euo pipefail
cd "$(dirname "$0")/.."
: "${DOTNET_ROOT:?Set DOTNET_ROOT to a .NET 8 SDK directory}"
mkdir -p build generated
for mode in debug optimized; do
  flags=()
  if [[ "$mode" == debug ]]; then flags+=(--debug); fi
  python3 tools/build_test_suite.py "$DOTNET_ROOT" core "${flags[@]}"
  "$DOTNET_ROOT/dotnet" build/core/core.dll --pipelines
  "$DOTNET_ROOT/dotnet" build/core/core.dll --feedback
  for bench in generated/scheduled_*_tb.v generated/feedback_*_tb.v; do
    stem="${bench%_tb.v}"
    iverilog -g2012 -s tb -o build/pipeline-test "$stem.v" "$bench"
    vvp build/pipeline-test
  done
  echo "PASS pipeline $mode CIL and RTL"
done
