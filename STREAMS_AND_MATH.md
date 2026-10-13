# Streams, packets and hardware math — v0.2

## Should every function be an AXI stream?

Use expressions for simple combinational functions and ready/valid streams for
buffered or pipelined functions. Use an AXI4-Stream wrapper at FPGA IP boundaries.
Putting a handshake around every add, comparison or bit slice would create
unnecessary registers and control logic. Multi-cycle functions need an explicit
latency and flow-control contract.

Our stream bundle uses Data, Keep, Last, User, Valid and Ready. A transfer occurs
on a rising edge with Valid and Ready both true. A producer must hold Valid and
all payload/sideband fields stable until transfer. A stalled consumer may change
Ready. The producer must not wait for Ready before asserting Valid.

All stream widths are byte-aligned. Keep has one bit per byte; lane zero is
Data[7:0]. User is a per-beat metadata field, at least one bit wide. Last marks
the final beat. Valid bubbles are allowed even inside packets. Reset flushes
all queued/in-flight transactions; the upstream system must restart packet
framing after reset. No clock-domain crossing is implemented.

## Stream API

```csharp
var input = Stream.Input(m, "s_axis", 64, userWidth: 8);
var output = Stream.Output(m, "m_axis", 64, userWidth: 8);
var buffered = StreamBlocks.Fifo(m, input, "queue", depth: 4);
Stream.Connect(m, buffered, output);
```

| Factory | Behavior |
|---|---|
| `Map` | One elastic register stage; transforms Data and carries metadata |
| `Pipeline` | A requested number of identity Map stages |
| `Fifo` | 1–256-entry register-bank FIFO, no fall-through |
| `Arbitrate` | Two-input round-robin packet arbiter |
| `Stream.Connect` | Connect forward payload/valid and reverse ready |

`Map` preserves Keep if widths match; when widths differ it asserts all output
Keep bits. Math inputs therefore require all input byte lanes valid. Partial
math operands are outside the interface contract. Last and User remain aligned
with each result. The source is responsible for providing valid inputs.

Map stages and FIFO input readiness can depend combinationally on downstream
readiness. Long pipelines can have a long ready path. This release does not
include a registered-ready skid-buffer timing cut. One sample/clock is a logical
throughput property, not an achieved target-device clock frequency.

The FIFO permits simultaneous push and pop, including when full. Its storage
is registers and selection logic, suited to short queues. The arbiter locks
its choice when it first offers valid output, including during a stall, and
retains that choice through the packet's accepted Last beat. It alternates
preference after completed packets. A selected source that never supplies Last
can block the other source; there is no timeout or abort signal.

## Packet facilities

```csharp
var header = new PacketLayout()
    .Field("version", 4)
    .Field("type", 12)
    .Field("length", 16)
    .Field("source", 32);
var packetType = header.Extract(word, "type");
var changed = header.Replace(word, "type", Expr.Const(12, 7));
```

Fields are declared from most-significant to least-significant bits. The same
layout supports software PackValue/Decode and hardware Pack/Extract/Replace.
This is a bit layout, not an automatic network byte-order conversion. For this
example, version is bits 63:60 and source is bits 31:0. The CRC byte traversal is
separately defined as low-to-high byte lanes. A protocol integration must choose
its bus byte order explicitly.

`RouterDesign.Build(4)` creates a 64-bit input and two independently queued
64-bit outputs. A full first-beat header is required: type 1 selects output one;
all other types select output zero. The route is held through Last. The entire
packet, including its header and original Keep/User, is forwarded. Single-beat
packets are supported. A blocked selected queue backpressures the input; this
is not a virtual-output-queue architecture that can bypass blocked packets.
The length field is carried but is not validated against Last.

`PacketMath.AttachCrc32` passes payload through and replaces User with CRC-32 on
the final beat (zero on other beats). This deliberately replaces incoming User;
combine metadata explicitly if both are needed. It implements reflected
CRC-32/ISO-HDLC, initial state 0xffffffff, polynomial 0xedb88320, final inversion.
Only Keep-enabled bytes contribute, in ascending lane order. State updates
only on transfer and restarts after accepted Last. It attaches a CRC value;
it does not append CRC bytes or validate an existing FCS. The regression uses
"123456789" -> 0xcbf43926 plus sparse-Keep packets.

## Hardware math calls

```csharp
var phase = Stream.Input(m, "s_axis", 24, userWidth: 8);
var cosine = HwMath.Cos(m, phase, "cosine");
var result = Stream.Output(m, "m_axis", 32, userWidth: 8);
Stream.Connect(m, cosine, result);
```

This call elaborates a CORDIC circuit. It does not rewrite ordinary
`System.Math.Cos(double)`. A Roslyn frontend that recognizes ordinary C# math
calls is a separate future feature. All present hardware math uses explicit
formats and an explicit implementation choice at elaboration time.

| Function/backend | Input interpretation | Output | Default stages |
|---|---|---|---:|
| `Sin`, `Cos`, `SinCos` | 24-bit unsigned turns modulo one revolution | Signed 32-bit Q8.24 per component | 21 |
| `RomMath(Exp2Fraction)` | f = input / 65536, f in [0,1) | 2^f in unsigned 24-bit Q4.20 | 2 |
| `RomMath(Log2Mantissa)` | x = 1 + input / 65536 | log2(x), Q4.20 | 2 |
| `RomMath(ReciprocalMantissa)` | x = 1 + input / 65536 | 1/x, Q4.20 | 2 |
| `RomMath(SqrtMantissa)` | x = 1 + input / 65536 | sqrt(x), Q4.20 | 2 |
| `PolynomialMath` for the same four functions | Same normalized input as above | Q4.20 | 11 |

Q8.24 here means a total 32-bit two's-complement value with 24 fractional bits;
Q4.20 is a total 24-bit unsigned value with 20 fractional bits. `SinCos` packs
cosine in bits 31:0 and sine in bits 63:32. `EncodePhase(radians)` is a host helper
for preparing input data; the hardware does not accept floating-point radians.
0x000000, 0x400000, 0x800000, 0xc00000 represent 0, pi/2, pi, 3pi/2.

Stage counts are clock edges from acceptance to a valid result in an otherwise
unstalled pipeline. Backpressure increases elapsed latency. All these pipelines
can accept one transaction each clock once running without stalls. CORDIC uses
one range-reduction stage and 20 rotation stages by default; 1–22 iterations are
supported, with correspondingly different accuracy and latency. Low-iteration
settings have no promise of the default error bound.

CORDIC reduces the phase to the convergence interval, compensates gain using
the initial x value, and applies signed shifts/adds with quantized arctangent
constants. It needs no runtime multiplier. SinCos shares the rotation chain;
separately calling Sin and Cos constructs separate chains. Use SinCos when both
results are needed.

## ROM and Level II-style math

The reusable ideas are range reduction, stored coefficients or values, and
small fixed-point evaluation circuits. These implementations are new and do
not reproduce TRS-80 Level II ROM floating-point formats or bit-exact results.

RomMath samples each normalized function at 257 points by default. Hardware
stores 256 bases and signed deltas, looks up the segment, then linearly
interpolates using the remaining input bits. AddressBits may be 2–12. Generated
Verilog uses constant case functions. Synthesis may implement tables with
logic; block-RAM inference is not guaranteed.

PolynomialMath centers the argument at fraction=0.5 and uses newly generated
Taylor/binomial coefficients with Horner evaluation. Default degree is ten;
2–16 is supported. Coefficients are quantized to signed Q4.28. Each Horner stage
uses a signed 64-bit product followed by a shift and a coefficient addition.
These constants become compile-time coefficients, not a dynamically addressed
coefficient RAM. Final conversion to Q4.20 truncates. This uses more multiplier
resources than the default table backend; DSP mapping depends on synthesis.

Supporting arbitrary positive inputs to log/reciprocal/sqrt would require
leading-bit normalization, exponent tracking/reconstruction, and an explicit
exception/overflow policy. Negative logarithms, zero reciprocals, NaNs, infinity,
and general floating-point inputs are not represented by these normalized APIs.
Natural exp/log can be constructed with scaling and appropriate range reduction,
but only exp2/log2 are supplied in this release.

## Measured accuracy

Default CORDIC: maximum observed absolute component error 2.416498183444027e-6
across 396 full-circle, boundary and deterministic random phases.

Default table interpolation: maximum observed absolute error 4.01469e-6 in
its initial characterization. Combined table and degree-ten polynomial tests:
maximum observed absolute error 5.7220458984375e-6, across 513 inputs per function
and backend. Tests enforce <5e-6 for CORDIC and <8e-6 for normalized math.
These are sampled regression results, not exhaustive error proofs over every
representable input. Error is measured against the quantized input value.

## AXI4-Stream integration

```csharp
File.WriteAllText("core.v", m.Verilog());
File.WriteAllText("core_axi.v", Axi4Stream.Wrapper(m));
```

The wrapper exposes `aclk`, active-low `aresetn`, and the named stream ports,
removing the core's `s_` signal-name prefix. It gates outgoing Valid/Ready and
incoming Valid/Ready during reset, and drives the core's synchronous reset.
Assert reset across at least one rising edge and deassert synchronously to
aclk. No reset-domain synchronizer is included.

The implemented subset has TDATA, TKEEP, TLAST, TUSER, TVALID and TREADY; no
TSTRB, TID or TDEST. Packet data bytes are transported as declared. This is not
AXI4 memory-mapped or AXI4-Lite, and does not include DMA or address channels.

For configuration/status registers, an AXI4-Lite adapter is a sensible future
addition. For the packet and math datapaths, AXI4-Stream is the appropriate
external interface. Pure combinational internal expressions remain expressions.

## Verification and synthesis

The release has 56,852 C# assertions: 15,212 base checks and 41,640 feature checks.
Feature traces generate HDL testbenches for exact C#/Verilog comparisons.
Random stalls, FIFO saturation and reset flushing, packet-route retention,
metadata stability, arbitration, CRC, signed math, ROM defaults, and documented
latencies are checked. An AXI wrapper test checks reset masking and a stalled
payload. Six designs also pass Yosys RTL synthesis checks and post-synthesis
Icarus simulation against the original generated testbenches.

The tested Yosys 0.33 flow uses `proc -norom` to retain table case logic: its
automatic `proc_rom`/memory-mapping path produced undriven table outputs for
this test. The documented flow passes `check -assert` and post-synthesis vectors.
This is RTL synthesis validation, not target FPGA mapping, place-and-route,
timing closure, formal equivalence or full AXI protocol certification.

References for the interfaces and algorithm family:
- Arm AXI-Stream specification: https://documentation-service.arm.com/static/64819f1516f0f201aa6b963c
- AMD CORDIC guide: https://www.amd.com/content/dam/xilinx/support/documents/ip_documentation/cordic/v6_0/pg105-cordic.pdf


## Version 0.6 additions

The normalized fixed-point APIs above retain their domains and behavior.
[Numeric types and math](NUMERIC_TYPES_AND_MATH.md) documents the additional
custom finite floating-point layer, arithmetic, range reduction, conversions
and stream adapters. [Skid buffering and annotated Verilog](SKID_BUFFER_AND_VERILOG_COMMENTS.md)
adds a two-entry timing cut and explicit packed-bus comments.

## Identifier contract for AXI wrappers

The core emitter prefixes signals with `s_` and escapes the few resulting
SystemVerilog keywords (for example, user `always` becomes escaped `s_always`).
AXI wrappers retain public port names using Verilog escaped identifiers. Wrapper
ports named `aclk` or `aresetn` are rejected because those names belong to the
wrapper clock/reset. Generated internal nets and the core instance receive unique
names if a user port collides. `tests/AxiNameTests.cs` covers keyword names,
clock/reset rejection, internal collisions and reset masking in emitted Icarus RTL.

| Requirement | Contract | Verification |
|---|---|---|
| AXI-NAME-01 | Accept valid graph identifiers without emitting HDL keywords as plain identifiers. | Core and wrapper keyword compilation tests. |
| AXI-NAME-02 | Reject reserved wrapper clock/reset ports clearly; avoid generated internal collisions. | Reserved-name checks and colliding-port RTL simulation. |
