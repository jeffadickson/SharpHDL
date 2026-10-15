# Automatic scheduling of stream pipelines

SharpHDL can now automatically pipeline a **stateless stream transform** written
as an ordinary C# loop. Unlike `AttachProcess`, `AttachPipeline` removes CIL stack
loads/stores from the clock schedule, builds the arithmetic dependency graph,
then inserts registers between groups of operations. Conditional arithmetic
(`if/else`, ternary expressions and forward `switch`) becomes predicated mux logic. Several inputs can be in
flight simultaneously. With a ready sink, the implemented initiation interval is
**one input per clock (II=1)**.

This is a bounded, explicit scheduling model. The separate
[`AttachFeedbackPipeline` backend](FEEDBACK_PIPELINES.md) now handles scalar
recurrences conservatively with one iteration in flight. The stateless backend
described here still rejects carried state. Neither backend schedules arbitrary
loops, RAM accesses or shared multi-cycle arithmetic units.

## A worked example

```csharp
[HardwareProcess]
public static void Polynomial(HwStream<ushort> input, HwStream<ushort> output)
{
    while (true)
    {
        uint x = input.Read();
        uint a = x + 2u;
        uint b = x * 3u;
        output.Write(unchecked((ushort)((a * b) ^ x)));
    }
}
```

Attach it explicitly:

```csharp
var input = HwStream<ushort>.Input(m, "input");
var result = HwStream<ushort>.Internal(m, "result");
var schedule = CilCompiler.AttachPipeline(
    m, "polynomial", typeof(ScheduledPipeline).GetMethod("Polynomial"),
    input, result, new PipelineOptions { LogicLevelsPerStage = 1 });
HwStream<ushort>.ToAxi(m, result, Stream.Output(m, "output", 16));
Console.WriteLine(schedule.Report);
```

The full buildable example is [ScheduledPipeline.cs](examples/ScheduledPipeline.cs).
`HardwareProcess` is descriptive; the choice between `AttachProcess` and
`AttachPipeline` determines which compiler backend is used.

Roslyn decomposes the source into CIL loads, locals, addition, multiplication,
XOR, conversion and stream calls. SharpHDL symbolically evaluates one iteration:
loads/stores identify values, rather than become clock states. It checks that the
iteration consumes exactly one token and produces exactly one token, with no
state carried into the next iteration. Then it schedules the resulting graph:

| Stage | Operations | Values carried forward |
|---|---|---|
| 1 | `a = x + 2`, `b = x * 3` in parallel | 32-bit a and b; zero-extended 32-bit x |
| 2 | `product = a * b` | 32-bit product; delayed 32-bit x |
| 3 | `result = product ^ x`, narrow to 16 bits | 16-bit result |

The two stage-1 operations are independent, so they execute together. The second
multiply waits for both. The original input x crosses two register boundaries
before the XOR, so it still belongs to the same token as the product. This delay
matching is essential: omitting those registers would mix different inputs.
The [generated schedule](generated/scheduled_polynomial1.schedule.txt) reports
176 payload register bits and three valid bits before downstream synthesis.
The [generated Verilog](generated/scheduled_polynomial1.v) names operations with
the actor, pipeline stage, method and CIL offset, and comments each register bus.

Without stalls, the stage occupancy looks like this just after each rising edge:

| Edge | Stage 1 | Stage 2 | Stage 3 | Output accepted at this edge |
|---|---|---|---|---|
| 0 | A | empty | empty | none |
| 1 | B | A | empty | none |
| 2 | C | B | A | none |
| 3 | D | C | B | A |
| 4 | E | D | C | B |

`Latency` reports the number of register boundaries. For this example the first
output can be accepted three clock periods after its input acceptance, and the
steady-state rate is one token per clock. Output becomes valid just after the
third boundary, ready to transfer at the next rising edge. Stalls extend latency.
This describes a pipeline, not three CIL instructions executed by a processor.

## Choose the grouping, inspect the result

`LogicLevelsPerStage` sets how many dependent graph operations may be chained
between registers. Independent operations do not add to one another's depth.
For the same example:

| Levels per stage | Stages | Payload register bits | Unstalled II |
|---|---:|---:|---:|
| 1 | 3 | 176 | 1 |
| 2 | 2 | 80 | 1 |
| 3 | 1 | 16 | 1 |

More grouping saves registers but increases combinational work between them.
This is **not a nanosecond delay model**. Add, subtract, multiply, bitwise logic,
comparisons and multiplexers each count as one logic level. Constant shifts,
slices, concatenations, widening and narrowing count as wiring. Variable shifts
are lowered to multiplexers and their dependencies are scheduled normally.
Constants do not need delay registers. No resource sharing is attempted.

In particular, a multiply is still a whole combinational multiplier, not a
scheduled vendor DSP primitive with a characterized latency. A design containing
a wide multiply can miss timing even with one logic level per stage. Synthesis,
placement and timing analysis are required before claiming a target frequency.
The options do not accept an MHz target or imply an achieved clock rate.

`MaxStages` (default 64, range 1..256) bounds register-stage allocation.
`LogicLevelsPerStage` accepts 1..32. `InitiationInterval` currently accepts only 1;
other values are rejected rather than silently ignored. The report lists every
scheduled non-wiring node with stage, level, width, operation and CIL provenance.
`StageValid` exposes read-only access to the occupancy signals for debugging.
`PayloadRegisterBits` counts the registers this scheduler creates, before synthesis.

## Backpressure and reset

Each stage has an occupancy bit and accepts replacement data when it is empty
or the next stage can advance. Data and valid remain stable while that stage is
blocked. The scheduler moves all values belonging to a token under the same
stage enable, including values on shorter paths. Empty stages absorb bubbles.
An N-stage pipeline holds at most N tokens; there is no hidden FIFO.

Ready propagates combinationally backward. This first scheduler does not insert
registered-ready cuts or skid buffers; long pipelines can have long ready paths.
The external scalar AXI adapter uses TKEEP=all ones, TLAST=1 and TUSER=0, so each
result is a one-beat packet. Metadata-preserving packet pipelines still use the
existing structural packet stream blocks.

Synchronous reset clears every valid bit, discarding in-flight tokens. Reset
overrides transfers at that edge. All endpoints must obey the same reset
contract. This pipeline has no PC or per-token fault output; operations that
would require fault handling are rejected. Use the sequential process backend
when those semantics are needed.

## What is accepted, and what is not

| Construct | Pipeline behavior |
|---|---|
| One unconditional infinite outer loop | Exactly one read of parameter 0 and one write of parameter 1 on every path through each iteration |
| Static void method, two scalar typed channels | Same signed/unsigned 1..32-bit token types as the process backend |
| Local intermediates | Must be assigned before use within each iteration |
| Integer wrapping add/subtract/multiply, bitwise logic | Dependency-scheduled; CIL widths preserved |
| Scalar comparisons | Accepted when represented as value-producing comparison instructions |
| Constant/variable integer shifts | CIL shift-count masking and signedness retained |
| Byte/short conversions, UBits arithmetic | Narrowing and per-operation wrapping preserved |
| Identity or constant result | One elastic stage, still consumes one input per output |
| Forward if/else, ternary and switch | Supported through predication; every path must consume and produce exactly one token |
| Nested loops, conditional back edges, finite return | Rejected: need more general loop and termination semantics |
| Running sums or other loop-carried locals | Use `AttachFeedbackPipeline` explicitly; its II is latency + 1 by default or latency with optional commit forwarding |
| More than one read/write per iteration | Rejected: need explicit multi-rate and join/split semantics |
| Checked overflow, division/remainder, assertions | Rejected: need pipeline fault policy and/or operator implementation selection |
| RAM, arrays, helpers, exceptions, mutable packet records | Rejected by this first pipeline frontend |

`AttachProcess` continues to support the broader sequential subset. There is
**no silent fallback** to a slower backend when pipeline compilation fails.
Errors identify the method and IL offset for unsupported CIL. Decode is limited
to 512 instructions; scheduling is bounded to 4096 expression nodes. Control
expansion is bounded to 128 expanded branches, 128 terminal paths and 8192
visited instructions. Prefix-only constant initialization is ignored; each local
must still be assigned before use on every iteration path. Debug and
optimized C# CIL are tested. F#/VB pipeline body coverage is not yet claimed.

## Requirements and verification

| ID | Requirement | Status / evidence |
|---|---|---|
| PIPE01 | Extract a stateless single-read/single-write iteration from actual CIL | Implemented; optimized/debug C# tests and rejection cases |
| PIPE02 | Schedule from dependencies, allowing independent operations in parallel | Implemented; reconvergent polynomial at 1/2/3 logic levels per stage |
| PIPE03 | Align every live operand across stage boundaries | Implemented; independent numeric scoreboards, reconvergent paths and variable shifts |
| PIPE04 | II=1 under continuous valid/ready; report latency and capacity | Implemented; exact-cycle startup/steady-state/drain tests |
| PIPE05 | Preserve valid and payload under backpressure without loss/reordering | Implemented; randomized stalls, initial full stall and native RTL trace replay |
| PIPE06 | Synchronous reset flushes every occupied stage | Implemented; mid-traffic resets, occupancy and discarded-token accounting |
| PIPE07 | Preserve signedness, width truncation and per-operation UBits wrap | Implemented; signed shifts, comparison, narrow arithmetic and native arithmetic oracles |
| PIPE08 | Report stage operations and register cost, retain meaningful RTL names | Implemented; checked-in reports and commented generated RTL |
| PIPE09 | Reject unsupported semantics and invalid limits explicitly | Implemented; recurrences, nested loops, missing/multiple transfers, faults, finite methods and option tests |
| PIPE10 | Device delay model and timing-constrained operator/DSP selection | Future; current grouping is abstract logic depth only |
| PIPE11 | Shared/multi-cycle operators, recurrence-aware II, memory scheduling | Partial: separate feedback backend supports scalar state at II=D+1 by default or II=D with optional commit forwarding; sharing, memory and optimal-II analysis remain future |
| PIPE12 | Packet metadata, branches/predication and multi-rate processes | Partial: scalar branch predication implemented; packet metadata and multi-rate processes remain future |
| PIPE13 | Predicated paths preserve signed/unsigned selection and one token per input | Implemented; if/else, ternary, saturation and switch scoreboards plus debug/optimized RTL replay |
| PIPE14 | Bound control expansion and reject state/faults on any explored path | Implemented; fixed branch/path/instruction limits, conditional-state and conditional-fault rejection tests |

`bash tests/run-pipelines.sh` compiles both debug and optimized CIL, runs the
independent numeric/handshake tests and replays the three polynomial and three conditional
RTL benches with Icarus. It leaves optimized generated fixtures last. Set
`DOTNET_ROOT` to a .NET 8 SDK directory and put `iverilog`/`vvp` on PATH.
The core `tests/run.sh` also includes the pipeline suite, and `tests/run-all.sh`
includes the dedicated debug/optimized runner. No placed-and-routed timing result
is claimed for these new examples.

## Worked conditional pipeline: gain and clipping

`ScheduledPipeline.ClipGain` is now compiled by the same scheduler:

```csharp
while (true)
{
    int x = input.Read();          // signed 16-bit token
    int y = x < 0 ? x * 3 : x * 5;
    if (y > 12000) y = 12000;
    else if (y < -12000) y = -12000;
    output.Write((short)y);
}
```

Use `AttachPipeline` with `HwStream<short>` input/output and the `ClipGain`
method. The optimized example produces a six-stage II=1 pipeline under the
one-logic-level setting. See its [schedule](generated/scheduled_clipgain.schedule.txt)
and [commented RTL](generated/scheduled_clipgain.v). Debug compilation may introduce
extra boolean operations and change the schedule; the report is authoritative.

| Input | Selected gain | Before clipping | Output |
|---:|---:|---:|---:|
| -5000 | 3 | -15000 | -12000 |
| -10 | 3 | -30 | -30 |
| 0 | 5 | 0 | 0 |
| 2400 | 5 | 12000 | 12000 |
| 2401 | 5 | 12005 | 12000 |

Both pure arithmetic paths can exist in hardware. A mux selects the appropriate
result; the condition is delayed alongside the data when it crosses a pipeline
boundary. This is predication, not a CPU predicting which branch to execute.
Backpressure must hold predicates and values together, or one input's condition
could select another input's result.

The frontend explores each control path and validates one read and one write
on every path. `if (x > 5) output.Write(x);` with no else is rejected: it is a
filter with a variable output rate. An extra conditional read, checked operation,
assertion, or use of state from a previous iteration is also rejected even if
another path would be valid. There are no speculative memory writes or hidden
side effects. Switch indices use CIL equality semantics, with unmatched values
selecting the default path.

Path exploration can duplicate common suffix logic before downstream synthesis;
this implementation does not promise minimal area or perform general control-flow
merging. The report includes the expanded branch/path/instruction counts. Fixed
limits stop excessive expansion with an explicit diagnostic.

## Structured schedule metadata

The structural pipeline scheduler now also returns `PipelineHandle.Operations` containing immutable `PipelineOperation` records (stage, logic level, bit width, operator and name). This is a first step toward a dataflow/pipeline inspector: it avoids parsing the human-readable `Report` string. This initial metadata does not yet contain dependency edges or scheduling from the CIL-based pipeline frontend, and its logic levels remain abstract—not a target frequency or timing estimate.

## Dataflow DAG export (initial implementation)

For structural expressions, `StructuralPipeline.Attach(module, name, input, output, expression, options)` returns a `PipelineHandle` containing `DataflowGraph`. Nodes carry postorder IDs, stage, abstract logic level, width, operation and optional name. Directed edges map source operand IDs to consuming nodes with an operand index. `handle.DataflowGraph.ToJson()` produces machine-readable JSON. xUnit graph tests check topological order, determinism and generated RTL. This is an IR-level *dependency DAG*; it is not a placement-level schematic or a timing measurement. The behavior CIL scheduler has not yet been unified with this representation.

## CIL-generated graph and live occupancy sampling

`CilCompiler.AttachPipeline` uses `PipelineScheduler.Attach` internally; its resulting `PipelineHandle.DataflowGraph` includes DAG nodes/edges and existing method/IL operation name hints. No independent CIL DAG implementation is needed.

`PipelineFlowProbe(handle, input, output)` and `PipelineSignalProbe(handle, inputValid, inputReady, outputValid, outputReady)` can sample stage-valid bits and handshake values from a `Simulator` without advancing the clock. A `PipelineFlowSample` exposes `StageOccupied`, `InputAccepted`, `OutputAccepted`, `InputStalled`, and `OutputStalled`. Call `Sample(sim, cycle)` at a defined pre-edge point **before** `sim.Tick()`. These values show stage occupancy and transfers, but do not yet correlate individual tokens across stages or estimate actual FPGA timing. Dedicated xUnit coverage exists but awaits execution.

## C# multiplication in HLS

CIL `mul` creates a hardware multiply node. SharpHDL assigns it one abstract scheduling level, not a measured device delay. The schedule report includes a MULTIPLIER ADVISORY listing each multiplier's operand widths, result width, and stage. For widths above 18 bits it also includes a TIMING WARNING. This threshold is a conservative review trigger, not a vendor DSP capacity or frequency guarantee. Constant operands may be simplified by synthesis. Physical DSP/LUT mapping and achieved clock timing must be established with downstream synthesis and static timing analysis.

C# fixed-width multiplication uses the destination expression's bit width, so a `uint * uint` operation does not automatically produce a 64-bit mathematical product. Use explicit widening only where supported by the HLS frontend. Checked C# multiplication emits a `mul.ovf` CIL instruction; the current pipelined compiler rejects it with a specific diagnostic rather than inventing overflow-trap hardware. For overflow detection, specify a supported fixed-width overflow flag or an explicit wider arithmetic design.

## Multiplier implementation directive

C# does not have C-style `#pragma` directives, so SharpHDL uses a method
attribute or an explicit pipeline option. The implementation directive is
applied to the **HLS-compiled hardware**, not to ordinary .NET execution.

```csharp
[HardwareProcess]
[HlsMultiplier(MultiplierImplementation.ShiftAdd)]
public static void Transform(HwStream<uint> input, HwStream<uint> output)
{
    while (true)
    {
        uint x = input.Read();
        output.Write(x * (x + 3u));
    }
}
```

`PipelineOptions.MultiplierImplementation` overrides the method attribute
when it is not `Auto`.

| Choice | Current implementation | Important tradeoff |
|---|---|---|
| `Auto` | Ordinary Verilog `*` | Downstream synthesis selects the physical multiplier |
| `Operator` | Ordinary Verilog `*` | Explicitly request default operator without hint |
| `DspPreferred` | Verilog `*` with `(* use_dsp = "yes" *)` net attribute | Vendor-specific *preference*, not guaranteed DSP allocation |
| `ShiftAdd` | Balanced combinational tree of gated shifted partial products | Real alternative RTL structure; may use substantially more LUTs and routing |

`ShiftAdd` currently supports results up to 32 bits and retains modulo
`2^width` arithmetic. It is **combinational** within each scheduled operation;
the existing elastic pipeline scheduler adds register boundaries based on
abstract operation depth. This is not a multicycle iterative multiplier and
does not automatically optimize the timing of a wide product. The balanced
adder tree can create more intermediate nodes, stages and registers than a
single `*`. The report identifies the chosen implementation.

The directive applies to **all multiply expressions within one annotated
pipeline method**. A per-expression pragma, Booth/Wallace compressor,
vendor-specific DSP macro binding, multicycle iterative multiplier,
target-frequency scheduling and resource sharing remain future work in
[issue #26](https://github.com/jeffadickson/SharpHDL/issues/26).

`DspPreferred` is intentionally a soft hint: vendor toolchains may ignore
it, particularly outside AMD/Xilinx flows. DSP use, LUTs, clock rate and
slack must be measured after synthesis and timing analysis.

## Additional multiplier architectures and explicit DSP access

The method attribute now also accepts `BoothRadix4` (unsigned radix-4 recoding
with a balanced partial-product sum) and `WallaceTree` (3:2 carry-save
compression followed by a final carry-propagate adder). Both preserve
modulo-`2^width` arithmetic for widths through 32 bits. These are **real
combinational RTL architectures**, not DSP binding hints. Their arithmetic
nodes are still scheduled by the existing abstract-depth elastic scheduler;
this is not timing-driven pipelining.

For area-constrained designs, a separate **iterative** unit is available:

```csharp
var ports = IterativeMultiplier.Attach(module, "mul", 16);
// Connect ports.A, ports.B, ports.InputValid, ports.OutputReady;
// Observe ports.InputReady, ports.Result, ports.OutputValid.
```

This uses one shared 16-bit adder/accumulator with 16 computation edges after
acceptance. The output is held stable under backpressure. Its initiation
interval is **not one**; the next transaction starts after the previous
result is consumed. It is deliberately not a drop-in replacement for the
ordinary `[HlsMultiplier]` directive because doing so would change the
stream pipeline's timing and handshake semantics.

For direct AMD UltraScale/UltraScale+ DSP48E2 instantiation:

```csharp
var a = module.Input("a", 16);
var b = module.Input("b", 16);
var product = module.Output("product", 32);
module.Drive(product, Dsp48E2.MultiplyUnsigned(a, b));
```

Include `rtl/amd/sharphdl_dsp48e2_mul_unsigned.v` alongside the generated
Verilog. This instantiates **one actual DSP48E2 primitive** with
`AREG=BREG=MREG=PREG=0` and `OPMODE=9'b000000101` for combinational
multiplication. The unsigned operands must be at most **26x17** bits,
allowing zero extension into the signed 27x18 DSP datapath. The wrapper
supports `SHARPH_DL_DSP_BEHAVIORAL` for portable functional simulation;
that fallback does **not** validate AMD's physical primitive, routing or
timing. Real primitive simulation and timing require Vivado/UNISIM and a
matching FPGA target. Direct DSP is nonportable and does not automatically
decompose a 32x32 product into multiple slices.

Run `make multiplier-compare` to generate five equivalent 32-bit HLS
pipelines and collect actual **generic Yosys logic-cell counts**. These
measurements are not FPGA LUT/DSP utilization, maximum clock frequency or
timing slack. Target-specific architecture selection and multicycle DSP
register scheduling remain open under issue #26.

### Architecture selection from measured target results

`tools/select_multiplier_architecture.py` accepts a JSON report of **actual**
target-device measurements and selects a candidate using area, throughput or
latency criteria, with optional clock and resource constraints:

```sh
python3 tools/select_multiplier_architecture.py results.json \
    --objective throughput --target-mhz 200 --max-dsp 4
```

The report must identify `device` and provide `results` rows with
`architecture`, `lut`, `dsp`, `fmax_mhz`, `latency` and
`initiation_interval`. Missing metrics are rejected, not treated as zero.
Area selection is lexicographic (LUTs, then DSPs), not a fabricated
DSP-to-LUT conversion. Throughput compares `fmax_mhz/II`; latency
compares `latency/fmax_mhz`. The selector does not invent timing estimates.
It recommends an architecture; applying that choice to an HLS pipeline is
currently explicit via `PipelineOptions` or a method attribute.

`make multiplier-compare` generates and synthesizes the five current
combinational alternatives with Yosys and writes
`generated/multipliers/comparison.json`. Generic gate counts are useful
for regression and relative comparison but are **not** acceptable inputs
for target-specific Fmax/DSP decisions. Native DSP48E2 simulation and
Vivado synthesis are separate qualification gates.

### Registered direct DSP48E2 pipeline

`rtl/amd/sharphdl_dsp48e2_mul_pipeline.v` provides an explicit
three-stage DSP48E2 primitive configuration with `AREG=BREG=MREG=PREG=1`,
`OPMODE=9'b000000101`, and a matching three-stage `input_valid` to
`output_valid` pipeline. It accepts a new operand pair every rising edge
(II=1) and produces its corresponding product after three registered
boundaries. **There is no downstream ready input or automatic stall
propagation**. Use a FIFO or an elastic wrapper before connecting this
component to a backpressured SharpHDL stream.

The two direct-DSP wrappers have portable behavioral simulation guarded by
`SHARPH_DL_DSP_BEHAVIORAL`. Icarus tests check arithmetic and valid/data
alignment. These behavioral tests do not validate actual AMD primitive
simulation or Vivado timing. The physical implementation must still be
verified with the target device's UNISIM libraries and post-route STA.

### First executed generic synthesis comparison (2026-10-15)

[GitHub Actions run 38814950387](https://github.com/jeffadickson/SharpHDL/actions/runs/38814950387)
passed the five-job workflow, including direct DSP wrapper tests,
C#-generated DSP RTL simulation, HLS provenance/Verilator and generic Yosys
synthesis of the five 32-bit streaming multiplier designs.

| Implementation | Generic Yosys cells |
|---|---:|
| Operator | 3,222 |
| ShiftAdd | 3,211 |
| BoothRadix4 | 4,579 |
| WallaceTree | 4,459 |
| DspPreferred | 3,222 |

These are **generic logic cell counts for complete streaming pipeline
implementations**, including control and registers, not DSP counts or
placed-and-routed FPGA LUT counts. They do not establish which architecture
has the best Fmax. The first Booth/Wallace lowerings are functionally
correct but not yet area-optimized; this is a useful concrete reason to
improve compressor and recoding implementations rather than assuming their
theoretical benefits translate directly to the generated netlist.

The same CI run passed 1,002 combinational DSP wrapper vectors, a
three-stage DSP valid/data alignment regression and 500 vectors through
C#-generated RTL instantiating the direct DSP wrapper. The direct DSP tests
used the portable behavioral model; **Vivado UNISIM and target P&R remain
unverified**.
