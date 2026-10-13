#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
: "${YOSYS_DATDIR:?Set YOSYS_DATDIR to the Yosys share directory}"
mkdir -p build generated/synthesis
for name in FsFir FsOscillator FsScramblerRoundTrip FsFilterBank2 FsFilterBank3 FsFilterBank4 PacketPeak; do
  stem="generated/fsharp/$name"
  [[ "$name" == PacketPeak ]] && stem="generated/$name"
  yosys -Q -T -p "read_verilog $stem.v; hierarchy -check -top h_$name; proc -norom; opt; memory; opt; check -assert; stat; write_verilog -noattr generated/synthesis/${name}_rtl.v" > "generated/synthesis/$name.log"
  iverilog -g2012 -s tb -o build/example-synth "generated/synthesis/${name}_rtl.v" "${stem}_tb.v"
  vvp build/example-synth
done
yosys -Q -T -p 'read_verilog generated/three-languages/AddTwo_CSharp.v; synth_ice40 -top h_AddTwo; check -assert; stat; write_verilog -noattr generated/three-languages/AddTwo_mapped.v' > generated/three-languages/AddTwo_mapped.log
iverilog -g2012 -s tb -o build/addtwo-mapped generated/three-languages/AddTwo_mapped.v generated/three-languages/AddTwo_CSharp_tb.v "$YOSYS_DATDIR/ice40/cells_sim.v"
vvp build/addtwo-mapped
yosys -Q -T -p 'read_verilog generated/DebugUartController.v examples/Verilog/debug_uart_serial.v; hierarchy -check -top debug_uart_serial; proc; opt; check -assert; stat' > generated/synthesis/DebugUartSerial.log
for wrapper in generated/fsharp/*_axi.v; do
  stem="${wrapper%_axi.v}";name="$(basename "$stem")"
  iverilog -g2012 -s "h_${name}_axi" -o build/fsharp-wrapper "$stem.v" "$wrapper"
done
