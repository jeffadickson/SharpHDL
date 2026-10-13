# Behavioral CIL synthesis

SharpHDL 0.11 translates selected ordinary static .NET methods into combinational
hardware. This is a second frontend alongside the original Expr circuit builder.
The C#, F# or VB compiler decomposes source into CIL; SharpHDL must still interpret
its stack, signedness, control flow, overflow and widths as hardware. CIL is a
common input language, not a hardware scheduler.

## A comparison really becomes hardware

```csharp
public static int Minimum(int a, int b) {
    if (a < b) return a;
    return b;
}
var module = CilCompiler.BuildModule(
    typeof(Functions).GetMethod("Minimum"), "Minimum");
File.WriteAllText("minimum.v", module.Verilog());
```

The actual optimized C# method in `generated/behavioral/CilCsMinimum.cil.txt` is:

```text
IL_0000: ldarg.0
IL_0001: ldarg.1
IL_0002: bge.s 6
IL_0004: ldarg.0
IL_0005: ret
IL_0006: ldarg.1
IL_0007: ret
```

The two loads put symbolic input wires on a symbolic stack. `bge.s` creates a
signed comparison. SharpHDL explores both paths and joins their return values
with a mux. It does not invoke the method with trial inputs to discover its
behavior. Equivalent simplified hand-written RTL is:

```verilog
// Inputs and result are signed 32-bit integers, bit 31 is the sign.
// Combinational circuit: no clock latency and no state.
assign result = ($signed(a) < $signed(b)) ? a : b;
assign fault = 1'b0;
```

The emitted RTL uses named intermediate wires and comments; the snippet above
is simplified for teaching. F# `if a < b then a else b` and VB multiline `If`
reach the same comparison-and-selector behavior through different CIL sequences.
Actual assemblies, CIL reports, graph dumps and Verilog are in the Behavioral
example and generated directories. Identical source intent does not override
language differences: VB checked addition can fault where unchecked C# wraps.

## Supported language subset

| Source operation | CIL interpretation / hardware |
|---|---|
| int, uint, bool parameters, locals and return | 32-bit integer stack values; one-bit Boolean ports |
| Constants, assignment and local variables | Symbolic values and wiring; locals do not create registers |
| +, -, *, unary minus | 32-bit wrap; checked add/subtract/multiply also set fault |
| /, % | Signed or unsigned combinational division; native toward-zero semantics |
| &, \|, ^, ~ | Bitwise logic |
| ==, !=, <, <=, >, >= | Signed/unsigned comparison according to CIL instruction |
| <<, >> with constant or variable count | Shift network; count masked to five bits |
| if, conditional expression, early return, switch | Explore reachable symbolic paths; join with muxes |
| && and \|\| | Compiler-generated short-circuit branches; fault follows selected path |
| Constant-controlled for/while loops | Fully unroll within compilation budgets |
| Pure static helpers in the same assembly | Inline supported method bodies; reject recursion |
| Casts through byte/sbyte/short/ushort | Truncate and extend according to conversion opcode |

The native scalar type boundary is Int32, UInt32, Boolean; v0.13 also recognizes UBits<W> and explicit flat records (see SEQUENTIAL_CIL.md). The smaller casts are
supported instructions; smaller scalar parameter/local types are not added by
that fact. No Int64, floating-point, object, array, ref, instance-method, exception
handler or external-library-call synthesis. See SYNTHESIS_BOUNDARIES.md.

`CilOptions` bounds total visited instructions (20,000), symbolic branch depth
(128), and helper nesting (16). Runtime-dependent backward branches are rejected;
other unending or excessively expanding paths exhaust the budget. Returns are
merged rather than performing optimized SSA merges at every block. Large branch
trees can duplicate logic. This is a restricted prototype, not arbitrary .NET HLS.

## Widths and loop counters

```csharp
[return: PortWidth(8)]
public static int Narrow([PortWidth(8)] int a, [PortWidth(8)] int b) {
    return a < b ? a + 2 : b - 1;
}
```

Signed eight-bit inputs represent -128 through 127 and are sign-extended to 32
bits. `uint` inputs zero-extend. Arithmetic retains CIL's 32-bit semantics; the
return explicitly keeps its low eight bits. Attributes apply at a Compile boundary,
not to each inlined helper call. They do not create an eight-bit software int.

For `for (int i=1; i<=12; i++) a += b`, the present combinational compiler resolves
the loop index while translating. The emitted graph contains twelve additions
and **no index register**. A warning to change `i` to four bits would be misleading.
The synthesis report and graph provide the useful evidence instead.

A sequential loop is different: an index retained across clocks needs storage.
Choose an explicit register width today. An unsigned counter spanning 0..12 needs
four bits. Include initialization, the terminal value (often 13), increment-before-
test behavior and all exit paths when proving a width. Signed +12 needs five bits.
An automatic range-analysis pass with a proven bound and optional width advisory
is a future requirement; it is not implemented. Do not silently narrow ordinary
32-bit state and change wrap or checked-overflow behavior.

## Multiply and divide by powers of two

| Expression | Implemented CIL lowering |
|---|---|
| unchecked x * 2 | Left shift by one, retaining low 32 bits |
| checked x * 2 | Shift plus an overflow check using the extended result |
| uint x / 2 | Logical right shift by one |
| int x / 2 | Add one when negative, then arithmetic right shift |
| int x % 2 | x minus twice the corrected quotient |

More generally, division by positive 2^k adds a negative-input bias of 2^k-1
before the arithmetic shift. Thus -3/2 is -1, not -2. The implementation recognizes
literal power-of-two operands; this optimization is in the CIL frontend. Raw Expr
operations retain their existing API and may be optimized later by FPGA synthesis.
These are fixed wiring shifts, with the signed correction logic where needed.

## Faults and explicit state

`CilCompiler.Compile(method, Expr[], options)` returns `Value`, `Fault`, and `Report`.
Fault marks checked add/subtract/multiply overflow, division by zero, and signed
MIN/-1 division or remainder overflow. Tests compare both divide and remainder to
the actual CLR. The selected branch alone contributes its faults. Result bits are
unspecified when fault is one. This is an explicit hardware status convention;
there is no exception unwinding. BuildModule always exposes a fault output.

To create state, compile a next-state function with register/input expressions
and pass Value to `Module.Next`. Handle Fault explicitly if it can occur. A local
variable or method call does not introduce a clock boundary. To insert a pipeline
stage, wrap a compiled function in `StreamBlocks.Map` or use explicit registers.

`examples/Behavioral/Program.cs` wires an ordinary F# Minimum through Map, a C#
skid buffer and an ordinary VB arithmetic method through Map. The 40-bit internal
bus holds result in bits 31:0, fault in bit 32, and zeros in 39:33. TLAST, TKEEP and
TUSER are preserved. Under continuous readiness the earliest transfer is three
edges after input acceptance; stalls change latency. The test checks order, values,
fault propagation, sidebands and backpressure against native F#/VB execution.

## Verification and reproduction

Use .NET 8 SDK plus Python, Icarus and Yosys:

```sh
export DOTNET_ROOT=/path/to/dotnet-sdk
bash tests/run-behavioral.sh
export YOSYS_DATDIR=/path/to/yosys/share
bash tests/synthesize-behavioral.sh
```

The direct compiler driver avoids NuGet dependencies. The v0.11 release passed 62,024
.NET oracle/simulator assertions, 62 generated RTL benches, ten structural checks,
and six iCE40-mapped RTL benches. Optimized and unoptimized C# methods are tested.
The RAM256 example maps to one SB_RAM40_4K plus interface logic. These are simulation
and synthesis results, not formal equivalence or physical-board measurements.

Version 0.12 extends the loop tests with While4, DoWhile4, BreakContinue and
NestedLoops in optimized and debug CIL: 70,360 assertions and 70 generated RTL
benches pass. Explicit clocked state can now use StateMachine; it does not change
the combinational CIL frontend into an automatic sequential loop scheduler.

## Version 0.13

See SEQUENTIAL_CIL.md for BuildSequential, runtime loops, one owned uint[] RAM,
UBits<W> operator semantics, read-only packet records and Hardware.Assert.
BuildModule now also exposes assertion_failed. Existing combinational methods
still unroll constant loops and never gain implicit clock boundaries.
