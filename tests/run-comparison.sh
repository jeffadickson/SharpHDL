#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p build generated/synthesis
hand=examples/Verilog/filter_bank_handwritten.v
adapter=examples/Verilog/filter_bank_trace_adapter.v
bank_tb=generated/fsharp/FsFilterBank4_tb.v
iverilog -g2012 -s tb -o build/handwritten "$hand" "$adapter" "$bank_tb"
vvp build/handwritten
yosys -Q -T -p "read_verilog $hand $adapter; hierarchy -check -top h_FsFilterBank4; proc -norom; opt; memory; opt; check -assert; stat; write_verilog -noattr generated/synthesis/HandwrittenFilterBank_rtl.v" > generated/synthesis/HandwrittenFilterBank.log
iverilog -g2012 -s tb -o build/handwritten_synth generated/synthesis/HandwrittenFilterBank_rtl.v "$bank_tb"
vvp build/handwritten_synth
iverilog -g2012 -s tb -o build/debug_core generated/DebugUartController.v generated/DebugUartController_tb.v
vvp build/debug_core
iverilog -g2012 -s tb -o build/debug_serial generated/DebugUartController.v examples/Verilog/debug_uart_serial.v examples/Verilog/debug_uart_serial_tb.v
vvp build/debug_serial
yosys -Q -T -p "read_verilog generated/DebugUartController.v; hierarchy -check -top h_DebugUartController; proc -norom; opt; check -assert; stat; write_verilog -noattr generated/synthesis/DebugUartController_rtl.v" > generated/synthesis/DebugUartController.log
iverilog -g2012 -s tb -o build/debug_synth generated/synthesis/DebugUartController_rtl.v generated/DebugUartController_tb.v
vvp build/debug_synth
