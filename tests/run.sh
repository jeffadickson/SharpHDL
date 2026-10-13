#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p build generated
if [[ -n "${DOTNET_ROOT:-}" ]]; then
  python3 tools/build_test_suite.py "$DOTNET_ROOT" core
  "$DOTNET_ROOT/dotnet" build/core/core.dll
elif command -v dotnet >/dev/null; then
  dotnet run --project tests/Tests.csproj
else
  mcs -r:System.Numerics -out:build/tests.exe src/SharpHDL/*.cs examples/*.cs tests/*.cs
  mono build/tests.exe
fi
for bench in generated/*_tb.v; do
  stem="${bench%_tb.v}"
  name="$(basename "$stem")"
  extra=()
  if [[ "$stem" == *_axi ]]; then extra+=("${stem%_axi}.v"); fi
  iverilog -g2012 -s tb -o "build/$name" "$stem.v" "${extra[@]}" "$bench"
  vvp "build/$name"
done
for wrapper in generated/*_axi.v; do
  stem="${wrapper%_axi.v}"
  name="$(basename "$stem")"
  iverilog -g2012 -s "h_${name}_axi" -o "build/${name}_axi" "$stem.v" "$wrapper"
done
iverilog -g2012 -s h_Counter -o build/counter generated/counter.v

iverilog -g2012 -s tb -o build/axi-names/sim build/axi-names/design.v build/axi-names/tb.v
vvp build/axi-names/sim
