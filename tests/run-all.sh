#!/usr/bin/env bash
# Complete software/RTL/example refresh. Requires .NET 8, Python, Node,
# Icarus, Yosys, nextpnr-ice40 and icepack. Does not program a board.
set -euo pipefail
cd "$(dirname "$0")/.."
: "${DOTNET_ROOT:?Set DOTNET_ROOT to the .NET 8 SDK directory}"
: "${YOSYS_DATDIR:?Set YOSYS_DATDIR to the Yosys share directory}"
export SDK_ROOT="$DOTNET_ROOT"
mkdir -p build/audit
run() { echo "Running $1"; local name="$1"; shift; "$@" > "build/audit/$name.log" 2>&1 || { tail -40 "build/audit/$name.log"; return 1; }; }
run core bash tests/run.sh
for suite in behavioral advanced fsharp three-languages learning tinyrisc; do
  run "$suite" bash "tests/run-$suite.sh"
done
run naming python3 tests/check-net-naming.py
run browser-graph node tests/check-learning-browser.js
run verisim python3 tests/check-verisim.py
for script in synthesize synthesize-numeric synthesize-multiply synthesize-divide synthesize-behavioral synthesize-examples synthesize-tinyrisc; do
  run "$script" bash "tests/$script.sh"
done
run comparison bash tests/run-comparison.sh
run board bash boards/GoBoard/lesson.sh 2
iverilog -g2012 -s tb -o build/goboard_shell generated/three-languages/ClassroomCore.v boards/GoBoard/classroom_top.v boards/GoBoard/classroom_top_tb.v
run board-shell vvp build/goboard_shell
if bash boards/GoBoard/lesson.sh 3 > build/audit/board-negative.log 2>&1; then
  echo 'FAIL: wrong classroom answer was accepted'; exit 1
fi
# Ensure rejection was the intended prediction failure, not a failed tool launch.
grep -Fq 'System.Exception: Classroom right digit' build/audit/board-negative.log
(
  cd docs/tinyrisc
  iverilog -g2012 -s tb -o ../../build/tinyrisc/demo ../../generated/tinyrisc/CountRun.v demo_tb.v
  vvp ../../build/tinyrisc/demo > demo-verification.txt
)
python3 tools/build_tinyrisc_demo.py
run tinyrisc-demo node tests/check-tinyrisc-demo.js
# CHROME_BIN and optional VERISIM_ENGINE_DIR configure real browser/WASM testing.
run tinyrisc-browser node tests/check-tinyrisc-browser.js
python3 tools/create_classroom_proposal.py docs/SharpHDL_Classroom_Proposal.docx
run generated-docs python3 tests/check-generated-docs.py
cp generated/behavioral/Ram256-synthesis.log generated/behavioral/ram-synthesis.log
cp build/audit/core.log generated/behavioral/core-regression.txt
cp build/audit/synthesize-behavioral.log generated/behavioral/mapped-verification.txt
cp build/audit/behavioral.log generated/behavioral/rtl-verification.txt
cp build/audit/three-languages.log generated/three-languages/rtl-verification.log
cp build/audit/tinyrisc.log generated/tinyrisc/test.log
echo 'PASS all automated suites. Render and visually review the rebuilt workbook before release.'
