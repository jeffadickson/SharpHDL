#!/usr/bin/env bash
set -euo pipefail
_sharphdl_repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
test -d "$_sharphdl_repo/tools/SharpHDL.EquivalenceCli" && test -d /tmp
source "$_sharphdl_repo/tools/setup-z3-linux.sh"
_sharphdl_output=$(mktemp -d /tmp/sharphdl-equivalence-XXXXXX)
export DOTNET_PROCESSOR_COUNT=2 DOTNET_CLI_USE_MSBUILD_SERVER=0 MSBUILDDISABLENODEREUSE=1 UseSharedCompilation=false
dotnet run --project "$_sharphdl_repo/tools/SharpHDL.EquivalenceCli" -c Release --artifacts-path "$_sharphdl_output/artifacts" -- "$_sharphdl_output/evidence"
printf 'Retained equivalence qualification at %s\n' "$_sharphdl_output"
