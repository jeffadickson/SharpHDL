#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
: "${SDK_ROOT:?Set SDK_ROOT to the .NET 8 SDK directory}"
for configuration in debug optimized; do
  flags=()
  if [[ "$configuration" == debug ]]; then flags+=(--debug); fi
  python3 tools/build_test_suite.py "$SDK_ROOT" core "${flags[@]}"
  "$SDK_ROOT/dotnet" build/core/core.dll --axis-interop
  iverilog -g2012 -Wall -Wno-timescale -s tb -o build/core/mixed-axis.vvp -f generated/mixed-axis/sources.f examples/MixedAxis/tb.v 2> build/core/mixed-axis-compile.log
  if [[ -s build/core/mixed-axis-compile.log ]]; then
    cat build/core/mixed-axis-compile.log
    echo "FAIL: mixed RTL compiler diagnostics require review"
    exit 1
  fi
  vvp build/core/mixed-axis.vvp
done
