# Scheduling loop-carried state

`CilCompiler.AttachFeedbackPipeline` supports constant-initialized scalar locals
that survive between stream iterations. It reuses the arithmetic and predication
scheduler, with a **single-iteration feedback controller**. State is captured with
the input, computed alongside the output, and committed when the output transfers.
No later input is accepted before that commit.

This is conservative recurrence scheduling. It does not overlap dependent
iterations, perform modulo scheduling, or share arithmetic resources. For a
D-stage datapath the implemented external initiation interval is **D + 1 clocks**
with a continuously valid source and ready sink. Backpressure can increase it.
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
No same-edge forwarding is implemented. A hand-designed accumulator can achieve
II=1; this implementation deliberately uses the more conservative schedule.

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
| `Latency` | Datapath depth; earliest output acceptance is D clocks after input acceptance |
| `InitiationInterval` | Actual conservative external II = D + 1, with no stalls |
| `Busy` | Exactly one iteration has been accepted and not yet committed |
| `StateRegisters` | Committed state, ordered by CIL local index; names contain that index |
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

There is no recurrence-distance analysis, same-edge feedback bypass, interleaving
of independent state contexts, minimum-II search, resource sharing or device
latency model. The generated arithmetic is still parallel hardware partitioned
by abstract logic depth; no FPGA clock rate or post-synthesis area is claimed.

## Requirements and evidence

| ID | Requirement | Status / evidence |
|---|---|---|
| FEED01 | Recognize initialized loop-carried scalar locals from CIL | Implemented; nonzero running-sum seed and two-state checksum; debug-only locals excluded |
| FEED02 | Preserve source ordering of output and state assignments | Implemented; sum emits new state, PreviousValue emits old state |
| FEED03 | Capture state with input and commit all state with accepted output | Implemented; independent recurrence oracle checks every commit edge and blocked cycle |
| FEED04 | Prevent a dependent input from using stale state | Implemented; ready is low while busy; occupancy and one-in-flight checks |
| FEED05 | Report actual external latency and initiation interval | Implemented; exact-cycle continuous-traffic tests, external II=D+1 |
| FEED06 | Preserve conditional state updates and integer wrapping | Implemented; conditional checksum, zero-word clear, uint overflow and random data |
| FEED07 | Reset restores initial constants and discards in-flight work | Implemented; reset during stalled output and during computation where depth permits |
| FEED08 | Emit readable bundle layouts, state comments and schedules | Implemented; named state locals and bit ranges in generated Verilog |
| FEED09 | Keep the stateless scheduler behavior and reject unsupported feedback | Implemented; existing pipeline suite plus invalid body/no-state rejection tests |
| FEED10 | Improve recurrence II using dependence analysis and safe bypass | Future; current one-iteration controller is deliberately conservative |

Run `bash tests/run-pipelines.sh` with `DOTNET_ROOT` and Icarus on PATH. It checks
both debug and optimized CIL, including three feedback RTL trace benches.
The core suite also runs these tests. Generated examples:
[running sum](generated/feedback_runningsum.v),
[conditional checksum](generated/feedback_conditionalchecksum.v), and
[previous value](generated/feedback_previousvalue.v).
