#!/usr/bin/env bash
# Local compiler/library regression; Z80 has its separate full qualification run.
set -euo pipefail
script_directory=$(dirname -- "${BASH_SOURCE[0]}")
test -d "$script_directory" && test -d "$script_directory/.."
repo=$(cd -- "$script_directory/.." && pwd)
campaign=${1:-wp03}
[[ "$campaign" =~ ^[a-zA-Z0-9_-]+$ ]] || { echo 'Invalid campaign name' >&2; exit 2; }
test -d "$repo/tests/SharpHDL.UnitTests" && test -d /tmp
for directory in "$repo/build" "$repo/build/verification" "$repo/build/verification/$campaign"; do
  if [[ ! -d "$directory" ]]; then
    test -d "$(dirname -- "$directory")"
    mkdir -- "$directory"
  fi
done
output=$(mktemp -d /tmp/sharphdl-workpackages-tests-XXXXXX)
export DOTNET_PROCESSOR_COUNT=2 DOTNET_CLI_USE_MSBUILD_SERVER=0
export MSBUILDDISABLENODEREUSE=1 UseSharedCompilation=false
dotnet test "$repo/tests/SharpHDL.UnitTests/SharpHDL.UnitTests.csproj" -c Release \
  -m:1 -nr:false -p:UseSharedCompilation=false --artifacts-path "$output/artifacts" \
  --filter 'FullyQualifiedName!~Z80Tests' --collect:'XPlat Code Coverage' \
  --results-directory "$repo/build/verification/$campaign/linux" --logger 'trx;LogFileName=tests.trx'
printf 'Linux artifacts retained at %s\n' "$output"
