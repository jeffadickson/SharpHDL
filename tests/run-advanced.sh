#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
: "${DOTNET_ROOT:?Set DOTNET_ROOT to a .NET 8 SDK installation}"
python3 examples/AdvancedBehavioral/build.py "$DOTNET_ROOT"
"$DOTNET_ROOT/dotnet" build/advanced/Advanced.dll
mkdir -p build/advanced-check
for bench in generated/advanced/*_tb.v; do
  stem="${bench%_tb.v}"
  iverilog -g2012 -s tb -o build/advanced-check/test "$stem.v" "$bench"
  vvp build/advanced-check/test
done
