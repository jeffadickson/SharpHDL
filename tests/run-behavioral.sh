#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
: "${DOTNET_ROOT:?Set DOTNET_ROOT to a .NET 8 SDK installation}"
python3 examples/Behavioral/build.py "$DOTNET_ROOT"
"$DOTNET_ROOT/dotnet" build/behavioral/Behavioral.dll generated/behavioral
python3 tests/check-behavioral-shapes.py
mkdir -p build/behavioral-check
for bench in generated/behavioral/*_tb.v; do
  stem="${bench%_tb.v}"
  iverilog -g2012 -s tb -o build/behavioral-check/test "$stem.v" "$bench"
  vvp build/behavioral-check/test
done
