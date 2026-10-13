#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
mkdir -p build/goboard
# Run examples/ThreeLanguages/build.py and its executable first to elaborate.
yosys -Q -p 'read_verilog generated/three-languages/ClassroomCore.v boards/GoBoard/classroom_top.v; synth_ice40 -top classroom_top -json build/goboard/classroom.json' > build/goboard/synthesis.log
nextpnr-ice40 --hx1k --package vq100 --json build/goboard/classroom.json --pcf boards/GoBoard/classroom.pcf --freq 25 --asc build/goboard/classroom.asc > build/goboard/place-route.log 2>&1
icepack build/goboard/classroom.asc build/goboard/classroom.bin
printf 'Built build/goboard/classroom.bin. Programming the board is a separate teacher step.\n'
