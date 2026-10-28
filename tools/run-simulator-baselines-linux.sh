#!/usr/bin/env bash
# Run after other local verification jobs finish. No optimization or speedup is asserted.
set -euo pipefail
repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
test -d "$repo/tools/SharpHDL.ScheduledBench" && test -d /tmp
extra=()
if (($#)); then
  [[ $# == 1 && $1 == --include-native-aot ]] || { echo "Usage: $0 [--include-native-aot]" >&2; exit 2; }
  extra=(--include-native-aot)
fi
output=$(mktemp -d /tmp/sharphdl-baseline-XXXXXX)
export DOTNET_PROCESSOR_COUNT=2 DOTNET_CLI_USE_MSBUILD_SERVER=0 MSBUILDDISABLENODEREUSE=1 UseSharedCompilation=false
git -C "$repo" rev-parse HEAD > "$output/base-commit.txt"
git -C "$repo" -c core.autocrlf=true diff --binary -- src/SharpHDL.Core src/SharpHDL.Simulation src/SharpHDL.HLS examples/ScheduledTable.cs examples/SharpHDL.ScheduledExamples.csproj tools/SharpHDL.ScheduledBench tools/SharpHDL.SimulatorBenchmarks tests/Shared/SimulatorObservationWorkload.cs > "$output/working.patch"
sha256sum "$repo/tests/Shared/SimulatorObservationWorkload.cs" "$repo/tools/SharpHDL.ScheduledBench/Program.cs" \
  "$repo/tools/SharpHDL.ScheduledBench/SharpHDL.ScheduledBench.csproj" "$repo/tools/SharpHDL.ScheduledBench/BaselineRunOptions.cs" "$repo/tools/SharpHDL.SimulatorBenchmarks/SimulationBenchmarks.cs" "$repo/tools/SharpHDL.SimulatorBenchmarks/SharpHDL.SimulatorBenchmarks.csproj" > "$output/source-hashes.txt"
cp -- "$repo/tests/Shared/SimulatorObservationWorkload.cs" "$output/workload.cs"
cp -- "$repo/tools/SharpHDL.ScheduledBench/BaselineRunOptions.cs" "$output/options.cs"
cp -- "$repo/tools/SharpHDL.SimulatorBenchmarks/SimulationBenchmarks.cs" "$output/benchmarks.cs"
cp -- "$repo/tools/SharpHDL.SimulatorBenchmarks/SharpHDL.SimulatorBenchmarks.csproj" "$output/benchmarks.csproj"
dotnet --info > "$output/dotnet-info.txt"
(cd -- "$repo/tools/SharpHDL.ScheduledBench" && dotnet run -c Release --artifacts-path "$output/bootstrap" -- \
  --qualified-baseline "${extra[@]}" --filter '*ObservedSimulationBenchmarks*' --artifacts "$output/report")
printf 'Retained controlled-input baseline at %s\n' "$output"
