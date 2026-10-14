#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
: "${SDK_ROOT:?Set SDK_ROOT to the .NET 8 SDK directory}"
for configuration in debug optimized; do
  flags=()
  if [[ "$configuration" == debug ]]; then flags+=(--debug); fi
  python3 tools/build_test_suite.py "$SDK_ROOT" core "${flags[@]}"
  "$SDK_ROOT/dotnet" build/core/core.dll --frame-reduction
  iverilog -g2012 -Wall -Wno-timescale -s tb -o build/frame-reduction/replay.vvp generated/frame-reduction/statistics.v build/frame-reduction/statistics_tb.v 2> build/frame-reduction/compile.log
  if [[ -s build/frame-reduction/compile.log ]]; then cat build/frame-reduction/compile.log; exit 1; fi
  vvp build/frame-reduction/replay.vvp
done
