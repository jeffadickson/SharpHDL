#!/usr/bin/env bash
# Reusable established coverage-guided engine; all corpora/findings remain local.
set -euo pipefail
repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
seconds=${1:-60}
[[ "$seconds" =~ ^[0-9]+$ ]] && ((seconds>=1 && seconds<=3600)) || exit 2
test -d "$repo/tools/SharpHDL.DocumentationFuzz" && test -d /tmp
output=$(mktemp -d /tmp/sharphdl-doc-fuzz-XXXXXX)
export DOTNET_PROCESSOR_COUNT=2 DOTNET_CLI_USE_MSBUILD_SERVER=0 MSBUILDDISABLENODEREUSE=1
DOTNET_ROOT=$(dirname -- "$(readlink -f -- "$(command -v dotnet)")")
test -d "$DOTNET_ROOT"
export DOTNET_ROOT DOTNET_ROLL_FORWARD=Major
dotnet publish "$repo/tools/SharpHDL.DocumentationFuzz" -c Release -m:1 -nr:false -p:UseSharedCompilation=false --artifacts-path "$output/artifacts" -o "$output/publish"
dotnet tool install SharpFuzz.CommandLine --version 2.2.0 --tool-path "$output/tools"
"$output/tools/sharpfuzz" "$output/publish/SharpHDL.DocBuilder.dll"
export AFL_SKIP_BIN_CHECK=1 AFL_NO_UI=1 AFL_SKIP_CPUFREQ=1 AFL_I_DONT_CARE_ABOUT_MISSING_CRASHES=1
afl-fuzz -i "$repo/tools/SharpHDL.DocumentationFuzz/corpus" -o "$output/findings" -V "$seconds" -s 261026 -t 5000 -m none -- dotnet "$output/publish/SharpHDL.DocumentationFuzz.dll"
printf 'Documentation campaign retained at %s\n' "$output"
cat "$output/findings/default/fuzzer_stats"
if find "$output/findings/default/crashes" -type f ! -name README.txt -print -quit | grep -q .; then exit 1; fi
