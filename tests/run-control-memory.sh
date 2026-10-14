#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
: "${SDK_ROOT:?Set SDK_ROOT to the .NET 8 SDK directory}"
compile() {
  iverilog -g2012 -Wall -Wno-timescale -s tb -o build/control-memory/test.vvp "$@" 2> build/control-memory/compile.log
  if [[ -s build/control-memory/compile.log ]]; then cat build/control-memory/compile.log; exit 1; fi
  vvp build/control-memory/test.vvp
}
for configuration in debug optimized; do
  flags=();if [[ "$configuration" == debug ]]; then flags+=(--debug); fi
  python3 tools/build_test_suite.py "$SDK_ROOT" core "${flags[@]}"
  "$SDK_ROOT/dotnet" build/core/core.dll --control-memory
  for bench in build/control-memory/*_tb.v; do
    stem="$(basename "${bench%_tb.v}")"
    compile "generated/control-memory/$stem.v" "$bench"
  done
  compile generated/control-memory/CaptureInstrument.v generated/control-memory/CaptureInstrument_axi.v examples/CaptureInstrument/tb.v
done
