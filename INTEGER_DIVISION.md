# Integer division and rounding

SharpHDL 0.10 implements unsigned and signed integer division. Choose a
combinational expression or a parallel/iterative stream block. Rounding and
exception behavior are explicit; both architectures produce identical numbers.

## Simple operators

```csharp
Expr quotient = a / b;   // unsigned, combinational, toward zero
Expr remainder = a % b;  // unsigned remainder, same W-bit width

var signedA = new SInt<W8>(a);
var signedB = new SInt<W8>(b);
var signedQ = signedA / signedB; // SInt<W8>, toward zero
var signedR = signedA % signedB; // SInt<W8>, sign follows A
```

`UInt<W>` also has `/` and `%`. Operands must have equal widths. Division does
not widen the quotient. Raw Expr always uses unsigned division, even if its bits
were produced by SignedConst; use SInt or `signed: true` to select signed division.
Raw and typed operators use Wrap on overflow and omit status flags. On division
by zero they return quotient zero and remainder A. Signed MIN/-1 returns wrapped
MIN and remainder zero. These are deterministic hardware conventions, **not C#
CPU exception behavior**. Use the explicit API to observe errors.

If both quotient and remainder are needed, call Evaluate once to share the
constructed division graph. Separate `/` and `%` calls build separate graphs;
downstream synthesis may merge them, but sharing is not guaranteed by the builder.

## Explicit combinational division

```csharp
DivisionResult result = IntegerDivide.Evaluate(
    a, b,
    rounding: DivisionRounding.NearestEven,
    signed: true,
    overflow: Overflow.Saturate);

m.Drive(q, result.Quotient);       // W bits
m.Drive(r, result.Remainder);      // W+1 bits, always signed
m.Drive(div0, result.DivideByZero);// one bit
m.Drive(ov, result.Overflowed);    // one bit
```

Evaluate has no clock or intrinsic latency: it constructs W restoring-division
steps and rounding logic as a combinational graph. Assign it to a register or Map
stage to obtain a clocked result. Supported expression widths are 1..256 bits;
large combinational dividers can be expensive and slow.

## Rounding policy

Rounding is chosen at circuit construction, not through a runtime control port.
It applies to the quotient before overflow handling. The residual is computed
consistently as **R = A - Q*B** when neither error flag is set.

| DivisionRounding | Meaning | 7/3 gives Q,R | -7/3 gives Q,R |
|---|---|---|---|
| TowardZero | Discard fractional part | 2, 1 | -2, -1 |
| Floor | Toward negative infinity | 2, 1 | -3, 2 |
| Ceiling | Toward positive infinity | 3, -2 | -2, -1 |
| NearestEven | Nearest integer, ties to even | 2, 1 | -2, -1 |
| AwayFromZero | Increase magnitude for every inexact result | 3, -2 | -3, 2 |

Nearest-even examples: 5/2 -> 2 with residual 1; 7/2 -> 4 with residual -1;
-5/2 -> -2 with residual -1; -7/2 -> -4 with residual 1. Exact divisions are
unchanged by the rounding mode. Negative divisors are supported in signed mode.

The explicit remainder is a **signed W+1-bit residual**, including for unsigned
operands. This accommodates negative residuals after rounding upward and positive
residuals as large as an unsigned W-bit dividend after division by zero. For
nonzero B and no overflow, its magnitude is less than |B|. The `%` overload uses
toward-zero division and returns the usual W-bit truncation remainder.

These policies do not globally change arithmetic elsewhere. Fixed-point conversion
still offers Rounding.Floor and Rounding.NearestEven. Compact-float basic arithmetic
uses its existing nearest-even packer; this release does not add selectable float
rounding or a direct SFixed division operator.

## Exceptions and overflow

| Condition | Quotient | Residual | DivideByZero | Overflowed |
|---|---|---|---|---|
| B=0, including 0/0 | 0 | A, widened with correct input signedness | 1 | 0 |
| Signed MIN/-1, Wrap | MIN | 0 | 0 | 1 |
| Signed MIN/-1, Saturate | MAX | 0 | 0 | 1 |
| Otherwise | Rounded quotient | A-Q*B | 0 | 0 |

Overflow means the mathematical rounded quotient cannot fit in W bits. MIN/-1
is the only overflow case for equal-width integer operands. Residual reconstruction
is not promised for flagged overflow, even under saturation. No software exception
or hardware trap is generated. Stream error results have the same latency as
ordinary results; there is no early-out timing variation.

## Parallel and iterative streams

```csharp
var m = new Module("RoundedDivider8");
var pairs = SharpHDL.Stream.Input(m, "pairs", 16, 4);
var answer = SharpHDL.Stream.Output(
    m, "answer", IntegerDivide.ResultWidth(8), 4); // 24 bits
var divided = IntegerDivide.Build(
    m, pairs, "divide", 8,
    DivideArchitecture.Iterative,
    DivisionRounding.NearestEven,
    signed: true,
    overflow: Overflow.Saturate);
SharpHDL.Stream.Connect(m, divided, answer);
```

`DivisionResult.Unpack(divided.Data, 8)` returns expressions naming the quotient,
residual and flags. They are meaningful with divided.Valid. Do not connect a
second driver to divided.Ready; connect the entire stream to a consumer.

| Eight-bit result bus | Meaning |
|---|---|
| Input TDATA[7:0] | A dividend |
| Input TDATA[15:8] | B divisor, accepted atomically with A |
| Output TDATA[7:0] | Quotient, selected signedness |
| Output TDATA[16:8] | Nine-bit signed residual |
| Output TDATA[17] | Divide by zero |
| Output TDATA[18] | Quotient overflow |
| Output TDATA[23:19] | Zero padding |
| Input TKEEP | Must be all ones; partial numeric operands are outside the contract |
| Output TKEEP | All ones, including padding/status byte |
| TLAST and TUSER | Preserved with the operand pair |

For width W, Q occupies [W-1:0], R [2W:W], divide-by-zero bit 2W+1, overflow
bit 2W+2. The remaining high bits are zero up to a whole number of bytes.
Stream widths require W divisible by four, 4..256. The output width changes from
2W to round_up_to_byte(2W+3). Generated RTL comments describe all fields.

| Property | Parallel | Iterative |
|---|---|---|
| Structure | W compare/subtract steps, combinational rounding, one elastic output stage | One restoring step reused W times, rounding at completion |
| Accept operands at E0 | Result valid immediately after E0 | Capture operands and signs |
| Compute edges | Already computed before E0 | E1 through EW |
| Earliest result transfer | E1 | E(W+1) |
| Best input interval | Every clock | Every W+1 clocks |
| While result is stalled | Hold data/flags/metadata | Hold data/flags/metadata; no next computation |
| Synchronous reset | Discard pending result | Discard computation and pending result |

For W=8, iterative accepts at E0, computes at E1..E8, and can transfer the answer
and load a new pair at E9. Rounding correction is combinational on the final
compute path; it adds no clock but can affect timing. Parallel means unrolled,
not internally pipelined. No automatic pipeline scheduling, resource sharing
between separate dividers, vendor divider IP or target frequency is implied.

## How one step works

Unsigned restoring division consumes dividend bits from most significant to least:
shift the partial remainder left, bring in the next dividend bit, compare with B,
and subtract B if possible. The comparison produces the next quotient bit.

For four-bit A=13 (1101), B=3, starting at Q=0 and R=0:

| Compute edge | Next A bit | Trial R | Subtract 3 | New R | Q so far |
|---|---|---|---|---|---|
| E1 | 1 | 1 | No | 1 | 0000 |
| E2 | 1 | 3 | Yes | 0 | 0001 |
| E3 | 0 | 0 | No | 0 | 0010 |
| E4 | 1 | 1 | No | 1 | 0100 |

The truncation answer is 4 remainder 1. Ceiling changes it to 5 residual -2.
Signed division first operates on unsigned magnitudes (including the representable
magnitude of MIN), then applies signs, rounding and overflow policy. It never uses
host division to construct runtime data-dependent results.

## Verification

The test oracle uses BigInteger.DivRem and mathematical rounding rather than
reimplementing the restoring algorithm. Tests cover every four-bit pair for both
architectures, signs, five rounding modes and two overflow policies; directed and
random cases at 8 and 32 bits; expression cases at widths 1, 3 and 65; and `/`/`%`
operator behavior. Stream tests independently predict ready/valid latency and
verify flags, padding, residuals, metadata, stalls and reset recovery.

Run the compiled test program with `--divide` for the subset, or tests/run.sh for
the full suite. See VERIFICATION.txt for executed results. No divider FPGA
placement/routing, clock-frequency measurement or physical-board test is claimed.

### Example mapped resource counts

Eight-bit operands and four-bit TUSER, Yosys 0.33 synth_ice40. These are primitive
counts, not placed logic cells or clock-speed measurements.

| Configuration | LUT4 | Flip-flops | Carry |
|---|---:|---:|---:|
| Divide8SignedIterativeFloorWrap | 181 | 69 | 59 |
| Divide8SignedIterativeNearestEvenSaturate | 206 | 69 | 67 |
| Divide8SignedParallelFloorWrap | 372 | 26 | 162 |
| Divide8SignedParallelNearestEvenSaturate | 393 | 26 | 170 |
| Divide8UnsignedIterativeCeilingWrap | 103 | 67 | 33 |
| Divide8UnsignedParallelCeilingWrap | 283 | 25 | 136 |

All six mapped netlists passed their RTL traces. The iterative design trades extra
cycles and state for fewer replicated division steps; exact area depends on rounding
and signedness. Reproduce with tests/synthesize-divide.sh.
