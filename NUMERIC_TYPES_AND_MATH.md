# Bit widths, fixed point, compact float, and streamed math

SharpHDL 0.6 implements these features in `src/SharpHDL/NumericTypes.cs` and
`CompactFloat.cs`. They construct ordinary SharpHDL expression graphs, which
both the simulator and the Verilog writer execute. No runtime .NET calls are
hidden inside the hardware. The C# and F# examples are compiled and tested.

## Widths belong to the type

```csharp
var a = new UInt<W12>(module.Input("a", 12));
var b = new UInt<W12>(module.Input("b", 12));
var sum = a + b;                    // UInt<Sum<W12,W1>>: 13 bits
var product = a * b;                // UInt<Sum<W12,W12>>: 24 bits
var difference = a - b;             // SInt<Sum<W12,W1>>: 13 bits
var limited = sum.Resize<W12>(Overflow.Saturate);
var wrapped = sum.Resize<W12>(Overflow.Wrap);
```

`Sum<A,B>` is a type-level width expression: its `Value` adds the two marker
values during elaboration. Built-in markers cover W1..W64 and F0..F32. Define
another struct implementing `IBitCount` for other counts, up to the expression
engine's 4096-bit limit. These are normal generic CLR types, available to F#.

The compiler checks which wrapper types an operation accepts. The constructor
checks the actual `Expr.Width` at elaboration time. This is a combination of
static API checking and elaboration-time validation, not dependent typing:
C# does not prove an arbitrary marker's integer value at compile time. In
particular, `Sum<W12,W1>` and `W13` are distinct CLR types even though both
elaborate to 13 bits. Rewrap `.Bits` or use an explicit resize to name the result
with another equivalent width marker. Mixed-width arithmetic requires explicit
conversion. No implicit truncation is added by these wrappers.

| Operation on equal-width operands | Result width | Fraction bits |
|---|---:|---:|
| `UInt<W> + UInt<W>` | W+1, unsigned | 0 |
| `UInt<W> - UInt<W>` | W+1, signed | 0 |
| `UInt<W> * UInt<W>` | 2W, unsigned | 0 |
| `SInt<W> +/- SInt<W>` | W+1, signed | 0 |
| `SInt<W> * SInt<W>` | 2W, signed | 0 |
| `SFixed<W,F> +/- SFixed<W,F>` | W+1, signed | F |
| `SFixed<W,F> * SFixed<W,F>` | 2W, signed | 2F |

The older raw `Expr` API retains its original equal-width, modulo arithmetic.
For example, adding two raw 8-bit expressions produces an 8-bit expression.
The new wrappers explicitly extend operands before creating that operation.

## A worked fixed-point gain stage

```csharp
var sample = new SFixed<W24,F16>(input.Data);
var product = sample * SFixed<W24,F16>.Constant(1.5);
var result = product.Convert<W24,F16>(
    Rounding.NearestEven, Overflow.Saturate);
```

`W24,F16` means 24 total bits, including sign, with 16 fractional bits:
`value = signed(bits) / 65536`. Its range is -128 through 128 - 2^-16.
Here “Q8.16” counts the sign among the eight integer bits.

For sample 2.25, the stored integer is 147456; constant 1.5 is 98304.
The compiler runs overloaded `*`, which constructs two sign extensions and a
48-bit multiplication. Its integer result is 14495514624 and has 32 fractional
bits. Conversion removes 16 fractional bits, producing 221184, or 3.375.
The hardware includes round and saturation logic; no CPU floating point is
involved in processing a sample.

Rounding is explicit: `Floor` rounds toward negative infinity;
`NearestEven` resolves exact half-way cases toward an even retained integer.
Thus 2.5 -> 2, 3.5 -> 4, -2.5 -> -2, and -3.5 -> -4 when converting to an integer.
`Saturate` clamps after rounding; `Wrap` retains low bits. Increasing fraction
bits left-shifts after sign extension. Fixed constants are rounded nearest-even
at elaboration and rejected if they do not fit, rather than silently wrapped.

See executable `examples/NumericExamples.cs` and `examples/FSharp/NumericDesigns.fs`:

```fsharp
let applyGain (bits: Expr) =
    let sample = SFixed<W24,F16>(bits)
    let product = sample * SFixed<W24,F16>.Constant(1.5)
    product.Convert<W24,F16>(Rounding.NearestEven, Overflow.Saturate).Bits

input
|> fun s -> StreamBlocks.SkidBuffer(m, s, "input_cut")
|> fun s -> StreamBlocks.Map(m, s, "gain", 24, Func<Expr,Expr>(applyGain))
|> fun s -> StreamBlocks.SkidBuffer(m, s, "output_cut")
|> fun s -> Stream.Connect(m, s, output)
```

## Compact floating-point representations

The new format follows the useful small-ROM-era idea of a sign, exponent and
normalized significand. It is an original format, not IEEE 754 and not a
binary-compatible reproduction of Level II BASIC's representation or ROM code.

| Property | `FloatFormat.Tiny16` | `FloatFormat.Compact24` |
|---|---|---|
| Packed layout | sign[15], exp[14:10], fraction[9:0] | sign[23], exp[22:16], fraction[15:0] |
| Exponent bias | 15 | 63 |
| Significand precision, including hidden bit | 11 bits | 17 bits |
| Smallest positive normal | 2^-14 | 2^-62 |
| Largest finite magnitude | (2-2^-10) × 2^15 = 65504 | (2-2^-16) × 2^63 |

For ordinary values:

`value = (-1)^sign × (1 + fraction / 2^F) × 2^(exponent - bias)`.

For example, Compact24 encodes 1.5 as `0x3F8000`: sign=0, exponent=63,
fraction=0x8000. Adding 1.5 + 1.5 gives 3.0, `0x408000`.

All exponent-zero bit patterns read as zero; generated zero is canonical all
zero bits. There are no subnormals or negative-zero semantics. All exponent-all-
ones patterns read as **NaR**, “not a real result”; generated NaR has fraction=0
and sign=0. Invalid domains and division by zero return NaR. NaR propagates,
including `Pow(NaR,0)`. There are no exception flags, infinities or traps.
Finite overflow saturates to the largest magnitude with the result's sign.
The basic packer rounds nearest-even before checking whether the resulting
exponent underflows; underflow flushes to zero. `Exp2` explicitly flushes inputs
below the smallest-normal exponent, before table evaluation.

`FloatFormat.Encode/Decode` are host helpers for constants and test benches.
Host NaN/infinity encode as NaR. `FloatMath.FromFixed` and `ToFixed` build
hardware conversion networks; `ToFixed` requires explicit rounding/overflow
and maps NaR to zero. `FloatMath.Convert(value, targetFormat)` changes float
precision/exponent range with nearest-even rounding and the target saturation/
flush rules. `FloatMath.LessThan` is false if either operand is NaR.

Other custom formats can use 3..7 exponent bits and 2..24 fraction bits.
Stream payloads must also be byte-aligned. Verification focuses on Tiny16 and
Compact24; the configurable combinations are not all exhaustively tested.

## What each operation becomes

| API | Implemented hardware strategy | Domain / behavior |
|---|---|---|
| `a+b`, `a-b` | Exact common-scale integer alignment, signed add, leading-one normalization, nearest-even pack | All finite inputs; saturation/flush |
| `a*b` | Full significand product, exponent sum, normalization/round | All finite inputs |
| `a/b`, `Reciprocal` | Unrolled restoring quotient, guard bits and jammed remainder, normalization/round | Zero denominator -> NaR |
| `Sqrt` | Exponent parity reduction, unrolled integer square-root, remainder jamming, normalization/round | x >= 0 |
| `Log2` | Extract exponent; 1024-interval mantissa ROM with interpolation; add exponent | x > 0 |
| `Log`, `Log10` | Log2 followed by a rounded float constant multiply | x > 0 |
| `Exp2` | Separate integer/fraction; 1024-interval fraction ROM; reconstruct exponent | Finite x; overflow saturates, underflow flushes |
| `Exp` | Multiply by log2(e), then Exp2 | Finite x |
| `Sin`, `Cos` | Radians-to-turns reduction and 30 unrolled CORDIC rotations | abs(x) < 4096 radians; otherwise NaR |
| `Tan` | Sin/Cos quotient | Same bounded range; large pole-adjacent errors possible |
| `Atan` | Reciprocal reduction to [0,1], interpolated ROM, sign/quadrant restoration | All finite x |
| `Atan2(y,x)` | Atan(y/x) plus quadrant/axis handling | (0,0) -> NaR |
| `Pow(a,b)` | Exp2(b×Log2(abs(a))); integer parity handling for negative a | Negative base requires integer b; 0^0=1, 0^negative=NaR |
| `Abs`, unary `-` | Sign manipulation with canonical zero/NaR handling | All inputs |

For abs(x) < 2^-12, Sin and Atan return x and Cos returns 1 to avoid fixed-point
CORDIC/table noise overwhelming a tiny answer. Zero has exact Sin=0, Cos=1,
Atan=0. These are approximations, not correctly-rounded transcendental claims.

ROM bases and interval differences are generated during elaboration using
host `Math` functions. Only constants, ROM addresses, multipliers, additions,
shifts and muxes reach the HDL. The new transcendental ROMs use 1024 intervals
and 32 fraction bits. Existing `HwMath.RomMath` and `PolynomialMath` remain
available for normalized fixed-point functions with smaller pipelines.

Calling ordinary `System.Math.Cos` on a hardware expression is not intercepted.
Call `FloatMath.Cos` on a `FloatValue`, or the existing `HwMath.Cos` stream block.
There is no Roslyn source translator or automatic replacement pass.

## Functions and streams

Pure numeric functions build combinational expression graphs. Stream adapters
supply transaction storage and carry metadata:

```csharp
var f = FloatFormat.Compact24;
var logarithm = FloatMath.Unary(m, input, "logarithm", f, FloatMath.Log2);
var isolated = StreamBlocks.SkidBuffer(m, logarithm, "ready_cut");
Stream.Connect(m, isolated, output);
```

`Unary` has one elastic output register stage. It holds output data and metadata
under backpressure. Its ready path is combinational; the skid buffer breaks
that path and adds another cycle of minimum latency. Supply full TKEEP on
numeric operands; partial-byte float arithmetic is not defined. Last and User
are transported with the beat. Width-changing maps emit all-one output Keep.

A binary operation takes **one packed transaction**, `{B,A}`, so there is no
ambiguity about pairing independently stalled operands:

```csharp
var power = FloatMath.Binary(m, complexInput, "power", f,
    (real, imaginary) => real*real + imaginary*imaginary);
```

Compact24 input is 48 bits: real[23:0], imaginary[47:24]. Output is 24 bits.
For (3,4), two significand multipliers and an adder generate 25. C# and F# share
this implementation; the F# example builds it with ordinary functions and pipes.
An application with two independent input streams must add an explicit join or
pairing protocol before calling this adapter.

The new float networks are deliberately direct implementations. Addition uses
a wide exact grid; division, square-root, and CORDIC loops are **fully unrolled**.
A single stream output register does not make a long combinational path fast.
Large composed functions such as Pow can use substantial logic and need
architecture-specific pipelining or resource sharing for a real clock target.
The existing fixed-point CORDIC has internal elastic stages; the new float
CORDIC is combinational inside the unary wrapper. No timing closure, DSP/BRAM
mapping, initiation-interval guarantee at a chosen frequency, or FPGA board
execution is claimed. Do not mistake sampled accuracy for an error proof.

## Verification

`tests/NumericTests.cs` uses an independent BigInteger rational oracle for
add/subtract/multiply/divide, including cancellation, extremes, invalid values,
rounding and random bit patterns. Fixed-point nearest-even/saturation is
exhaustively checked for a small 8-to-4-bit conversion. Math tests compare
against double references evaluated on the **decoded, quantized inputs**;
additional square-root/reciprocal/log samples span the finite exponent range.
Generated Verilog is checked against the simulated expression graph.

C# and F# stream examples additionally check their results, metadata, ordering,
stalls and drain. See `generated/numeric_accuracy.txt`, `VERIFICATION.txt`, and
`tests/synthesize-numeric.sh` for the actual checks and representative RTL
synthesis/post-synthesis simulations. The archive includes generated examples.
# Operator inventory

See [supported operators and multiplier architectures](OPERATORS_AND_MULTIPLIERS.md)
for the complete API table and selectable parallel/bit-serial integer streams.


## Integer division added in version 0.10

Raw Expr and typed UInt/SInt now support `/` and `%`, with toward-zero rounding.
For five selectable rounding policies, signed residuals, divide-by-zero and overflow
flags, and parallel/iterative stream architecture, see [integer division](INTEGER_DIVISION.md).
This addition does not change fixed-point conversion or compact-float rounding;
direct SFixed division remains unsupported.
