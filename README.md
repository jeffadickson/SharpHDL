# SharpHDL 0.14

**Developer setup:** [Build and toolchain guide](docs/BUILD_AND_SETUP.md). Run `just setup`, `just container-build`, and `just container-test` with Docker, Python 3 and just installed. The container provisions .NET 8 and the open-source HDL tools automatically. 

> **Development design:** [Feature parity and competitive coverage](docs/FEATURE_PARITY.md) · [Architecture and implementation record](docs/DESIGN.md) · [Product requirements](docs/PRODUCT_REQUIREMENTS.md) · [Testing strategy](docs/TEST_STRATEGY.md). The new debugger, compiled combinational backend, xUnit and container integration fixtures are under development; passing builds and throughput results must be confirmed by CI.

A small, executable C# hardware-description library inspired by MyHDL.

For teaching, start with the [eight short learner chapters](docs/LEARNER_CHAPTERS.md)
and [teacher pilot guide](docs/LEARNER_LED_PILOT.md). The illustrated workbook
includes this route before the longer reference lessons. Begin with one mission;
binary and waveform chapters can come later. [Review record](docs/LEARNER_REVIEW_ITERATIONS.md).

Read [How the compiler works](HOW_THE_COMPILER_WORKS.md) for worked counter,
width, F# FIR, CORDIC, reciprocal and packet-reducer examples, with actual IR
dumps and generated HDL.
Original implementation; not a port of MyHDL and not affiliated with it.

Build a single-clock RTL design in C#, simulate it, and emit Verilog.

New: [typed channels and concurrent CIL processes](STREAM_PROCESSES.md). Write
blocking `Read`/`Write` loops, attach independent hardware actors, and connect
explicit FIFOs. Ready/valid handshakes are generated internally; scalar AXI4-Stream
adapters define external packet behavior. `AttachProcess` uses one CIL instruction
per clock. New: [automatic pipeline scheduling](PIPELINE_SCHEDULER.md) via
`AttachPipeline` overlaps stateless arithmetic iterations at II=1, with explicit
logic-depth grouping, elastic backpressure and generated schedule reports.
Conditional arithmetic (`if/else`, ternary and `switch`) is supported when every
path consumes and produces exactly one token; gain-and-clipping is a worked example.
[Feedback pipelines](FEEDBACK_PIPELINES.md) add constant-initialized loop-carried
state with one iteration in flight. External II is latency + 1 by default; optional
`ForwardOnCommit` reduces it to latency (II=1 for a one-stage running sum).
Opt-in `AdvanceStateOnAccept` removes duplicate state and forwarding muxes for
eligible single-stage processes; exposed state then advances on accepted input.

A [packet checksum benchmark](examples/PacketBenchmark/README.md) now compares CIL-generated
and handwritten RTL with shared traffic tests, mapped-cell simulation, and three
iCE40 place-and-route seeds. See the [measured core results](generated/packet-benchmark/RESULTS.md): the optimized
checksum matches the handwritten reference at 35 LUTs and 34 flip-flops.
The [TinyRisc routed baseline](generated/tinyrisc-benchmark/RESULTS.md) exercises
the larger structural CPU on HX8K; it does not use this feedback optimization.

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

## Getting started with VS Code

The SharpHDL extension adds a **Verification Explorer**, a multi-design **Verilator Projects** panel, generated RTL/report navigation and an experimental hardware clock debugger. Use a standard C# extension and a Verilog/SystemVerilog extension for language editing; SharpHDL focuses on synthesis and verification workflows.

From a new clone, with Docker, Make and Python 3 installed:

```sh
make setup
make vscode
code --install-extension dist/sharphdl-0.3.0.vsix
code .
```

Then run `make tinyrisc-hello` to generate the first CPU design and use **SharpHDL Projects → TinyRisc16 → Run Verilator test**. For the full interpreter/JIT/Verilator regression, use **SharpHDL Verification → Run TinyRisc verification**.

The scheduled C# polynomial example now emits an automatic Portable-PDB-to-Verilog source map, allowing source/RTL navigation and signal-name lookup in VS Code. **Complete setup, Windows/WSL2 and macOS notes, debugging and troubleshooting:** [Getting started with SharpHDL in VS Code](docs/VSCODE_GETTING_STARTED.md). The extension's generalized project integration is in development; consult [issue #25](https://github.com/jeffadickson/SharpHDL/issues/25) for remaining acceptance work.

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
5. `LegacyVerilogWriter.Emit` (in `src/SharpHDL.LegacyRtl`) recursively visits expression nodes. Each operation
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

[Memory targets](MEMORY.md) now include Xilinx 7-series and Intel/Altera Cyclone V
and Arria 10 inference policies. [Native execution](NATIVE_EXECUTION.md) runs the
original stream methods and array algorithms on .NET without synthesis.

## Mixed RTL and scientific streams

[AXI interoperability](AXI_INTEROP.md) composes SharpHDL modules and handwritten
Verilog in a checked single-clock stream chain, with explicit native functional
models. [Scientific applications](SCIENTIFIC_APPLICATIONS.md) includes a working
signed sensor calibration/energy pipeline, numerical contracts, and a prioritized
list of remaining scientific features. Run `tests/run-axis-interop.sh` to verify
the native model and actual mixed RTL in Debug and optimized builds.

[Frame reductions](FRAME_REDUCTIONS.md) compute sum, count, signed extrema and
energy per TLAST, with overflow flags, a native PC runner, annotated result buses,
and numerical-error reports. Run `tests/run-frame-reduction.sh` for Debug and
optimized native/graph/RTL verification.

## Control and banked capture

[AXI4-Lite control, banked memory and local stream capture](CONTROL_AND_MEMORY.md)
adds independent Lite channels, byte-strobed register access, cyclic RAM banks,
and an integrated capture/readback example with native functional models.
Requirements AXIL01–AXIL05, BANK01–BANK04, CAP01–CAP03 and DMA01–DMA03 distinguish
implemented local transfers from the remaining full AXI burst DMA work.


**Pipeline debugging preview:** [Browser inspector](docs/pipeline/index.html) · [Build and usage instructions](docs/pipeline/README.md). The initial inspector replays a versioned trace from the .NET simulator rather than running a live DUT in JavaScript; see limitations in its README.
