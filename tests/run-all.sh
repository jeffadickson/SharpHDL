#!/usr/bin/env bash
# Complete software/RTL/example refresh. Requires .NET 8, Python, Node,
# Icarus, Yosys, nextpnr-ice40 and icepack. Does not program a board.
set -euo pipefail
cd "$(dirname "$0")/.."
: "${DOTNET_ROOT:?Set DOTNET_ROOT to the .NET 8 SDK directory}"
: "${YOSYS_DATDIR:?Set YOSYS_DATDIR to the Yosys share directory}"
export SDK_ROOT="$DOTNET_ROOT"
# Fail before regenerating anything if required tools are missing.
for tool in python3 node iverilog vvp yosys nextpnr-ice40 icepack; do
  command -v "$tool" >/dev/null || { echo "Missing required tool: $tool" >&2; exit 1; }
done
[[ -x "$DOTNET_ROOT/dotnet" ]] || { echo 'DOTNET_ROOT does not contain an executable dotnet' >&2; exit 1; }
[[ -f "$YOSYS_DATDIR/ice40/cells_sim.v" ]] || { echo 'YOSYS_DATDIR lacks ice40/cells_sim.v' >&2; exit 1; }
nextpnr_help=$(nextpnr-ice40 --help 2>&1)
[[ "$nextpnr_help" == *--hx1k* ]] || { echo 'nextpnr-ice40 lacks HX1K device support; check its chip database installation' >&2; exit 1; }
python3 -c 'import docx'
node -e 'const fs=require("fs"),{chromium}=require("playwright"); const p=process.env.CHROME_BIN||chromium.executablePath(); if(!fs.existsSync(p))throw Error("Chromium missing: set CHROME_BIN or install Playwright chromium");'
mkdir -p build/audit
: > build/audit/stages.log
run() { echo "Running $1"; local name="$1"; shift; "$@" > "build/audit/$name.log" 2>&1 || { tail -40 "build/audit/$name.log"; echo "FAIL $name" >> build/audit/stages.log; return 1; }; echo "PASS $name" >> build/audit/stages.log; }
run core bash tests/run.sh
for suite in behavioral advanced fsharp three-languages learning tinyrisc; do
  run "$suite" bash "tests/run-$suite.sh"
done
run naming python3 tests/check-net-naming.py
run browser-graph node tests/check-learning-browser.js
run verisim python3 tests/check-verisim.py
run wasm-workbook node tests/check-wasm-workbook.js --browser
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
"${CODEX_PRIMARY_RUNTIME_PYTHON:-python3}" tools/create_classroom_proposal.py docs/SharpHDL_Classroom_Proposal.docx
run generated-docs python3 tests/check-generated-docs.py
cp generated/behavioral/Ram256-synthesis.log generated/behavioral/ram-synthesis.log
cp build/audit/core.log generated/behavioral/core-regression.txt
cp build/audit/synthesize-behavioral.log generated/behavioral/mapped-verification.txt
cp build/audit/behavioral.log generated/behavioral/rtl-verification.txt
cp build/audit/three-languages.log generated/three-languages/rtl-verification.log
cp build/audit/tinyrisc.log generated/tinyrisc/test.log
mkdir -p generated/audit
printf 'PASS expected classroom prediction rejection\nPASS complete automated run\n' >> build/audit/stages.log
cp build/audit/stages.log generated/audit/run-all.txt
echo 'PASS all automated suites. Render and visually review the rebuilt workbook before release.'
