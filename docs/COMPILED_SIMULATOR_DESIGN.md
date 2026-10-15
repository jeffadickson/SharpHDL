# Compiled simulator design: verified DAG backend

This is the current simulator implementation addendum to [DESIGN.md](DESIGN.md). The historical implementation entries in that file are superseded for execution status by [SIMULATOR_VERIFICATION.md](SIMULATOR_VERIFICATION.md). The supported frontend/IR migration plan is unchanged.

## Hardware graph, not source-language execution

Both explicit structural designs and CIL/HLS-generated designs elaborate into the existing `Module`/`Expr` hardware graph. The simulator executes that hardware graph. It does not run an arbitrary .NET application as if software instructions were FPGA cycles. The proposed `SharpHDL.Core.HardwareGraph` is not yet the production compiler representation.

`CompiledCombinational` performs a dependency walk with cycle and ownership checks, memoized by expression identity. Constants are emitted inline, wires alias their drivers, and inputs/registers are loaded once into typed locals. Every computed DAG node has one local assignment. All register next-state roots share a schedule and an `Action<BigInteger[], BigInteger[]>` delegate fills the caller-owned pending-state buffer.

This matters for CPU decode and other high-fanout logic: a fused tree can still repeatedly expand and execute the same shared node. A 28-level `result = result + result` test now has exactly 29 scheduled nodes instead of exponential tree expansion.

## Numeric contract

- Widths 1..32 use unsigned 32-bit locals; widths 33..64 use unsigned 64-bit locals; wider supported bit vectors use `BigInteger`.
- Each operation keeps the original node's modulo-width behavior. A subsequent resize cannot recover an already discarded carry.
- BigInteger values are masked before native conversion, because the CLR conversion itself range-checks.
- Native signed comparisons and arithmetic shifts sign-extend from the declared width, not from the storage width.
- Oversized logical shifts yield zero. Arithmetic shifts clamp their native count rather than accepting the CLR's masked-count behavior.
- Lookup and RAM reads retain explicit address guards and yield zero outside the supported range.

No full native-word persistent-state claim is made: public values, the state buffers and RAM images still use BigInteger. Word-sized locals remove repeated conversions inside most ordinary datapaths; there is more optimization headroom at state and memory boundaries.

## Atomic rising edges and RAM

The sequential owner validates input writes and updates its operand buffer immediately. A normal edge evaluates every next-state root against the old register/input buffer and old RAM images. It also computes pending RAM write addresses/data before modifying any memory. Only after all evaluations succeed are staged memory writes and register values committed. Reset restores register reset values, including synchronous RAM output registers, without erasing RAM cells.

The pending-register and RAM-write scratch arrays are reused. Individual BigInteger operations may still allocate. RAM address, enable and data currently use separate compiled cones; they are not advertised as fully fused into the register schedule.

## Eager schedule boundary

The topological schedule can evaluate both branches of a mux. This is valid for the current initialized two-state, pure and total operation set: all reads are bounds-guarded and arithmetic is defined at every supported input. The compiled API requires reset for state-dependent execution. Adding exceptions, partial operations, speculative side effects, four-state rules, or uninitialized-branch compatibility requires explicit control-dependent lowering; do not generalize this scheduling rule silently.

## Verification and interfaces

`Set`, `Read(Signal)`, `Read(Expr)` and `Tick` support local ownership and exact bit widths. Output signals dispatch through compiled probes. Register-free circuits are supported. Unsupported operations are rejected instead of silently falling back to an interpreter.

The verified acceptance includes integer and queue oracles, two .NET engines, generated RTL under Icarus, a real TinyRisc UART serial decoder, a 256-edge matched native trace fingerprint, full final-port comparison and five repeated in-process timing samples. See the evidence report for exact tested commits, tool versions, results, scope and remaining work.
