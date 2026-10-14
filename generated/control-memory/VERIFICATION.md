# Control and banked-memory verification

Commands:

- `tests/run-control-memory.sh` (.NET SDK 8.0.100, Icarus 12.0).
- `tests/map-banked-memory.sh` (Yosys 0.33).

Debug and optimized builds each passed 21,414 assertions and seven RTL benches:

| Bench | Cycles / scope |
|---|---|
| AxiRegisters | 2,632 cycles; channel ordering, byte strobes, errors, response stalls, changing status, collisions and reset |
| BankedGeneric | 601 cycles; four independent banks with old-data collisions, bounds and reset retention |
| BankedXilinx7Series | Same 601-cycle memory oracle with Xilinx inference policy |
| BankedIntelCycloneV | Same 601-cycle memory oracle with Cyclone V inference policy |
| BankedIntelArria10 | Same 601-cycle memory oracle with Arria 10 inference policy |
| StreamCapture | 1,201 cycles; frame/capacity stopping, arbitrary metadata, pauses, clear, reads and retained RAM |
| CaptureInstrument | Independent integrated AXI wrapper/control/capture/readback test |

Core replay traces are generated after graph assertions against native register
and array oracles. The integrated bench drives actual bus transactions and checks
independently computed packed words. Icarus used -Wall -Wno-timescale and emitted
no compilation diagnostics. The large replay benches regenerate under build/.

Resource mapping evidence:

- Four Xilinx banks, each 13 logical 16-bit rows padded to 16 physical rows:
  exactly four RAMB18E1 cells after synth_xilinx -family xc7.
- Four Cyclone V banks, each 256 x 16 bits: exactly four MISTRAL_M10K cells after
  synth_intel_alm -family cyclonev.
- The tiny Cyclone V fixture maps to logic in this Yosys version because the Intel
  memory mapping rules require minimum block efficiency. Its ramstyle hint is a
  Quartus inference request, not an unconditional Yosys resource directive.
- The original unpadded 13-row Xilinx shape failed mapping; protected zero padding
  fixed that while retaining out-of-range zero reads and ignored writes.

Statistics are in mapping/. Open-source resource mapping does not prove vendor
primitive collision behavior, timing closure, or physical FPGA operation.
Vivado/Quartus qualification and Arria 10 M20K mapping remain pending. The larger
Cyclone V fixture is mapping evidence; the small fixture supplies cycle replay.

The capture example transfers into local RAM only. It is not an AXI4 burst DMA
master and does not access host DDR or manage descriptors/cache coherency.
