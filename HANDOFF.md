# SharpHDL handoff

## Current status

The schedule-driven FIR no longer constructs a legacy `Module`. A reusable
`ScheduledMachineBuilder` in `SharpHDL.HLS` represents typed expressions,
registers, states, guarded assignments and transitions, synchronous RAM
operations, and single-port arbitration. It lowers directly to canonical
Core IR as `ScheduledHardwareDesign`.

`HlsScheduledFirCompiler` maps the existing 2*N-cycle single-port FIR plan
onto this backend. It preserves AXI backpressure, sidebands, coefficient
capture, reset state, read-before-write RAM behavior, one-cycle read
latency, circular history, and one shared multiplier.

## Verification completed

- Baseline: 132/132 xUnit tests passed on .NET SDK 10.0.402.
- New scheduled FIR tests: 7/7 passed for 2, 4, 8 and 16 taps.
- Four-tap Core implementation matches the existing structural FIR in an
  independent transaction simulation.
- Project layering check passed: Core <- HLS, Simulation and RTL.
- Core Verilog generation and Icarus cycle replays passed for 2, 4, 8 and
  16 taps under randomized traffic, stalls, reset and history wraparound.

## Known limitations

FIR planning remains specialized. Generic lowering currently supports the
Core operations used by this kernel and single-port, zero-initialized RAM.
It rejects multiple same-state accesses conservatively rather than proving
mutually exclusive guards. It does not yet extract schedules from arbitrary
CIL loops, overlap iterations, bind multiple arithmetic resources, support
multiport RAM, or perform device timing and placement.

## Recommended next work

Add explicit named arithmetic resource declarations and binding validation,
then compile a non-FIR memory kernel (for example a packet table update) to
prove reuse. After that, connect supported stateful CIL control flow to this
schedule representation and add Yosys resource-count checks for RAM and DSP
inference.
