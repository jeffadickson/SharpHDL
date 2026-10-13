#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p build generated/learning
mcs -r:System.Numerics -out:build/learning.exe src/SharpHDL/*.cs examples/LearningLab/*.cs
mono build/learning.exe
for name in FixedShifts DrillController; do
  iverilog -g2012 -s tb -o build/learning-rtl "generated/learning/$name.v" "generated/learning/${name}_tb.v"
  vvp build/learning-rtl
done
# Run tests/run-behavioral.sh first to refresh the CIL loop graphs.
python3 tools/build_learning_html.py
node tests/check-learning-browser.js
