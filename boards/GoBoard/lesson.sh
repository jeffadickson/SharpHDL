#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
: "${SDK_ROOT:?Set SDK_ROOT to the .NET 8 SDK installation directory}"
expected_addend="${1:?Supply the predicted addend from 0 to 15}"
python3 examples/ThreeLanguages/build.py "$SDK_ROOT"
"$SDK_ROOT/dotnet" build/three-languages/ThreeLanguages.dll --classroom "$expected_addend" generated/three-languages
iverilog -g2012 -s tb -o build/classroom_lesson generated/three-languages/ClassroomCore_fast.v generated/three-languages/ClassroomCore_fast_tb.v
vvp build/classroom_lesson
boards/GoBoard/build.sh
