#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p build generated/synthesis
# Set YOSYS_DATDIR when using a locally packaged Yosys installation.
yosys_data="${YOSYS_DATDIR:-$(yosys-config --datdir)}"
for arch in Parallel Iterative; do
  for suffix in SignedNearestEvenSaturate SignedFloorWrap UnsignedCeilingWrap; do
    case "$suffix" in
      SignedNearestEvenSaturate) name="Divide8Signed${arch}NearestEvenSaturate" ;;
      SignedFloorWrap) name="Divide8Signed${arch}FloorWrap" ;;
      UnsignedCeilingWrap) name="Divide8Unsigned${arch}CeilingWrap" ;;
    esac
    yosys -Q -T -q -l "generated/synthesis/${name}_ice40.log" -p "read_verilog generated/$name.v; hierarchy -check -top h_$name; synth_ice40 -top h_$name; check -assert; stat; write_verilog -noattr generated/synthesis/${name}_ice40.v"
    iverilog -g2012 -s tb -o build/divider-synth "generated/synthesis/${name}_ice40.v" "$yosys_data/ice40/cells_sim.v" "generated/${name}_tb.v"
    vvp build/divider-synth
  done
done
