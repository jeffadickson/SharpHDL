# Scheduling loop-carried state

`CilCompiler.AttachFeedbackPipeline` supports constant-initialized scalar locals
that survive between stream iterations. It reuses the arithmetic and predication
scheduler, with a **single-iteration feedback controller**. State is captured with
the input, computed alongside the output, and, by default, committed when the output transfers.
No later input is accepted before that commit. An explicit single-stage
optimization can instead advance state on input acceptance, as described below.

This is conservative recurrence scheduling. It does not overlap dependent
iterations, perform modulo scheduling, or share arithmetic resources. For a
D-stage datapath the default external initiation interval is **D + 1 clocks**
with a continuously valid source and ready sink. Setting `ForwardOnCommit = true`
reduces this to **D clocks** by forwarding completed state on the commit edge.
Backpressure can increase either interval.
The ordinary stateless `AttachPipeline` retains its II=1 behavior and continues
to reject state unless this feedback API is explicitly selected.

## Running sum from ordinary C#

```csharp
[HardwareProcess]
public static void RunningSum(HwStream<uint> input, HwStream<uint> output)
{
    uint sum = 7;
    while (true)
    {
        sum = unchecked(sum + input.Read());
        output.Write(sum);
    }
}
```

```csharp
var m = new Module("Sum");
var input = HwStream<uint>.Input(m, "input");
var output = HwStream<uint>.Output(m, "output");
var schedule = CilCompiler.AttachFeedbackPipeline(
    m, "sum", typeof(FeedbackExamples).GetMethod("RunningSum"), input, output);
Console.WriteLine(schedule.Report);
```

The source and two additional examples are in
[FeedbackExamples.cs](examples/FeedbackExamples.cs). The compiler finds locals
read before being assigned within an iteration, requires explicit constant
initialization before the loop, and gives them persistent registers. Initialization
used only by debug-compiler bookkeeping does not become persistent state.
Uninitialized iteration-local reads remain errors.

The [sum CIL listing](generated/feedback_runningsum.cil.txt) exposes the initializer,
local loads/stores and loop back edge. Its [schedule](generated/feedback_runningsum.schedule.txt)
has one arithmetic stage, latency 1 and II=2:

| Edge | Event | Committed sum after edge |
|---|---|---:|
| Reset | Initialize state and empty the datapath | 7 |
| 0 | Accept input 3; compute proposed sum 10 | 7 |
| 1 | Consumer accepts output 10; commit | 10 |
| 2 | Accept input 5; compute proposed sum 15 | 10 |
| 3 | Consumer accepts output 15; commit | 15 |

If the consumer stalls before accepting 10, the output holds 10, the committed
sum stays 7, and input ready stays low. When acceptance finally occurs, state
changes to 10. The earliest next input transfer is the following clock edge.
This is the default conservative mode. The optional forwarding mode below removes
that extra edge.

## Forwarding: one input per clock for a one-stage accumulator

The C# process stays unchanged; enable forwarding when attaching it:

```csharp
var schedule = CilCompiler.AttachFeedbackPipeline(
    m, "sum", typeof(FeedbackExamples).GetMethod("RunningSum"), input, output,
    new FeedbackPipelineOptions { ForwardOnCommit = true });
```

| Edge | Event | Committed sum after edge |
|---|---|---:|
| Reset | Initialize state and empty the datapath | 7 |
| 0 | Accept input 3 with state 7 | 7 |
| 1 | Transfer output 10; accept input 5 with forwarded state 10 | 10 |
| 2 | Transfer output 15; accept input 2 with forwarded state 15 | 15 |
| 3 | Transfer output 17; no replacement input | 17 |

The completed bundle is registered. On `output.Valid && output.Ready`, a mux
selects its next-state fields for the new input snapshot. All fields are forwarded
together; output data is not substituted for state (which matters for `PreviousValue`).
When both transfers happen, busy stays set for the replacement iteration.
When only the output transfers, busy clears. The committed state registers still
change only on accepted output, and synchronous reset overrides both transfers.

If output ready goes low, the result and proposed state hold steady, committed
state remains unchanged, and no replacement input is accepted. Raising ready
allows the pending output and next input to transfer together on the next edge.
There is still at most one iteration in flight after each edge.

| Example | Datapath depth D | Default II | Forwarding II |
|---|---:|---:|---:|
| Running sum | 1 | 2 | 1 |
| Conditional checksum and count | 4 | 5 | 4 |
| Previous value | 1 | 2 | 1 |

These values use the default one abstract logic level per stage, with no stalls.
Forwarding adds state muxes and a ready/control path; it does not establish an
FPGA clock frequency. It removes the commit bubble, but cannot make a D-stage
dependent recurrence accept every clock when D is greater than one.
See the [forwarded sum RTL](generated/feedback_runningsumforwarded.v) and
[its schedule](generated/feedback_runningsumforwarded.schedule.txt).

## Eliminating duplicate state for one-stage recurrences

Set `AdvanceStateOnAccept = true` together with `ForwardOnCommit = true` to
select the smaller implementation. It is **opt-in** because `StateRegisters`
then expose state after accepted inputs, which may lead retired outputs by one
word. `StateAdvancesOnInput` reports that distinction. The default state contract
and both existing scheduling modes remain unchanged.

```csharp
new FeedbackPipelineOptions {
    LogicLevelsPerStage = 32,
    ForwardOnCommit = true,
    AdvanceStateOnAccept = true
}
```

The compiler first validates the existing pure scalar CIL restrictions (one read
and write on every path), then checks that **all output and next-state expressions**
fit one stage under the same operation-cost model used by the normal scheduler.
If they do not, it rejects the request before adding signals. It does not silently
collapse a requested multistage schedule. Increasing the logic grouping changes
combinational depth; use synthesis and routing to check actual timing.

Eligible hardware has one output slot and one copy of each state variable. On
input acceptance it captures the output and updates every state register together.
A full, stalled output prevents another input; a consumed output can be replaced
on the same edge. No separate busy register or commit-forwarding mux is needed.
Source ordering still applies: writing the previous value before assigning new
state emits the previous value. Reset restores initial seeds and discards pending
output and the corresponding advanced state together.

Why the external stream behavior is preserved: accepted inputs remain ordered;
only one unretired result exists; no state-dependent successor starts until that
slot can accept a replacement; and scalar state has no external side effects.
Consumers observing **internal state** must explicitly accept the different state
timing. This is a bounded eligibility argument backed by tests, not a general
formal equivalence proof or a multistage speculation mechanism.

The [packet benchmark](examples/PacketBenchmark/README.md) compares this mode with
the retained committed-state baseline and handwritten RTL. On its HX1K mapping,
LUTs decrease from 54 to 35 and flip-flops from 51 to 34, matching the handwritten
reference for this example. This is not a promise of identical costs for other code.

## Multiple states and conditional updates

`ConditionalChecksum` maintains both a rolling checksum and a word count. An
input word of zero clears both; otherwise it updates them and returns their sum:

```csharp
uint checksum = 0x1234, count = 0;
while (true)
{
    uint word = input.Read();
    if (word == 0) { checksum = 0; count = 0; }
    else
    {
        checksum = unchecked((checksum * 3) ^ word);
        count = unchecked(count + 1);
    }
    output.Write(unchecked(checksum + count));
}
```

Predication selects both the output and each next-state value for the chosen path.
The output and all next-state fields travel in one internal bundle. Even if an
output expression is short, the bundle waits for the deepest next-state path.
All state registers commit together, preventing a new iteration from seeing a
mixture of old and new state. A path that does not assign a state variable retains
its incoming value on that path.

`PreviousValue` checks the complementary case: write the old state first, then
assign the new input to that state. The emitted token contains the old value;
the same accepted output commits the new one. The compiler keeps the output
expression captured at the source `Write` call distinct from the final local values.

## Signals, reset and implementation limits

| API / signal | Meaning |
|---|---|
| `FeedbackPipelineOptions.LogicLevelsPerStage` | Abstract logic grouping, 1..32; default 1 |
| `FeedbackPipelineOptions.MaxStages` | Limit 1..256; default 64 |
| `FeedbackPipelineOptions.ForwardOnCommit` | Default false; enable atomic same-edge next-state forwarding |
| `FeedbackPipelineOptions.AdvanceStateOnAccept` | Default false; explicit one-stage accepted-input state optimization; requires forwarding |
| `StateAdvancesOnInput` | True only when StateRegisters advance on input acceptance |
| `Latency` | Datapath depth; earliest output acceptance is D clocks after input acceptance |
| `InitiationInterval` | External II = D + 1 by default, D with forwarding, with no stalls |
| `Busy` | Exactly one iteration has been accepted and not yet committed |
| `StateRegisters` | State ordered by CIL local index; committed by default, accepted-input state in optimized mode |
| `StageValid` | Datapath occupancy; at most one stage is occupied |
| `Report` | External latency/II and commit policy, followed by the internal arithmetic schedule |

The internal datapath is itself elastic and capable of II=1, but the recurrence
controller gates it. Its report is explicitly labeled **internal**; its II is
not the external interface rate. There is no user-selected external II yet.
External channels use the same ready/valid contract and existing scalar AXI
adapters as other typed streams.

Synchronous reset clears busy and valid state, discards the uncommitted iteration,
and restores every state register to its declared constant. Previously emitted
outputs remain emitted. Reset overrides transfers at that edge. Callers must use
the same reset contract at the endpoints.

This first version supports at most 16 persistent scalar locals, represented as
32-bit CIL evaluation values, and at most 32 directly constant-initialized prefix
locals. The prefix supports CIL integer constant loads and local stores, not
arbitrary initialization code. Integer literal initializers are covered; UBits
constructor/conversion initializers and helper calls are not currently lowered
in that prefix. There is no implicit zero-state inference from CLR local defaults.
The general scalar/operation restrictions and control-expansion bounds from
[the pipeline scheduler](PIPELINE_SCHEDULER.md) still apply. Faults, RAM, arrays,
multiple reads/writes on a path, nested loops and conditional loop back edges
remain unsupported. A method with no carried state should use `AttachPipeline`.

There is no recurrence-distance analysis, interleaving
of independent state contexts, minimum-II search, resource sharing or device
latency model. The generated arithmetic is still parallel hardware partitioned
by abstract logic depth; no FPGA clock rate or post-synthesis area is claimed.

## Requirements and evidence

| ID | Requirement | Status / evidence |
|---|---|---|
| FEED01 | Recognize initialized loop-carried scalar locals from CIL | Implemented; nonzero running-sum seed and two-state checksum; debug-only locals excluded |
| FEED02 | Preserve source ordering of output and state assignments | Implemented; sum emits new state, PreviousValue emits old state |
| FEED03 | In default modes, capture state with input and commit all state with accepted output | Implemented; independent recurrence oracle checks every commit edge and blocked cycle |
| FEED04 | Prevent a dependent input from using stale state | Implemented; ready is low while busy unless forwarding and commit coincide; occupancy and one-in-flight checks |
| FEED05 | Report actual external latency and initiation interval | Implemented; exact-cycle continuous-traffic tests, external II=D+1 or D with forwarding |
| FEED06 | Preserve conditional state updates and integer wrapping | Implemented; conditional checksum, zero-word clear, uint overflow and random data |
| FEED07 | Reset restores initial constants and discards in-flight work | Implemented; reset during stalled output, during computation where depth permits, and on a commit/replacement edge |
| FEED08 | Emit readable bundle layouts, state comments and schedules | Implemented; named state locals and bit ranges in generated Verilog |
| FEED09 | Keep the stateless scheduler behavior and reject unsupported feedback | Implemented; existing pipeline suite plus invalid body/no-state rejection tests |
| FEED10 | Improve recurrence II using dependence analysis and safe bypass | Partial; optional same-edge bypass implemented; general dependence analysis remains future |
| FEED11 | Forward every completed state field atomically on simultaneous commit/input acceptance; retain busy for replacement | Implemented; both committed-state modes tested for all three examples, exact II, stalls, old/new output ordering, multi-state updates and reset priority |
| FEED12 | Remove duplicate state only for explicitly selected, eligible one-stage recurrences | Implemented; shared depth model, rejection before allocation, old/new output ordering, atomic multi-state updates, stalls and reset; StateAdvancesOnInput identifies changed internal contract |

Run `bash tests/run-pipelines.sh` with `DOTNET_ROOT` and Icarus on PATH. It checks
both debug and optimized CIL, including nine feedback RTL trace benches (three examples in each of three modes).
The core suite also runs these tests. Generated examples:
[running sum](generated/feedback_runningsum.v),
[conditional checksum](generated/feedback_conditionalchecksum.v), and
[previous value](generated/feedback_previousvalue.v).

## Technology-mapped comparison

The [packet checksum benchmark](examples/PacketBenchmark/README.md) uses
`ForwardOnCommit` for a one-word-per-clock packet accumulator and compares it
with independently written RTL. It measures the extra storage/control cost of
this general commit model and records iCE40 routing estimates. These results
do not imply parity with handwritten RTL or physical-board validation.
