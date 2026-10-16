# SharpHDL modular architecture and migration

## Decision (2026-10-14)

One repository, three primary assemblies:

| Assembly | Ownership | Reference rule |
|---|---|---|
| `SharpHDL.Core` | Hardware types, versioned IR contracts, validation, structural primitives and backends' common semantics | No dependency on HLS or Simulation |
| `SharpHDL.HLS` | CIL language analysis, processes, scheduling and high-level hardware lowering | References Core |
| `SharpHDL.Simulation` | Reference interpreter, compiled simulator, event-breakpoints, replay and trace engines | References Core |

Support products, including Verilog exporter, VS Code extension, live debugger and tests, remain in the same repository.

## Migration status

**The project scaffolding and dependency direction are committed; implementation migration is NOT finished.** The existing `src/SharpHDL/SharpHDL.csproj` is preserved as the compatibility assembly and still contains the historical structural, HLS, simulator and Verilog implementation. New assemblies currently contain initial contracts, not duplicate implementations. The new `HardwareNode` representation is *not yet connected* to the production compiler. No class has been claimed migrated based on creating its destination project alone.

This staged approach avoids breaking public consumers while extensive `internal` references and combined source files are untangled. The key follow-on work is a real migration with end-to-end tests:

1. Inventory ownership and internal dependencies among current source files. Keep hardware semantics explicit and invariant across existing frontends.
2. Move the foundational expressions, signals, registers, RAM model and typed IR into Core. Separate Verilog emission from simulator evaluation; preserve public signatures through compatible references or documented forwards.
3. Move CIL/process frontends, pipeline scheduler and high-level numerical kernels into HLS. Both high-level and explicit structural interfaces must produce the same canonical IR.
4. Move reference and compiled sequential engines, checkpoints, debug session, waveforms and breakpoints into Simulation. Do not duplicate simulator state or depend on HLS types.
5. Update all example/test project references and assembly usage. Add clean-clone build, binary compatibility tests, C# / F# / VB smoke tests, CPU/UART differential simulation and a dependency-cycle test.
6. Retire the old assembly only with compatibility evidence and a migration version; don't silently break existing packages or example code.

### Build

```sh
make setup
just build
make container-verify
```

The provisioning script restores the new three projects along with the legacy projects. The `just build` and container smoke workflow compile them. Docker is still the bootstrap prerequisite for automated tool provisioning. New build/test results are not verified in this session.

## Explicit non-goals

We are not creating three repositories or three competing hardware languages. HLS and the structural API remain complementary authoring styles. The simulator runs the elaborated hardware design, never the source-level CIL application as though it were ordinary software.

## 2026-10-14 Core IR implementation increment

`src/SharpHDL.Core/HardwareGraph.cs` is a real backend-neutral immutable topological graph validator. It checks nonnegative unique node IDs, earlier-node-only operand references (rejects cycles and forward references), widths 1–4096, binary/unary/mux operand signatures and explicit unsupported operation types. `CoreHardwareGraphTests` is included in the existing xUnit suite through a direct Core project reference. `tests/check-project-layers.py` enforces HLS→Core and Simulation→Core references with no unexpected cross-layer dependencies; container smoke runs this check.

**Not yet migrated:** Production `Module`/`Expr`, CIL lowering, `Simulator`, Verilog and debugger types still live in the compatibility assembly. The new graph is not yet wired to synthesis or simulator execution. Existing end-to-end tests are not known to have passed after this change. The next extraction must use a genuine shared IR adapter without duplicate hardware state or breaking consumers; binary compatibility implications require explicit review.

## 2026-10-15 — first production graph-to-Core lowering

The legacy compatibility assembly now references `SharpHDL.Core` and exposes
`Module.ExportCoreGraph()`. This performs **real lowering** of a validated
production `Module` into the immutable, width-checked Core graph for the
currently supported expression subset: input/state, constants, add/subtract,
multiply, bitwise AND/OR/XOR/NOT and mux. Combinational outputs and register
next-state cones are included; pre-edge register state is represented as an
explicit input boundary. Literal values and signal names are preserved.

`SharpHDL.Simulation.HardwareGraphEvaluator` evaluates this canonical graph
without depending on the legacy assembly or HLS frontend. Arithmetic is
two-state, unsigned and masked to the declared hardware width. A differential
test compares the new evaluator with the original simulator for 300 clock
edges, including a stateful 8-bit multiply-accumulate with enable control.
Additional tests reject unsupported operators, missing input state and
out-of-range values. The new evaluator is an independent combinational
snapshot evaluator; the original simulator remains responsible for memory,
clock scheduling and other unsupported operations.

**Migration status:** This is a supported-subset compatibility bridge, not
a claim that all legacy hardware now executes through Core. In particular,
shifts, slices, comparisons, signed operations, RAM, DSP primitives,
concatenation, pipeline control and multi-clock semantics are not yet lowered
by `ExportCoreGraph`; those fail explicitly rather than being silently
misrepresented. HLS production scheduling and legacy simulator implementations
have **not** moved to their new assemblies. The next steps are extending the
canonical operation contract and lowering to cover full TinyRisc, then moving
simulation execution and frontends without breaking the public API. Keep
issue #24 open until end-to-end parity and clean builds pass.

## 2026-10-15 — expanded canonical operations and synchronous memory

The production `Module.ExportCoreGraph()` bridge now lowers constant shifts
(left, logical right, arithmetic signed right), slices, resizing, sign
extension, concatenation, equality and signed/unsigned comparisons, ROM
lookup, full-precision direct DSP48E2 multiplication, and RAM read expressions
in addition to the earlier arithmetic and mux subset.

The `HardwareGraph` validator checks operation-specific widths, slice
ranges, constant shift offsets, ROM contents/address capacity, DSP48E2
unsigned 26x17 limits, and explicit RAM identities. Immutable ROM and RAM
initialization payloads are copied on export. No missing memory values are
treated as zero.

RAM is represented with `HardwareMemoryPort` (read/write address, enable,
data and initial contents). `HardwareGraphEvaluator` reads a supplied
pre-edge RAM snapshot; `HardwareMemoryTransitions.Commit` separately applies
the evaluated writes and returns a new immutable snapshot. This preserves
synchronous read-before-write behavior, including disabled reads and writes.
Reset behavior remains owned by the sequential controller: resetting clears
the RAM output register but does not clear the memory cells.

Differential tests now cover 300 random bitfield/ROM vectors, direct DSP
arithmetic, and 180 RAM cycles with simultaneous read/write and disabled
read cases. These tests are part of `make core-ir-verify`.

**Still not migrated:** the main legacy simulator's event loop, HLS CIL
compiler/scheduler, the Verilog backend, and broader memory/bus/multiclock
semantics. The canonical evaluator is independent but not yet the default
runtime. End-to-end test status must be checked against CI before closing
issue #24.

### TinyRisc16 end-to-end canonical-IR regression

The unit suite now compiles the real `examples/TinyRisc/TinyRisc.cs`
design and exports its full combinational, register, ROM and scratch-RAM
cones into Core. The regression compares output ports against the legacy
simulator over 60 clock edges with a NOP/NOP/HALT program. It applies
register next-state values and RAM writes through the independent Core
evaluator/transition APIs and uses actual ROM lookup and synchronous
read-before-write semantics. This is a **differential verification path**,
not yet a replacement for the production simulator.

Run `make core-ir-verify` for the Core IR validation, arithmetic, memory
and TinyRisc parity tests. The full xUnit workflow additionally checks
Windows and Linux. Results for this newly added TinyRisc canonical test
must be confirmed from the corresponding CI run before declaring acceptance.

### Clocked canonical simulation engine

`SharpHDL.Simulation.HardwareGraphMachine` now executes a complete
two-state clock step directly from Core IR. It owns external input values,
register state and immutable RAM snapshots; `Tick()` evaluates all
combinational cones from pre-edge state and atomically commits register
next-state and RAM writes. `Reset()` resets registers without erasing
RAM cells, as in the original simulator. `ReadOutput()`,
`ReadRegister()` and `SetInput()` expose a minimal independent runtime.

A new 120-cycle RAM/register differential regression compares this clocked
machine against the original `Module.Simulate()` implementation, including
read-before-write and RAM retention across reset. The compatibility
`Module` still performs the initial lowering, but the new clocked
execution itself has **no reference to legacy Module or HLS**. This is
the first independently usable sequential simulation engine in the
Simulation assembly. Multiclock, four-state, waveform and rewind behavior
remain on the legacy path pending migration.

## 2026-10-15 — production compatibility and first independent HLS consumer

The existing `SharpHDL.Module` API now offers
`module.SimulateCore()`, a compatibility factory that exports its
validated design into Core IR and constructs a
`SharpHDL.Simulation.HardwareGraphMachine`. The independent engine
owns all clock stepping, RAM snapshots and register commits; the
legacy assembly supplies only the initial lowering. Existing
`module.Simulate()` remains unchanged for regression comparison.
An 80-cycle TinyRisc16 test drives both engines through the public
factory and compares output ports at every clock edge.

The `SharpHDL.HLS` assembly now contains
`HlsGraphAnalyzer.Analyze(HardwareGraph, stageBudget, HlsCostProfile)`,
the first independent HLS pass over **real production canonical IR**.
It analyzes operation dependency paths, separately counts ordinary
multiply and direct DSP nodes, and reports an abstract critical-path
cost and suggested stage count. Unlike the legacy depth-only policy,
multiplication, DSP multiplication, add/compare and memory operations
can have distinct configurable weights. The pass is tested against
real `Module.ExportCoreGraph()` output, including TinyRisc16 and
direct DSP multiplication.

**Important distinction:** HLS graph analysis does not insert
pipeline registers, change initiation intervals, or guarantee timing.
Its costs are arbitrary planning units, not characterized target
delays. The legacy production CIL pipeline scheduler remains in
place until a compatible IR-driven ready/valid scheduling backend
is implemented and differentially verified.

Standalone Roslyn C#/F#/VB build scripts now include the independent
Simulation source files to preserve clean-checkout compilation of
the compatibility API. `make core-ir-verify` includes the new HLS
analysis and TinyRisc compatibility regressions.

### Canonical IR optimization pass

`SharpHDL.HLS.HlsConstantFolder.Fold(graph)` now implements an
independent, side-effect-free HLS optimization on validated Core IR.
It folds fully constant arithmetic (with modulo-width truncation),
bitwise logic, comparisons, mux, shifts, slices, concatenation,
sign-extension and ROM lookup. It preserves every node ID and
source name so port maps and provenance references remain stable.
Register/state boundaries and RAM reads/writes are never folded.

This is an actual transformed graph, not a report. Tests compare the
folded graph's output with the legacy simulator over boundary values
and confirm that RAM and register inputs are preserved. It remains
opt-in while HLS lowering and Verilog generation are migrated;
the existing CIL frontend does not silently switch to the new pass.

## 2026-10-15 — first actual Core IR elastic HLS compiler

`SharpHDL.HLS.HlsElasticCompiler.Compile(graph, inputNodeId,
outputNodeId, stageBudget, profile)` now performs an **actual clocked
hardware transformation** in the independent HLS assembly. It lowers
a stateless, single-input Core IR expression DAG into a new canonical
`HardwareGraph` containing payload registers, valid bits, combinational
backpressure-ready propagation, next-state muxes and named input/output
ports. It is not merely a scheduling report.

The compiled result exposes `Graph`, `Inputs`, `Outputs`,
`NextState`, `ResetValues`, `Latency`, `InitiationInterval=1`
and `PayloadRegisterBits`. It runs directly in the independent
`HardwareGraphMachine`, preserving token order and values under
randomized ready/valid stalls. Individual operations whose abstract
cost exceeds the configured stage budget are **rejected**, not
silently split into unverified multicycle hardware.

The legacy compatibility assembly adds
`HlsPipelineCompatibility.ToModule(compiled, name)`, which maps
the compiled Core IR back into the existing, proven Verilog emitter.
This bridge preserves all register and handshake logic, and will
be retired once the Core-native RTL backend is qualified.
`tools/SharpHDL.CoreHlsDemo` demonstrates C# expression -> Core IR ->
registered elastic Core IR -> Verilog. The testbench
`tests/rtl/tb_core_hls_elastic.sv` exercises the resulting RTL under
randomized backpressure using Verilator.

Use `make core-hls-verilator` for the full hardware regression or
`make core-ir-verify` for Core/HLS/Simulation unit parity.

**Scope limits:** this compiler currently accepts one input token and
one result expression. It rejects external state, RAM, direct DSP
primitive nodes and operators too expensive for one stage. It does
not yet replace the existing CIL stream-loop compiler, perform
resource sharing, support multicycle II, or characterize physical
FPGA timing. The production `CilCompiler.AttachPipeline` remains
unchanged pending differential qualification of these features.

## 2026-10-15 — legacy RTL isolation and independent Core-native RTL

The original Verilog emitter has been **physically extracted** from
`src/SharpHDL/Hardware.cs` into
`src/SharpHDL.LegacyRtl/LegacyVerilogWriter.cs`. It is still compiled
into the legacy compatibility assembly using an explicit linked source
entry in `SharpHDL.csproj`, rather than a project reference that would
create a dependency cycle. `Module.Verilog()` remains the same public
API and calls the relocated legacy writer. Its implementation and
generated Verilog behavior are deliberately retained for differential
verification. All six standalone direct Roslyn build paths were
updated to include the relocated source.

A separate `src/SharpHDL.RTL` assembly now implements
`CoreVerilogEmitter.Emit` with a **Core-only dependency**. It emits
single-clock synthesizable Verilog directly from validated canonical
Core IR, including registers, synchronous reset, arithmetic,
comparisons, bitfield operators and muxes. It does not reference
legacy `Module`, the HLS compiler or the simulation engine. RAM,
ROM lookup, direct DSP primitives and other unsupported operations
currently produce explicit diagnostics.

`tools/SharpHDL.CoreHlsDemo` generates two independent RTL files
from the same compiled elastic Core IR graph:
`generated/core-hls/CoreHlsElastic.v` (preserved legacy writer) and
`generated/core-hls/CoreHlsElasticNative.v` (new Core-native writer).
The same randomized ready/valid and reset testbench runs against both
backends under Verilator. `make core-hls-verilator` verifies the
legacy route; `make core-rtl-native` verifies the new backend.
The xUnit workflow has a dedicated `core-rtl-native` acceptance job.
Native RTL acceptance passed in [run 38823125300](https://github.com/jeffadickson/SharpHDL/actions/runs/38823125300): both old and new emitters completed 303/303 randomized transactions with stall, drain and reset checks. Ubuntu/Windows xUnit and architecture layering also passed.

The long-term dependency direction is Core <- HLS, Simulation and
RTL; the compatibility assembly references the new subsystems but
continues to contain the original public API and legacy RTL writer.
Once Core-native RTL covers RAM/DSP and all production designs,
we can retire the source-linked legacy emitter without breaking
the `Module.Verilog()` compatibility contract.

## 2026-10-15 — native TinyRisc16 ROM/RAM and full UART acceptance

The independent `SharpHDL.RTL.CoreVerilogEmitter` now accepts the
canonical `HardwareMemoryPort` map and emits initialized ROM lookup
functions (with zero for out-of-range addresses) and initialized,
single-port synchronous RAM. RAM reads are combinational views of the
pre-edge array, captured by the existing Core IR read-data register;
writes occur at the rising edge, suppressed by reset and out-of-range
addresses. RAM cells retain their values through synchronous reset.
This preserves the existing read-before-write contract.

`tools/SharpHDL.NativeTinyRisc` constructs the **real** 33-word
Hello World TinyRisc16 program and exports its full canonical graph
(428 nodes and one scratch RAM) directly through the native Core RTL
backend. It generates `generated/tinyrisc/native/HelloWorld.v`.

`tests/run-tinyrisc.sh` executes the same two existing independent
Icarus testbenches against **both** the preserved legacy and the new
native RTL: the complete CPU cycle-by-cycle output oracle and the
8N1 serial UART top-level test expecting `Hello, world!\r\n`,
HALT and no fault. Both native benches passed in
[run 38825376134](https://github.com/jeffadickson/SharpHDL/actions/runs/38825376134).
The additional native Yosys `hierarchy -check; proc; opt; check -assert`
gate passed in
[run 38825395067](https://github.com/jeffadickson/SharpHDL/actions/runs/38825395067).
These results establish behavioral equivalence for this design, not
formal equivalence for all possible programs or physical FPGA timing.

Run `make tinyrisc-hello` to exercise both backends, including
UART and Yosys checks. The legacy emitter is retained as an oracle.
Direct DSP48E2, multiport memories and broader backend capabilities
remain for further migration.

## 2026-10-15 — production CIL frontend connected to Core elastic HLS

The existing `CilCompiler.AttachPipeline` now accepts
`new PipelineOptions { UseCoreCompiler = true }`. The **same**
production CIL decoder parses `HwStream<T>.Read/Write`, branches,
fixed-width arithmetic and C# loop structure, then lowers the
resulting token expression using `CoreTokenLowering` directly into
canonical `HardwareGraph`. No temporary Module output is added.

`SharpHDL.HLS.HlsElasticCompiler` creates actual registered
ready/valid hardware with stalls, token ordering and II=1.
`CorePipelineAttach` binds its register/next-state graph into the
original Module's existing stream ports, while preserving the
`PipelineHandle`, operation graph and source-map annotation path.
The legacy `PipelineScheduler` remains available as the default
until full CIL compatibility qualification. No silent fallback is
performed if Core compilation rejects an unsupported feature.

The initial Core CIL mode deliberately rejects DSP-preference hints
that cannot be preserved and remains stateless, single-input,
single-output and II=1. This is a real production-frontend integration,
not yet a replacement for feedback, multicycle resource sharing or
every legacy scheduler feature. Stage costs are abstract operation
counts, not physical timing.

`tools/SharpHDL.CilCoreDemo` generates legacy and native Core RTL
from an actual C# `while(true)` stream method, and
`tests/rtl/tb_cil_core.sv` checks both against a software arithmetic
oracle with randomized backpressure, drain and stalled reset.
Run `make cil-core-verilator` or the `cil-core-verilator` CI job.
The C# xUnit suite additionally checks real CIL arithmetic and
conditional-branch pipelines across 900 simulated clock cycles.
The CI results for these new tests must pass before this milestone
is declared verified.
