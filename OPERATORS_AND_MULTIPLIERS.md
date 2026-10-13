# Supported operators and selectable integer multipliers

SharpHDL 0.10 supports integer multiply and an explicit choice of parallel or
bit-serial stream hardware. It also supports [integer division with explicit rounding](INTEGER_DIVISION.md). The workbook has a shorter teacher reference.

## Raw Expr support

Widths are 1..4096 bits. Each node truncates to its own width. Binary operands
need equal widths unless otherwise stated. Expr stores raw unsigned bits;
signed operations interpret those bits as two's complement.

| API | Meaning | Result and limits |
|---|---|---|
| `Expr.Const(W, value)` | Unsigned constant | W bits; checks range |
| `Expr.SignedConst(W, value)` | Signed constant | W bits; checks signed range |
| `a + b`, `a - b`, `a * b` | Add, subtract, multiply | W bits, modulo 2^W |
| `a / b`, `a % b` | Unsigned combinational quotient/remainder, toward zero | W bits; W=1..256; explicit API exposes error flags |
| `a & b`, `a \| b`, `a ^ b`, `~a` | Bitwise AND, OR, XOR, NOT | W bits |
| `a << n`, `a >> n` | Constant logical shifts | W bits; nonnegative host integer n |
| `a.ArithmeticShift(n)` | Constant signed right shift | W bits; repeats sign bit |
| `a.Eq(b)`, `a.Lt(b)`, `a.SignedLt(b)` | Equal, unsigned less-than, signed less-than | One bit |
| `Expr.Mux(c, yes, no)` | Hardware selector | Equal-width branches; one-bit c |
| `a.Resize(N)` | Zero-extend or retain low N bits | N bits |
| `a.SignExtend(N)` | Extend sign bit | N >= W |
| `a.Slice(high, low)` | Inclusive bit selection | high-low+1 bits |
| `Expr.Concat(high, low)` | Join high and low vectors | Sum of widths |
| `Expr.Lookup(W, address, values)` | Constant combinational table | W bits; 1..65536 entries fitting address; missing addresses return zero |
| `a.Describe(text)` | Annotate emitted Verilog | No behavior change |

Use `~a.Eq(b)` for inequality, `b.Lt(a)` for greater-than, and `~b.Lt(a)`
for unsigned less-than-or-equal. C# `==` is not the hardware Eq operator.
Raw Expr has no unary-minus, variable-shift or reduction operator.
Negation can be built as `Expr.Const(W, 0) - a`; reductions can be built from
bits. Simulation is two-state, without X/Z.

For eight-bit a and b:

```csharp
Expr low = a * b;                              // low eight bits
Expr full = a.Resize(16) * b.Resize(16);        // full unsigned product
Expr signedFull = a.SignExtend(16) * b.SignExtend(16);
```

Widen before multiplying: `(a * b).Resize(16)` has already lost high bits.
For unsigned 200 times 3, full is 600 and low is 88.

## Typed arithmetic

| Type | Operators with matching types | Result |
|---|---|---|
| UInt<W> | +, -, *, /, % | Unsigned W+1 addition; signed W+1 subtraction; unsigned 2W product; W-bit quotient/remainder |
| SInt<W> | +, -, *, /, % | Signed W+1 sum/difference; signed 2W product; W-bit quotient/remainder |
| SFixed<W,F> | +, -, * | W+1,F sum/difference; 2W,2F product |
| FloatValue | +, -, *, / | Same custom compact-float format |

Typed integer/fixed wrappers expose `.Bits` for logic, slicing and comparisons.
`UInt.Resize<T>` and `SInt.Resize<T>` choose Wrap or Saturate. Fixed-point
`Convert<T,G>` also chooses Floor or NearestEven rounding. Integer wrappers now have toward-zero `/` and `%`; division widths are limited to 1..256 bits. See INTEGER_DIVISION.md for exception conventions and rounding choices. Their overloaded multiplication uses parallel expression
nodes; it does not inherit a stream multiplier option from elsewhere.

Compact float provides Sqrt, Reciprocal, Log2, Log, Log10, Exp2, Exp, Sin, Cos,
Tan, Atan, Atan2, Pow, conversions and LessThan. These are custom finite formats,
not full IEEE 754. See [formats and domains](NUMERIC_TYPES_AND_MATH.md).
CORDIC and normalized ROM math have separate formats and explicit stream latencies:
see [streams and math](STREAMS_AND_MATH.md). `System.Math.Cos` on a host double
runs in software; it is not automatically replaced with a CORDIC.

## State and stream blocks

| API | Behavior |
|---|---|
| Module.Input, Output, Wire, Drive | Ports and combinational wiring |
| Module.Register, Next, NextWhen | Rising-edge state; simultaneous updates; synchronous active-high reset |
| Stream.Connect | Wire a complete ready/valid stream |
| StreamBlocks.Map | One elastic output register stage around a combinational expression |
| Pipeline | Explicit elastic register stages |
| SkidBuffer | Two entries; registered payload; input ready independent of downstream ready |
| Fifo | Register queue, depth 1..256 |
| Broadcast | Exactly-once delivery to 1..16 consumers |
| Arbitrate | Two-input round-robin packet arbitration |
| IntegerDivide.Build | Parallel or iterative division, rounding and result status |
| IntegerMultiply.Build | Selectable parallel or bit-serial integer multiply |

In the explicit Expr builder, C# if/for/function calls execute during construction. A loop can construct
repeated hardware, but is not automatically a clocked state machine. Use Mux
for signal-dependent choices and registers for state. CIL executes the builder;
SharpHDL records the graph through these APIs. The separate v0.11 CilCompiler now translates a restricted ordinary scalar-method
subset including comparisons, branches and constant loops; see BEHAVIORAL_CIL.md.

## A pragma-like architecture directive

```csharp
var m = new Module("SerialMultiply8");
var pairs = SharpHDL.Stream.Input(m, "pairs", 16, 4);
var result = SharpHDL.Stream.Output(m, "result", 16, 4);
var product = IntegerMultiply.Build(
    m, pairs, "multiply", 8,
    MultiplyArchitecture.BitSerial,
    signed: false);
SharpHDL.Stream.Connect(m, product, result);
```

Change the enum to `MultiplyArchitecture.Parallel` for a different circuit.
The ports and numeric behavior stay the same; ready/valid timing changes.
An ordinary `a * b` remains combinational. It cannot silently become a multi-cycle
operation without adding a clock and transaction control.

The option is a normal .NET argument that creates a real alternative graph.
SharpHDL does not parse custom C# #pragma text or infer directives from comments.
An attribute frontend would need explicit code to read and apply attributes;
an attribute alone does nothing. Microsoft's [C# directive reference](https://learn.microsoft.com/dotnet/csharp/language-reference/preprocessor-directives)
describes the standard compiler directives.

Other .NET languages can call this shared signature:

```fsharp
let product = IntegerMultiply.Build(m, pairs, "multiply", 8,
                                    MultiplyArchitecture.BitSerial, false)
```

```vb
Dim product = IntegerMultiply.Build(m, pairs, "multiply", 8,
                                    MultiplyArchitecture.BitSerial, False)
```

These illustrate the signature; the new multiplier harness compiles C# calls.
The existing three-language examples test the common expression/stream API.

### Bus contract

| Signal | Meaning |
|---|---|
| Input TDATA[W-1:0] | Operand A |
| Input TDATA[2W-1:W] | Operand B, accepted atomically with A |
| Output TDATA[2W-1:0] | Full unsigned or two's-complement product |
| TKEEP | Input must be all ones; partial operands are outside the contract |
| TLAST and TUSER | Preserved with the operand pair and result |
| TVALID and TREADY | Transfer occurs only when both are high at a rising edge |

Operand widths are multiples of four, 4..2048, so the 2W stream is byte aligned.
These are API limits, not FPGA capacity promises. Raw Expr supports other widths.
No byte-enable masking of numeric operands is performed. Emitted Verilog comments
identify bus packing, signedness, architecture and register roles.

### Timing contract

| Property | Parallel | BitSerial |
|---|---|---|
| Datapath | Combinational multiply then register | Shift/add accumulation; signed final subtraction |
| Input accepted at E0 | Output valid just after E0 | Operands loaded, busy asserted |
| Compute edges | None after E0 | E1 through EW |
| Earliest output transfer | E1 | E(W+1) |
| Best input interval | 1 clock | W+1 clocks |
| Output stall | Holds data and sidebands; stops input when occupied | Holds result and sidebands; waits before new work |
| Reset | Discards pending result | Discards work and pending result |
| Mapping | Synthesis chooses DSP or logic; no forced mapping directive | No multiply graph node; explicit sequential arithmetic |

For eight-bit operands, serial accepts at E0, computes at E1..E8, and may transfer
the result and accept a new pair at E9. It does not overlap computation with an
unconsumed result. A register after parallel multiplication does not pipeline
its internal arithmetic or guarantee a clock frequency.

Serial reuses accumulation arithmetic, but registers and control also cost
resources: it is not necessarily smaller at a tiny width. Mapping and clock
performance still require synthesis and implementation for the target FPGA.

### Worked example

A=3, B=5 (0101), W=4. Read B from the low bit upward. Add shifted A when that
bit is one; shift A left and B right after each computation.

| Edge | B bit | Shifted A | Total |
|---|---|---|---|
| E0 | Load | 3 | 0 |
| E1 | 1 | 3 | 3 |
| E2 | 0 | 6 | 3 |
| E3 | 1 | 12 | 15 |
| E4 | 0 | 24 | 15; output becomes valid |
| E5 | — | — | Receiver may accept 15 |

Signed mode sign-extends A and subtracts the final selected partial product,
because the highest B bit has negative weight. For -3 times -2 (B=1110),
contributions are 0, -6, -12, +24, giving +6. This handles the most-negative
operand without an absolute-value overflow special case.

## Verification

The new tests check both architectures and signs at widths 4, 8, 12 and 32.
Four-bit pairs are exhaustive. Wider vectors include random values, zero, all
ones and most-negative operands. An independent BigInteger scoreboard checks
products, exact ready/valid timing, sidebands, stalls, draining and reset recovery.
Generated RTL benches replay these inputs and independently checked expectations.

Run `tests/run.sh` for all tests. Run the compiled test program with `--multiply`
for the multiplier subset. Sixteen new benches are generated as Multiply*_tb.v.
`tests/synthesize-multiply.sh` maps four eight-bit variants to iCE40 primitives
and replays their benches. See VERIFICATION.txt for executed results. No multiplier
placement/routing or physical-board measurement is claimed.

### Measured synthesis example

Yosys 0.33, synth_ice40, eight-bit operands, 16-bit product and four-bit TUSER:

| Variant | LUT4 primitives | Flip-flop primitives | Carry primitives |
|---|---:|---:|---:|
| Unsigned parallel | 158 | 24 | 10 |
| Unsigned bit serial | 59 | 66 | 16 |
| Signed parallel | 244 | 24 | 8 |
| Signed bit serial | 69 | 66 | 16 |

These are mapped primitive counts, not placed logic-cell counts; primitives can
share physical cells. This run shows the arithmetic-versus-storage tradeoff.
All four mapped netlists passed their cycle traces. No maximum-clock claim follows
from these counts. Logs and netlists are in generated/synthesis.

See [Why SharpHDL and what it won't synthesize](SYNTHESIS_BOUNDARIES.md) for the boundary between host code and hardware.

## Fixed-point shifts in 0.12

SFixed<W,F> now supports constant `<<` (wrap) and `>>` (arithmetic/floor), keeping
W and F unchanged. `ShiftRight(n, Rounding.NearestEven)` chooses rounded scaling.
Counts must be 0..W. These are builder APIs, not supported object operations inside
CilCompiler methods. See CONSTRUCTS_AND_REQUIREMENTS.md for examples and tests.

## v0.13 extension

SEQUENTIAL_CIL.md specifies the new UBits operator matrix and clocked CIL subset.
CONSTRUCTS_AND_REQUIREMENTS.md tracks LOOP02, NUM03, TYPE02, MEM02, ERR01 and SIM02
with implemented scope and remaining limitations.
