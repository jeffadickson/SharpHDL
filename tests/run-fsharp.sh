#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
# Set SDK_ROOT to use the direct compiler route instead of dotnet run.
if [[ -n "${SDK_ROOT:-}" ]]; then
  python3 examples/FSharp/build_direct.py "$SDK_ROOT"
  "$SDK_ROOT/dotnet" build/fsharp/FSharpExamples.dll generated/fsharp
else
  dotnet run --project examples/FSharp/Examples.fsproj -- generated/fsharp
fi
mkdir -p build
for bench in generated/fsharp/*_tb.v; do
  stem="${bench%_tb.v}"
  name="$(basename "$stem")"
  iverilog -g2012 -s tb -o "build/$name" "$stem.v" "$bench"
  vvp "build/$name"
done
