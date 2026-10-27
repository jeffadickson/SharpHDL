#!/usr/bin/env bash
# Bounded coverage-guided campaign; keep native Linux output and all crash/queue evidence.
set -euo pipefail
repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
seconds=${1:-60}
[[ "$seconds" =~ ^[0-9]+$ ]] && ((seconds >= 1 && seconds <= 3600)) || { echo 'Budget must be 1..3600 seconds' >&2; exit 2; }
test -d "$repo/tools/SharpHDL.ScheduledFuzz" && test -d /tmp
output=$(mktemp -d /tmp/sharphdl-fuzz-XXXXXX)
# Local SDK installations may be outside the apphost's default runtime search path.
DOTNET_ROOT=$(dirname -- "$(readlink -f -- "$(command -v dotnet)")")
test -d "$DOTNET_ROOT"
export DOTNET_ROOT DOTNET_ROLL_FORWARD=Major
dotnet publish "$repo/tools/SharpHDL.ScheduledFuzz" -c Release --artifacts-path "$output/artifacts" -o "$output/publish"
dotnet tool install SharpFuzz.CommandLine --version 2.2.0 --tool-path "$output/tools"
for assembly in SharpHDL.HLS SharpHDL.Core SharpHDL.Simulation SharpHDL.ScheduledExamples; do
  "$output/tools/sharpfuzz" "$output/publish/$assembly.dll"
done
AFL_SKIP_BIN_CHECK=1 AFL_NO_UI=1 AFL_SKIP_CPUFREQ=1 AFL_I_DONT_CARE_ABOUT_MISSING_CRASHES=1 
export AFL_SKIP_BIN_CHECK AFL_NO_UI AFL_SKIP_CPUFREQ AFL_I_DONT_CARE_ABOUT_MISSING_CRASHES
afl-fuzz -i "$repo/tools/SharpHDL.ScheduledFuzz/corpus" -o "$output/findings" -V "$seconds" -s 261026 -t 5000 -m none -- dotnet "$output/publish/SharpHDL.ScheduledFuzz.dll"
printf 'Campaign retained at %s\n' "$output"
cat "$output/findings/default/fuzzer_stats"
# AFL can finish successfully despite retained crashes; make those fail verification.
if find "$output/findings/default/crashes" -type f ! -name README.txt -print -quit | grep -q .; then exit 1; fi
