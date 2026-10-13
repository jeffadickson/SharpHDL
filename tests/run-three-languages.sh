#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
: "${SDK_ROOT:?Set SDK_ROOT to your .NET 8 SDK installation directory}"
python3 examples/ThreeLanguages/build.py "$SDK_ROOT"
"$SDK_ROOT/dotnet" build/three-languages/ThreeLanguages.dll generated/three-languages
for bench in generated/three-languages/*_tb.v; do
  stem="${bench%_tb.v}"
  iverilog -g2012 -s tb -o "build/$(basename "$stem")" "$stem.v" "$bench"
  vvp "build/$(basename "$stem")"
done
