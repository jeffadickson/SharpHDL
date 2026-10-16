# RAM end-to-end demonstration: acceptance harness

The existing `SynchronousRam` implementation already supports cycle-accurate
read-before-write semantics, independent simulation, and inferred Verilog.
This milestone must **reuse that implementation**, not create a second RAM.

## First runnable demonstration

Use the documented `SynchronousRam.Create` API to build a 256×16-bit
memory with shared read/write address. Initialize it with a reproducible
pattern. Drive 600+ cycles containing reads, writes, collisions, disabled
reads, out-of-range cases (in a second 13-word fixture), and resets.

Run three independent checks:
1. A plain C# array oracle with registered read output.
2. `Module.Simulate()` and its reset behavior.
3. Generated Verilog executed by Icarus or Verilator with the identical
   stimulus trace and sampled post-clock outputs.

Compare every cycle, not merely the final memory image. Preserve
read-before-write collision behavior and storage retention across reset.
Report seeds, exact commands, tool versions, and failing vectors.

## Second demonstration: compiler integration

Extend the Core HLS scheduler to handle a *single owned synchronous RAM*
with explicitly accepted requests and one-cycle registered reads. Reuse
the established memory semantics and emitted RAM representation.
Backpressure must hold response data and validity without duplicating
writes. The compiler must reject unsupported port sharing and ambiguous
read/write ordering rather than silently change the hardware.

## Required evidence

- A C# example and independently runnable test script.
- A generated Verilog artifact and deterministic trace.
- CI evidence for C# reference versus RTL; separate mapping evidence
  for FPGA BRAM inference.
- XML documentation on public C# APIs.
- No claims of vendor timing closure without vendor implementation tools.

This document distinguishes existing standalone RAM support from the
still-missing automatic RAM scheduling inside elastic HLS.
