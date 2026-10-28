#!/usr/bin/env bash
set -euo pipefail
_sharphdl_repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
test -d "$_sharphdl_repo/tests/SharpHDL.VerificationTests" && test -d /tmp
source "$_sharphdl_repo/tools/setup-z3-linux.sh"
_sharphdl_output=$(mktemp -d /tmp/sharphdl-verification-tests-XXXXXX)
export DOTNET_PROCESSOR_COUNT=2 DOTNET_CLI_USE_MSBUILD_SERVER=0 MSBUILDDISABLENODEREUSE=1 UseSharedCompilation=false
dotnet test "$_sharphdl_repo/tests/SharpHDL.VerificationTests" -c Release --artifacts-path "$_sharphdl_output/artifacts" \
  -m:1 -nr:false -p:UseSharedCompilation=false --collect:'XPlat Code Coverage' --logger 'trx;LogFileName=tests.trx' \
  --results-directory "$_sharphdl_repo/build/verification/wp08/linux"
printf 'Retained native verification tests at %s\n' "$_sharphdl_output"
