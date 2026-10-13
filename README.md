# SharpHDL 0.14

A small, executable C# hardware-description library inspired by MyHDL.

Read [How the compiler works](HOW_THE_COMPILER_WORKS.md) for worked counter,
width, F# FIR, CORDIC, reciprocal and packet-reducer examples, with actual IR
dumps and generated HDL.
Original implementation; not a port of MyHDL and not affiliated with it.

Build a single-clock RTL design in C#, simulate it, and emit Verilog.

Version 0.14 adds [TinyRisc16](examples/TinyRisc/ISA.md): a structural 16-bit
processor, assembler, instruction stepping, scratch RAM, memory-mapped I/O and
an independent architectural test model. The [TinyRisc HTML explorer](docs/tinyrisc/TinyRisc_Demo.html)
adds synchronized PC/register displays, waveforms and actual VeriSim WebAssembly
simulation. See [demo instructions](docs/tinyrisc/README.md) for offline use. [CPU requirements](examples/TinyRisc/REQUIREMENTS.md)
and a separate children's computer handoff are included. No board timing or ISA
compatibility is claimed.

Version 0.13 adds [explicit sequential CIL](SEQUENTIAL_CIL.md): runtime loops,
start/busy/done, bounds and assertion faults, watchdogs, and one owned uint[] RAM.
UBits<W> provides per-operation width control, and flat packet records have explicit
read-only layouts. The workbook links generated RTL directly into VeriSim.
See the updated [requirements and limits](CONSTRUCTS_AND_REQUIREMENTS.md).

Version 0.12 adds [construct coverage and requirements](CONSTRUCTS_AND_REQUIREMENTS.md),
constant fixed-point shifts with explicit rounding, a named StateMachine builder,
and an offline [interactive clock lab](docs/interactive/SharpHDL_Learning_Lab.html).
The lesson simulates actual exported graphs; it is not an embedded Verilog engine.
Additional behavioral tests cover while/do loops, nesting, break and continue.

Version 0.11 adds [behavioral CIL synthesis](BEHAVIORAL_CIL.md): ordinary static
C#, F# and VB comparisons, branches, bounded constant loops and scalar arithmetic
become hardware. Includes checked fault outputs, power-of-two shift reductions,
[explicit synchronous RAM](MEMORY.md), and a mixed-language behavioral skid pipeline.
Read the [requirements and MyHDL comparison](BEHAVIORAL_REQUIREMENTS.md).

Version 0.10 adds [integer division and five rounding modes](INTEGER_DIVISION.md),
parallel/iterative divider streams with quotient, signed residual and error flags,
and `/`/`%` for raw and typed integers. The workbook includes division and design
boundary references. Read [Why SharpHDL and what it won't synthesize](SYNTHESIS_BOUNDARIES.md).

Version 0.9 adds a [supported operator reference and selectable integer multipliers](OPERATORS_AND_MULTIPLIERS.md).
Choose Parallel or BitSerial with an explicit .NET option. Signed/unsigned full
products, ready/valid flow control, metadata and reset behavior are tested.
The classroom workbook now includes two teacher-reference pages (17 pages total).

Version 0.8 expands the classroom course into a 15-page illustrated workbook:
15 illustrations, 16 tables, printable binary cards, a display key, a hockey
capstone activity, and an answer key. Original SVG/PNG diagram assets and the
workbook builder are in `docs/course-art` and `tools`.

Version 0.7 adds [C#, F# and VB producing the same circuit](examples/ThreeLanguages/README.md),
actual CIL disassembly, mixed-language skid buffering, explicit pipelining, and
a [Go Board classroom target](boards/GoBoard/README.md) with a placed/routed bitstream.
The separate classroom proposal is in `docs/SharpHDL_Classroom_Proposal.docx`.

Version 0.6 adds [typed widths, fixed point and compact floating math](NUMERIC_TYPES_AND_MATH.md),
[C# skid buffering and annotated Verilog](SKID_BUFFER_AND_VERILOG_COMMENTS.md),
and matching C#/F# gain and complex-power stream examples.

Version 0.5 adds [hand-written Verilog for comparison](examples/Verilog/README.md)
and a [C# debug-UART mini controller](examples/DEBUG_UART.md), with an independently
tested serial RX/TX adapter.

Version 0.4 adds a configuration-driven F# filter bank and exactly-once stream
broadcast. Read [Dataflow and usefulness](examples/FSharp/DATAFLOW_AND_USEFULNESS.md)
for actual wiring, worked data, throughput limits and an honest RTL comparison.

Version 0.3 adds compiled, simulated F# FIR/oscillator/scrambler examples and a
C# packet peak reducer. See [the example gallery](examples/EXAMPLES.md).

Version 0.2 adds ready/valid streams, FIFOs, packet routing and arbitration,
CRC-32, AXI4-Stream wrappers, signed bit-vector operations, CORDIC sine/cosine,
and interpolated-table and polynomial math. See [STREAMS_AND_MATH.md](STREAMS_AND_MATH.md).
This release is a working foundation, not a complete MyHDL replacement.

## A counter

```csharp
using SharpHDL;

var m = new Module("Counter");
var enable = m.Input("enable", 1);
var count = m.Register("count", 8, 0);
var output = m.Output("value", 8);

m.NextWhen(count, enable, count + Expr.Const(8, 1));
m.Drive(output, count);

System.IO.File.WriteAllText("counter.v", m.Verilog());

var sim = m.Simulate();
sim.Tick(reset: true);
sim.Set(enable, 1);
for (int cycle = 0; cycle < 10; cycle++) sim.Tick();
System.Console.WriteLine(sim.Read(output)); // 10
```

`clk` and `rst` are implicit Verilog inputs. Reset is synchronous, active-high.
HDL module names are prefixed `h_`; user signal names are prefixed `s_`, so
reserved Verilog words are safe as C# design names. The example emits module
`h_Counter`, input `s_enable`, and output `s_value`.

## Run

With .NET 8 SDK:

```sh
cd SharpHDL
dotnet run --project tests/Tests.csproj
```

This compiles the library, runs C# assertions, and creates Verilog and
self-checking HDL testbenches in `generated/`. No NuGet dependencies.

For the full C#/Verilog differential regression on Linux/macOS, install Icarus
Verilog and either .NET 8 or Mono, then run:

```sh
bash tests/run.sh
```

The runner uses .NET when available; otherwise it uses `mcs` and `mono`.
The Mono route compiles the same sources directly. SDK project files are also
included for IDE use; the SDK route must be tested separately if only Mono is
available in the build environment.

## How the structural builder works

1. Ordinary C# creates `Module` and `Signal` objects.
2. Overloaded operators create immutable `Expr` objects. `count + one` does
   not add numbers during construction; it creates a `+` node with two children.
3. `Next` associates an expression with a register's next-state input.
   `Drive` associates an expression with a wire or output.
4. Validation rejects mixed ownership, missing drivers, multiple drivers,
   mismatched assignment widths, and combinational cycles.
5. `VerilogWriter.Emit` recursively visits expression nodes. Each operation
   becomes a sized temporary wire and a continuous assignment.
6. Registers become `always @(posedge clk)` blocks using nonblocking assignments.
7. The simulator evaluates the same graph and masks every node to its width.
   It calculates all next-state values before committing any register update.

The structural builder needs neither Roslyn nor a C# source parser: operator
overloading gives us the tree directly. It is a hardware DSL embedded in C#.
In this structural interface, ordinary C# `if` and loops execute at design construction time. Hardware
conditional selection is `Expr.Mux`, and register enables use `NextWhen`.

## Width and numeric contract

Raw Expr widths are explicit metadata, checked at elaboration. Typed structural
wrappers and behavioral UBits<W> also encode widths in generic type arguments. Widths range from 1 to 4096 bits.
`BigInteger` is used for simulator values, so a 65-bit datapath works.

Values are bit vectors with unsigned default operations. Explicit `SignedLt`,
`SignExtend`, `ArithmeticShift`, and `SignedConst` supply signed semantics.
Every expression node truncates modulo 2^width.
Binary arithmetic/bitwise operations require equal operand widths and return
that width. Comparisons return one bit. Constants must fit exactly; negative
or out-of-range constants and simulation inputs are rejected.

```csharp
var wrapped = (a + b).Resize(9);       // 8-bit add first, THEN zero extension
var full = a.Resize(9) + b.Resize(9);  // 9-bit add, preserves carry
```

These intentionally differ. Sized temporary wires preserve that distinction
in Verilog even when an expression is used in a wider assignment context.

`Resize` zero-extends or truncates high bits. `Slice(high, low)` is inclusive.
`Concat(high, low)` places the first operand above the second. Shifts are by
nonnegative constant C# integers, logical, and retain the left operand width.
Multiplication also retains operand width; resize operands first for a full product.

## API

| API | Purpose |
|---|---|
| `Input(name, width)` | Input port |
| `Output(name, width)` | Output port; requires Drive |
| `Wire(name, width)` | Internal combinational signal |
| `Register(name, width, resetValue)` | Internal clocked register; requires Next |
| `Drive(signal, expression)` | One combinational driver |
| `Next(register, expression)` | Unconditional next state |
| `NextWhen(register, enable, expression)` | Update or hold |
| `Expr.Const(width, value)` | Unsigned literal |
| `+ - * & \| ^ ~ << >>` | Hardware expression construction |
| `a.Eq(b)`, `a.Lt(b)` | Unsigned comparisons |
| `Expr.Mux(condition, yes, no)` | One-bit condition, equal-width branches |
| `Resize`, `Slice`, `Concat` | Explicit bit manipulation |
| `Validate()` | Check and freeze a design |
| `Simulate()` | Validate/freeze and create independent simulator state |
| `Verilog()` | Validate/freeze and generate Verilog source |
| `sim.Set(input, value)` | Change an input |
| `sim.Tick(reset)` | Apply one rising edge, then commit all registers together |
| `sim.Read(expression)` | Evaluate current combinational result |

Use only expressions belonging to the simulator's module when calling Read.
Inputs default to zero in C# simulation. Registers are undefined until reset;
reading an uninitialized register throws. HDL simulation should also assert
reset before comparisons. Set/Read do not generate clock edges.
The design freezes at successful validation: later structural edits fail.
Signal/Expr objects are shared safely by separate simulators because values
live in the simulator, not in the hardware description.

A held register can use `m.Next(r, r)`. A two-register swap uses
`m.Next(a, b); m.Next(b, a);`, with simultaneous update semantics.

## Verification

The test program exercises widths 1, 3, 8, 17 and 65, each for 160 cycles,
including a reset during operation. It computes independent BigInteger
expected results and checks arithmetic, logical operators, comparisons,
selection, concatenation, slices, resizing, intermediate overflow, enabled
accumulation and a one-cycle pipeline. It emits matching self-checking HDL
vectors for Icarus. Additional C# checks cover swap behavior and invalid designs.

See `VERIFICATION.txt` for the actual execution record for this package.
These are regression vectors, not a formal equivalence proof or FPGA timing test.

## Current boundaries

- No arbitrary C# source conversion: `HwMath.Cos` constructs hardware, while
  ordinary `System.Math.Cos` remains a host calculation.
- Custom finite floats now include division and math; no IEEE 754 exception/subnormal semantics.
  Raw `Expr` supports unsigned `/` and `%` (1..256 bits); variable-shift nodes and X/Z simulation remain unsupported.
- Single clock, synchronous active-high core reset. AXI adapter uses active-low
  reset and masks handshake outputs while reset is asserted.
- FIFOs use registers; no RAM-backed queues, CDC, or inferred block-RAM promise.
- Packet example requires a full 64-bit header in its first beat; no variable
  or multiword-header parser, protocol-specific Ethernet/IP stack, or malformed
  packet recovery. CRC helper and header rewrite are separate reusable blocks.
- No hierarchy in the IR, VHDL backend, VCD tracing, automatic retiming, or
  arbitrary-range trigonometric reduction. New float trig requires abs(x) < 4096 radians.
  Math ranges and approximation limits are documented explicitly.
- Only the Go Board classroom starter has target placement/routing and a generated
  bitstream at 25 MHz. Other blocks have no target timing/resource guarantee.
  No physical board execution or formal proof is claimed.

## Layout

- `src/SharpHDL/Hardware.cs`: expression graph, checks, simulator, Verilog writer.
- `examples/Counter.cs`: counter factory.
- `tests/Program.cs`: regression and HDL testbench generation.
- `tests/run.sh`: complete differential regression.
- `generated/`: actual generated counter, datapaths and testbenches.

- `src/SharpHDL/Streams.cs`: stream ports, elastic stages, FIFOs, packet arbiter.
- `src/SharpHDL/Packets.cs`: header layouts, two-queue router, CRC-32.
- `src/SharpHDL/MathHardware.cs`: CORDIC and normalized table/polynomial math.
- `src/SharpHDL/Axi4Stream.cs`: standard-name clock/reset/stream wrapper.
- `tests/FeatureTests.cs`: numerical references, scoreboards, stall and latency tests.
- `tests/synthesize.sh`: Yosys RTL synthesis and post-synthesis simulation.

## Why SharpHDL

Use ordinary .NET functions, types and finite construction loops to build reusable
hardware graphs, with explicit widths, state and streaming behavior. C#, F# and VB
can share one library. This is useful for packet pipelines, parameterized circuits
and teaching. CIL runs the builder; the FPGA runs the resulting circuit.
See [the rationale and worked boundaries](SYNTHESIS_BOUNDARIES.md).

## What it won't synthesize

SharpHDL does not synthesize arbitrary .NET methods, unrestricted software
control flow, runtime allocation, tasks, file I/O or System.Math calls. It does not
automatically schedule pipelines, infer arbitrary memories from arrays, add CDC or
provide full IEEE floating point. Host code can use these language facilities to
construct a finite supported graph, but they do not become FPGA runtime services.
See [the detailed support boundary](SYNTHESIS_BOUNDARIES.md).

Generated RTL supports [meaningful intermediate net names](NET_NAMING.md):
use `.Named("effective_address")` for design intent; unnamed expressions get
operation names, and behavioral arithmetic retains method/IL provenance.
The TinyRisc example and its browser demo include these names.

For the complete rebuild/test order, updated workbook and archived board image,
see [the regeneration audit](REGENERATION_AUDIT.md) and `tests/run-all.sh`.

The [workbook WebAssembly lab](WASM_WORKBOOK.md) runs the generated adder,
counter, state machine, loop and direct-versus-skid backpressure examples with a waveform viewer and prepared
student predictions. Fresh runs execute real Verilog in the browser.

Five further FPGA-design/instruction review rounds improved identifier handling,
rising-edge waveform controls and the guided shot-counter lesson. See
[the review record](REVIEW_ITERATIONS.md) for findings, validation and the physical
classroom checks still required.
