#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p build generated/learning
if [[ -n "${DOTNET_ROOT:-}" ]]; then
  python3 tools/build_test_suite.py "$DOTNET_ROOT" learning
  "$DOTNET_ROOT/dotnet" build/learning/learning.dll
else
  mcs -r:System.Numerics -out:build/learning.exe src/SharpHDL/*.cs examples/LearningLab/*.cs
  mono build/learning.exe
fi
for name in FixedShifts DrillController ShotCounter ShotCounterDouble; do
  iverilog -g2012 -s tb -o build/learning-rtl "generated/learning/$name.v" "generated/learning/${name}_tb.v"
  vvp build/learning-rtl
done
# Run tests/run-behavioral.sh first to refresh the CIL loop graphs.
python3 tools/build_verisim.py
python3 tools/build_wasm_lab.py
python3 tools/build_learning_html.py
node tests/check-learning-browser.js
