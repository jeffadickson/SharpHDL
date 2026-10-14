#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
: "${DOTNET_ROOT:?Set DOTNET_ROOT to .NET 8 SDK directory}"
python3 tests/check-tinyrisc-assembler.py
python3 examples/TinyRisc/assemble.py examples/TinyRisc/count.asm generated/tinyrisc/count.hex
python3 examples/TinyRisc/test_hello_world_smoke.py
python3 examples/TinyRisc/assemble.py examples/TinyRisc/hello_world.asm generated/tinyrisc/hello_world.hex
python3 examples/TinyRisc/build.py "$DOTNET_ROOT"
"$DOTNET_ROOT/dotnet" build/tinyrisc/TinyRisc.dll
for bench in generated/tinyrisc/*_tb.v; do
  iverilog -g2012 -s tb -o build/tinyrisc/test "${bench%_tb.v}.v" "$bench"
  vvp build/tinyrisc/test
done
iverilog -g2012 -s tb_tinyrisc_uart -o build/tinyrisc/hello_uart_test \
  generated/tinyrisc/HelloWorld.v \
  examples/TinyRisc/hello_uart_top.v examples/TinyRisc/hello_uart_tb.v
vvp build/tinyrisc/hello_uart_test
