# TinyRisc hardware baseline

Run `bash tests/run-tinyrisc-benchmark.sh` with DOTNET_ROOT, YOSYS_DATDIR and
Icarus/Yosys/nextpnr-ice40 on PATH. This first reruns the full architecture suite,
then maps the existing CountRun CPU and replays its established cycle trace.
A debug-multiplexing wrapper preserves every architectural register while reducing
package-pin requirements. Its mapped netlist is compared with the validated source
CPU for 1,000 cycles and every debug selector. Three HX8K CT256 place-and-route
seeds check a 25 MHz internal clock constraint.

[Results](../../../generated/tinyrisc-benchmark/RESULTS.md) are a structural CPU
baseline, not a claimed improvement from the CIL feedback optimization. Ports are
automatically placed, with no external timing budget or real board pin assignment.
No board was programmed. Counts include the debug mux and the fixed counting ROM.
The chosen HX8K device differs from the packet benchmark's HX1K.

| Requirement | Status / evidence |
|---|---|
| RISC-BENCH01: retain instruction, RAM, UART, run/step, fault/reset behavior | Full existing TinyRisc architecture regression is a runner prerequisite; mapped CountRun trace also passes |
| RISC-BENCH02: retain observable architectural state through mapping | Debug mux exposes PC, instruction, last PC, all registers, phase/retired/fault/halt; mapped wrapper checked each cycle |
| RISC-BENCH03: report actual technology-mapped costs and routed estimates | HX8K CT256, three seeds, 25 MHz gate; tool versions, source hashes, logs and JSON; no physical-board or CPU speedup claims |
