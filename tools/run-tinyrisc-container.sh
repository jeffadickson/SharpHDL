#!/usr/bin/env bash
# Execute the full TinyRisc regression inside the provisioned .NET/HDL container.
set -euo pipefail
cd /workspace
export DOTNET_ROOT="${DOTNET_ROOT:-$(dirname "$(readlink -f "$(command -v dotnet)")")}"
for tool in dotnet python3 iverilog vvp yosys; do
  command -v "$tool" >/dev/null || { echo "Missing required tool: $tool" >&2; exit 1; }
done
mkdir -p generated/tinyrisc
echo "Using .NET $(dotnet --version); $(yosys -V)"
bash tests/run-tinyrisc.sh
yosys -Q -T -l generated/tinyrisc/hello_uart_yosys.log -p \
  'read_verilog -sv generated/tinyrisc/HelloWorld.v examples/TinyRisc/hello_uart_top.v; hierarchy -check -top tinyrisc_hello_top; proc; opt; check -assert; stat'
echo 'PASS: TinyRisc Hello World CPU/RTL and serial UART regression plus Yosys structural check'
