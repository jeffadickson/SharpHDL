# Automatic scheduling of stream pipelines

SharpHDL can now automatically pipeline a **stateless stream transform** written
as an ordinary C# loop. Unlike `AttachProcess`, `AttachPipeline` removes CIL stack
loads/stores from the clock schedule, builds the arithmetic dependency graph,
then inserts registers between groups of operations. Several inputs can be in
flight simultaneously. With a ready sink, the implemented initiation interval is
**one input per clock (II=1)**.

This is a bounded, explicit scheduling model. It does not yet schedule arbitrary
loops, RAM accesses, recurrences or shared multi-cycle arithmetic units.

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
| One unconditional infinite loop | Exactly one read of parameter 0 and one write of parameter 1 per iteration |
| Static void method, two scalar typed channels | Same signed/unsigned 1..32-bit token types as the process backend |
| Local intermediates | Must be assigned before use within each iteration |
| Integer wrapping add/subtract/multiply, bitwise logic | Dependency-scheduled; CIL widths preserved |
| Scalar comparisons | Accepted when represented as value-producing comparison instructions |
| Constant/variable integer shifts | CIL shift-count masking and signedness retained |
| Byte/short conversions, UBits arithmetic | Narrowing and per-operation wrapping preserved |
| Identity or constant result | One elastic stage, still consumes one input per output |
| Conditional branches, switch, finite return | Rejected: need control-aware scheduling and token/fault semantics |
| Running sums or other loop-carried locals | Rejected: need recurrence analysis and an achievable II calculation |
| More than one read/write per iteration | Rejected: need explicit multi-rate and join/split semantics |
| Checked overflow, division/remainder, assertions | Rejected: need pipeline fault policy and/or operator implementation selection |
| RAM, arrays, helpers, exceptions, mutable packet records | Rejected by this first pipeline frontend |

`AttachProcess` continues to support the broader sequential subset. There is
**no silent fallback** to a slower backend when pipeline compilation fails.
Errors identify the method and IL offset for unsupported CIL. Decode is limited
to 512 instructions; scheduling is bounded to 4096 expression nodes. Debug and
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
| PIPE09 | Reject unsupported semantics and invalid limits explicitly | Implemented; recurrences, branches, multiple transfers, faults, finite methods and option tests |
| PIPE10 | Device delay model and timing-constrained operator/DSP selection | Future; current grouping is abstract logic depth only |
| PIPE11 | Shared/multi-cycle operators, recurrence-aware II, memory scheduling | Future; requires scheduling/resource constraints and new correctness tests |
| PIPE12 | Packet metadata, branches/predication and multi-rate processes | Future; needs explicit token matching and fault/side-effect contracts |

`bash tests/run-pipelines.sh` compiles both debug and optimized CIL, runs the
independent numeric/handshake tests and replays all three scheduled polynomial
RTL benches with Icarus. It leaves optimized generated fixtures last. Set
`DOTNET_ROOT` to a .NET 8 SDK directory and put `iverilog`/`vvp` on PATH.
The core `tests/run.sh` also includes the pipeline suite, and `tests/run-all.sh`
includes the dedicated debug/optimized runner. No placed-and-routed timing result
is claimed for these new examples.
