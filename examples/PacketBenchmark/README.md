# Packet checksum: C# versus handwritten RTL

This benchmark asks a specific question: what does SharpHDL's general feedback
controller cost compared with a small, purpose-written packet accumulator?
The [measured results](../../generated/packet-benchmark/RESULTS.md) come from
synthesis, mapped-cell simulation and three place-and-route seeds for each of three designs.
No physical FPGA has been programmed or measured by this benchmark.

## Contract and worked example

All three implementations use the same synchronous reset and ready/valid interfaces.
They accept one word per clock with a continuously ready consumer, and produce
that word's result one clock later. Under backpressure they hold output valid/data
until transfer. Reset takes priority over transfers, flushes the pending result,
and starts a new packet with sum zero. Endpoints must also honor that reset.

| Bus bits | Input meaning | Output meaning |
|---|---|---|
| 15:0 | Unsigned 16-bit payload | Running sum including this payload, modulo 65536 |
| 30:16 | Ignored | Zero |
| 31 | Last word of packet | Same last-word flag |

Every word produces an output. On the last word, the output contains the final
packet sum; the next input begins a new packet. Zero is ordinary data, not a
separator. This is an educational additive checksum, not Ethernet CRC, Internet
checksum, or an error-detection algorithm recommended for a transport protocol.
A separate bit in the scalar word represents `last`; this benchmark does not
instantiate an AXI wrapper or claim a complete AXI4-Stream interface.

| Accepted input payload | Last? | Emitted sum | State for next input |
|---:|---|---:|---:|
| 65535 | No | 65535 | 65535 |
| 1 | No | 0 (wrap) | 0 |
| 0 | Yes | 0 | 0 |
| 5 | Yes | 5 | 0 |

The [C# source](../PacketBenchmark.cs) expresses the loop and arithmetic. The
[elaborator](../../tests/PacketBenchmarkTests.cs) selects `ForwardOnCommit=true`
and `LogicLevelsPerStage=32`, grouping this small graph into one stage. That
number is an abstract grouping limit, not an FPGA timing constraint. The benchmark
checks that the compiler actually reports latency=1 and II=1. It emits both the original committed-state baseline and an optimized version
with `AdvanceStateOnAccept=true`. The emitted
[Verilog](../../generated/packet-benchmark/sharp.v) documents both bus layouts.

The [handwritten RTL](handwritten.v) is an independent, conventional elastic
one-stage implementation. Its private running sum updates on input acceptance.
The compiler baseline instead keeps committed state, carries proposed state with the result,
and forwards that state on simultaneous output/input acceptance. Those private
state timings differ, but their external token sequences and reset behavior match.
The optimized mode advances state on input acceptance, like the reference.
This lets the comparison measure the optimization against both the original
compiler implementation and the independent handwritten implementation.

## What was tested and measured

The [shared traffic testbench](traffic_tb.v) uses an independent arithmetic oracle;
it does not replay outputs taken from either implementation. It checks:

- 1,024 consecutive accepted words at one word/clock and one-clock latency.
- 12,000 total cycles, variable packet lengths, random source gaps, and long sink stalls.
- Directed zero payloads, modulo overflow, consecutive single-word packets, and ignored input bits.
- Output stability, exact token ordering, and accepted/received/discarded accounting.
- Reset during a held output and on a simultaneous output/input transfer edge.
- The same traffic through all three source RTL designs and their synthesized iCE40 cell netlist.

Yosys maps all three designs to the same iCE40 HX1K primitives. nextpnr places and
routes each using VQ100 and seeds 1, 2 and 3, with a 25 MHz clock constraint.
The runner fails on a tool error, missing traffic PASS, differing traffic totals,
or a reported core Fmax below the requested clock. Reports include LUTs,
flip-flops, carry cells, packed logic cells, RAM and DSP use. HX1K has no hard
DSP multiplier; this checksum requires neither multiplication nor memory blocks.

Pins are assigned automatically, not to a Go Board connector map. The clock
constraint evaluates internal register paths. No external input/output delay
budget is applied; asynchronous ready paths are reported but are not included
in the register-to-register Fmax claim. This is a core characterization, not a
board-ready design. Mapping and routing logs and machine-readable JSON are in
[generated/packet-benchmark](../../generated/packet-benchmark).

## Reproduce

Requires .NET 8, Python 3, Icarus Verilog, Yosys and nextpnr-ice40 with the HX1K
chip database installed. Put tool executables on PATH and set:

```sh
export DOTNET_ROOT=/path/to/dotnet-sdk
export YOSYS_DATDIR=/path/to/share/yosys
bash tests/run-packet-benchmark.sh
```

The checked-in results used .NET SDK 8.0.100, Icarus 12.0, Yosys 0.33 and
nextpnr 0.6. Different mapping tools/versions can change area and timing.
The runner records synthesis/place-and-route versions, source and testbench hashes,
seed-specific timing and resource reports. Temporary mapped netlists live under
`build/packet-benchmark`; source RTL and reproducible scripts are checked in.
The benchmark is also included in `tests/run-all.sh`.

## Interpretation and next steps

The retained baseline shows 54 LUT4s and 51 flip-flops for CIL versus 35 LUT4s
and 34 flip-flops for handwritten RTL: about **54% more LUTs** and **50% more
registers**. Both sustain the same traffic contract at the requested 25 MHz.
The optimized compiler mode now uses **35 LUTs and 34 flip-flops**, and all
three placement seeds report **202.06 MHz** register-path Fmax, matching the
handwritten reference in this example. The baseline remains at 142.86–156.81 MHz.
These estimates are not evidence of parity across arbitrary workloads.

The implemented optimization requires explicit opt-in and proves a narrow
eligibility condition: pure scalar transitions and one stage for every output
and next-state expression. See [the state contract](../../FEEDBACK_PIPELINES.md).
Multistage state advance and further general optimizations remain future work.

For physical validation, integrate the core with registered traffic generators
and checkers in a board wrapper, use the actual oscillator/pin constraints, route
again, and observe accepted/output/error counters over UART or LEDs. Test stalls,
reset, and sustained traffic on the board. Only then report measured operation;
25 Mword/s (400 Mbit/s payload) here is derived from clock and II, not measured
UART or Ethernet throughput. No hardware access is available in this run.

## Requirements

| ID | Requirement | Evidence/status |
|---|---|---|
| BENCH01 | Compare identical external packet/reset/flow-control contracts | Shared testbench and documented bit layouts; implemented |
| BENCH02 | Check independent functional oracle before and after mapping | Six traffic runs, including wrapping, stalls, packet boundaries and reset; implemented |
| BENCH03 | Verify latency and sustained II rather than infer from source | Exact cycle/occupancy checks and 1,024-word saturated phase; implemented |
| BENCH04 | Report technology-mapped resources and reproducible routed timing | Same HX1K/package/25 MHz target, three seeds, logs/JSON/tool versions; implemented |
| BENCH05 | Distinguish core estimates from physical system performance | Automatic pins, unbudgeted I/O, and absence of board measurements explicitly documented |
| BENCH06 | Preserve readable generated RTL and bus meanings | Named feedback registers, comments and packet bus field descriptions |
| BENCH07 | Validate on a physical FPGA with board-specific I/O constraints | Pending physical hardware access and a board traffic wrapper |
| BENCH08 | Optimize eligible recurrence state without changing observable behavior | Implemented for opt-in single-stage pure scalar recurrences; generalized multistage optimization remains future |
