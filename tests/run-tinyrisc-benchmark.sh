#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
: "${DOTNET_ROOT:?Set DOTNET_ROOT to a .NET 8 SDK}"
: "${YOSYS_DATDIR:?Set YOSYS_DATDIR to the Yosys share directory}"
bash tests/run-tinyrisc.sh
python3 tools/run_tinyrisc_benchmark.py
