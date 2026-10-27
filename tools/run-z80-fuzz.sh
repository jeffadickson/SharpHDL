#!/usr/bin/env bash
# Bounded managed-host coverage campaign; not HDL toggle coverage or full ISA fuzzing.
set -euo pipefail
repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
seconds=${1:-60}
[[ "$seconds" =~ ^[0-9]+$ ]] && ((seconds >= 1 && seconds <= 3600)) || { echo 'Budget must be 1..3600 seconds' >&2; exit 2; }
test -d "$repo/tools/SharpHDL.Z80Fuzz" && test -d /tmp
output=$(mktemp -d /tmp/sharphdl-z80-fuzz-XXXXXX)
DOTNET_ROOT=$(dirname -- "$(readlink -f -- "$(command -v dotnet)")")
test -d "$DOTNET_ROOT"
export DOTNET_ROOT DOTNET_ROLL_FORWARD=Major
dotnet publish "$repo/tools/SharpHDL.Z80Fuzz" -c Release --artifacts-path "$output/artifacts" -o "$output/publish"
dotnet tool install SharpFuzz.CommandLine --version 2.2.0 --tool-path "$output/tools"
for assembly in SharpHDL.HLS SharpHDL.Core SharpHDL.Simulation SharpHDL.ScheduledExamples; do
  "$output/tools/sharpfuzz" "$output/publish/$assembly.dll"
done
export AFL_SKIP_BIN_CHECK=1 AFL_NO_UI=1 AFL_SKIP_CPUFREQ=1 AFL_I_DONT_CARE_ABOUT_MISSING_CRASHES=1
afl-fuzz -i "$repo/tools/SharpHDL.Z80Fuzz/corpus" -o "$output/findings" -V "$seconds" -s 261027 -t 10000 -m none -- dotnet "$output/publish/SharpHDL.Z80Fuzz.dll"
printf 'Z80 fuzz artifacts retained at %s\n' "$output"
