#!/usr/bin/env bash
# Native Linux artifacts stay separate from the Windows build tree.
set -euo pipefail
repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
test -d "$repo/tests/SharpHDL.UnitTests" && test -d /tmp
output=$(mktemp -d /tmp/sharphdl-z80-tests-XXXXXX)
dotnet test "$repo/tests/SharpHDL.UnitTests/SharpHDL.UnitTests.csproj" -c Release --artifacts-path "$output/artifacts" --results-directory "$repo/build/verification/z80/linux" --logger 'trx;LogFileName=tests.trx'
printf 'Linux managed artifacts retained at %s\n' "$output"
