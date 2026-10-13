# F# hardware examples

These are compiled F# programs that call SharpHDL's C# library, build real RTL,
run the library simulator, and generate Verilog plus self-checking testbenches.
They are not pseudocode or F# code translated by a source parser.

## Run from the SharpHDL directory

With a normal .NET 8 SDK installation:

```sh
dotnet run --project examples/FSharp/Examples.fsproj -- generated/fsharp
```

For F# execution plus generated-Verilog tests, install Icarus Verilog and run:

```sh
bash tests/run-fsharp.sh
```

For an environment where MSBuild cannot run, a direct-compiler path is included:

```sh
python3 examples/FSharp/build_direct.py /path/to/dotnet-install
/path/to/dotnet-install/dotnet build/fsharp/FSharpExamples.dll generated/fsharp
```

The direct route uses the SDK's C# Roslyn compiler, F# compiler, .NET 8 reference
assemblies, and FSharp.Core. It compiles the library and examples without NuGet
or MSBuild. Do not package that SDK into the source archive.

For the strongest complete dataflow example, start with
[the configurable filter bank](DATAFLOW_AND_USEFULNESS.md). It shows real stream
wiring, fan-out, queues and arbitration, with practical limits.

## 1. A filter generated from a coefficient list

`Designs.fir [1; 3; 3; 1]` constructs a four-tap signed FIR:

    y[n] = x[n] + 3*x[n-1] + 3*x[n-2] + x[n-3]

The input is a signed 16-bit sample encoded as a two's-complement bit pattern.
The output is the signed 32-bit full weighted sum. DC gain is eight: divide by
eight with an arithmetic shift if unity gain is desired. This coefficient set
is the binomial smoothing filter, with a zero at the Nyquist frequency.

Here is the actual expression-building portion from Designs.fs:

```fsharp
let compute (sample: Expr) =
    List.zip coefficients (sample :: List.map expr delays)
    |> List.map (fun (c, x) -> x.SignExtend(32) * signed 32 c)
    |> List.fold (fun sum product -> sum + product) (bits 32 0)
```

`List.map` creates a multiplier expression for each coefficient. `List.fold`
connects those results into an adder chain. Synthesis can simplify constant
multiplications such as times three. This is a parallel direct-form datapath,
not one multiplier reused over successive clock cycles. No automatic balancing
or retiming of the adder chain is implemented.

The output has one elastic stage. The delay line advances only when the input
handshake occurs. The filter's sample index counts accepted samples, so stalls
and valid bubbles do not insert zeros or repeat samples. History starts at zero
and continues across Last boundaries. Last and User track their corresponding
output samples. Input Keep must be 3; output Keep is 15. Arithmetic wraps at
32 bits for arbitrary coefficient lists; the default list cannot overflow for
16-bit inputs. An empty coefficient list is rejected.

The test checks 256 signed samples, including both extrema, against an independent
integer recurrence with randomized source bubbles and output stalls.

## 2. A phase accumulator driving CORDIC

`Designs.oscillator()` builds a 24-bit phase accumulator and a shared sine/cosine
CORDIC pipeline. `phase_step` is a configuration input. For a constant step D,
the phase advances D/2^24 turns per accepted sample. The supplied test uses
D=2^18, giving 64 samples per revolution.

Cosine occupies output bits 31:0; sine occupies 63:32. Both are signed Q8.24.
The output User field is an eight-bit sample index. Last is always zero: this
is a continuous stream. Valid bubbles from reset/pipeline fill are expected.

This is a sample-indexed oscillator. Backpressure pauses input generation once
the pipeline fills. It is not a wall-clock DDS suitable for an unstalled DAC
without an additional fixed-rate consumption policy. A changed phase_step is
used on the next accepted accumulator update; the current offered phase stays
stable. Configuration must be synchronous to the same clock domain.

The test checks 160 output samples under long and random stalls. Maximum
observed component error was 2.3320064e-6.

## 3. Stateful stream composition

`Designs.scramblerRoundTrip()` builds two eight-bit XOR scramblers in series:

```fsharp
let scrambled = input |> fun stream -> scramble m stream "encode"
let decoded = scrambled |> fun stream -> scramble m stream "decode"
```

Each stage uses its own eight-bit Galois LFSR, initial state 0xA5, feedback mask
0xB8, right shift, and one state update per accepted byte. The same pseudorandom
byte sequence is XORed twice, recovering the input. The stages may transfer on
different cycles but remain aligned by accepted-byte index.

This demonstrates F# partial application, pipelines, and locally encapsulated
state. It is an educational scrambler, not encryption or a claimed standardized
communications scrambler. Keep must be one; Last is forwarded and does not reset
the LFSR. Both stages must reset together. Output latency is two unstalled edges.
The test checks 300 bytes and metadata with independent source bubbles/stalls.

## How F# maps to the hardware API

| F# construct | Hardware-building meaning |
|---|---|
| `x + y`, `x * y` for Expr operands | Overloaded C# operators create expression nodes |
| `x ^^^ y`, `x >>> 1` | XOR and logical right-shift nodes |
| `x.ArithmeticShift(n)` | Explicit signed right shift |
| `List.map` / `List.fold` | Run during elaboration to construct repeated hardware |
| `|>` | Composes ordinary host functions that build/connect hardware |
| `Expr.Mux` | Hardware conditional selection |
| Ordinary F# `if` / pattern matching | Host-side elaboration control flow |
| `Func<Expr,Expr>` | Explicit delegate passed to the C# stage-building API |
| `signal :> Expr` | Upcast used when resolving overloaded expression operators |

The same Signal and Expr objects feed the same simulator and Verilog backend
regardless of the .NET language that created them. Only construction syntax differs.


## Typed numeric streams (0.6)

`NumericDesigns.fs` adds a signed Q8.16 saturating gain pipeline and Compact24
complex power (`real*real + imaginary*imaginary`). Both use the new C# skid
buffer through the same .NET API. Both are compiled, simulated with independent
expected values and backpressure, and emitted to Verilog by `Program.fs`.
See [numeric types and math](../../NUMERIC_TYPES_AND_MATH.md) for the worked
scaling and float layouts. The float implementation is currently a direct
combinational network with stream output storage; it has no automatic internal
pipelining or target-frequency guarantee.
