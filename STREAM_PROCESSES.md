# Typed channels and concurrent CIL processes

`HwStream<T>` carries typed tokens using **data, valid and ready**. Transfers
occur on rising clock edges when both valid and ready are high. The compiler
creates those signals and the waiting state machines; process code uses `Read()`
and `Write(value)`. Every attached process is an independent circuit in the same
clock/reset domain. There is no CLR, operating-system thread or runtime scheduler
in the FPGA.

Internal channels are ready/valid connections, not complete AXI interfaces.
They can carry a four-bit `UBits<W4>` value without padding. At external boundaries,
`ToAxi` drives the existing byte-aligned `Stream` interface. It supplies TKEEP=all
ones, TLAST=1 and TUSER=0: **each scalar token is a separate one-beat packet**.
`FromAxi(..., discardSidebands: true)` explicitly discards packet metadata; it
does not filter partial beats. Use the existing packet `Stream` blocks when
TKEEP, TLAST or TUSER must survive processing. There is no automatic packet inference.

## An ordinary C# process

```csharp
[HardwareProcess]
public static void AddTwo(HwStream<ushort> input, HwStream<ushort> output)
{
    while (true)
    {
        ushort value = input.Read();
        output.Write(unchecked((ushort)(value + 2)));
    }
}
```

`HardwareProcess` documents intent; explicit `AttachProcess` selects compilation.
Calling `Read` or `Write` on the host throws, so a hardware process cannot
accidentally execute as a software loop. Simulate its compiled `Module` instead.

Roslyn compiles the loop, arithmetic, conversion and calls into CIL. SharpHDL
tracks which method parameter is each channel handle. At a `Read` call, it asserts
that channel's ready and holds the program counter until valid is high. At a
`Write` call, it asserts valid with the saved value and holds both until ready.
The transfer happens once, at the edge that advances past that call.

Each other reachable CIL instruction takes one clock. `value + 2` therefore
includes operand loading, addition and conversion states, not just the adder.
The CIL supplies operations and branches; **SharpHDL supplies the hardware timing,
registers and handshakes**. The instruction map is included in the generated PC
register comment. Local/stack registers and channel buses retain actor names.

## Wire two processes and explicit buffering

The runnable [StreamingProcesses example](examples/StreamingProcesses.cs) computes
`((input + 2) modulo 65536) * 3 modulo 65536`:

```csharp
var m = new Module("StreamingProcesses");
var input = HwStream<ushort>.Input(m, "input");
var added = HwStream<ushort>.Internal(m, "added");
var queue = HwStream<ushort>.Fifo(m, added, "queue", depth: 2);
var result = HwStream<ushort>.Internal(m, "result");

CilCompiler.AttachProcess(m, "add_two",
    typeof(StreamingProcesses).GetMethod("AddTwo"), input, added);
CilCompiler.AttachProcess(m, "times_three",
    typeof(StreamingProcesses).GetMethod("TimesThree"), queue, result);
HwStream<ushort>.ToAxi(m, result, Stream.Output(m, "output", 16));
```

Both actors execute concurrently. If the AXI consumer stops, the second actor
holds its output, the FIFO eventually fills and the first actor eventually
blocks on its write. No token is overwritten. A channel itself stores nothing;
a FIFO adds the declared number of register-backed token slots. Each actor also
holds its own in-flight values in registers. FIFO depth is not total system
capacity. FIFOs accept simultaneous push/pop even when full and have no
combinational input-to-output fall-through. Their ready path can depend on
output ready; they are not registered-ready skid buffers.

| API / signal | Contract |
|---|---|
| `Input`, `Output`, `Internal` | Typed data and reversed ready direction; no implicit storage |
| `Read()` | Wait for and consume exactly one token |
| `Write(value)` | Wait until exactly one token is accepted; payload stays stable |
| `Connect` | Wire one producer to one consumer of the same token type |
| `Fifo(..., depth)` | Explicit depth 1..256, register storage, reset empties queue |
| `ToAxi` | Byte-aligned matching width, full one-beat packets |
| `FromAxi` | Matching width; explicit metadata-discard permission required |
| `ProcessHandle.ProgramCounter` | Current CIL state, for simulation/debug |
| `WaitingForInput/Output` | Actor is at a blocked read/write |
| `Halted`, `Fault`, `AssertionFailed` | Return/fault status, sticky until reset |

Synchronous reset restarts all actors at their entry instruction, clears locals
and stack storage, and empties FIFOs. Reset overrides transfers at that edge;
external endpoints must use the same reset contract. Reset may discard accepted
but not yet emitted tokens. `return` halts an actor until reset. Arithmetic and
assertion failures halt it with a sticky fault; earlier transfers remain committed.
Expose the handle's diagnostics as module outputs if external monitoring is needed.
Waiting indefinitely for valid/ready is legitimate, so processes have no watchdog.

## Supported process subset and limits

| Construct | Current support |
|---|---|
| Token types in process bodies | bool, byte/sbyte, short/ushort, int/uint, `UBits<W>` 1..32 bits |
| Arithmetic | Integer add/subtract/multiply, divide/remainder toward zero, checked overflow faults |
| Logic | Bitwise operations, integer shifts, comparisons, conversions to 8/16/32 bits |
| Control | CIL branches/switch; runtime for/while/do loops, finite return |
| Intrinsics | Direct channel Read/Write, UBits operators/conversions, Hardware.Assert |
| Concurrency | Explicit independent `AttachProcess` calls, single producer/consumer per channel |
| Static limits | 512 decoded instructions, 32 evaluation-stack entries per actor |

Structural typed channels/FIFOs also carry flat `HardwareStruct` layouts accepted
by `PackedLayout`. **Record tokens and record locals in process bodies are not yet
supported**. Unsupported instructions produce an error with method and IL offset.
Channel aliases in locals, dynamic channel selection, by-reference values, arrays,
RAM accesses, exceptions, recursion, arbitrary helper calls, floating-point and
fixed-point objects in process bodies are not implemented. Other .NET languages
can target the same supported CIL, but this new process suite currently verifies
C#; language-specific generated patterns may need additional lowering.

`AttachPipeline` now provides a separate [automatic scheduler](PIPELINE_SCHEDULER.md)
for stateless arithmetic stream loops at II=1. The `AttachProcess` API described
here remains an explicit instruction-per-clock backend, **not an automatic HLS pipeline
scheduler**. It does not overlap loop iterations or promise initiation interval 1,
share arithmetic resources, automatically choose FIFO depth, insert skid buffers,
or detect all dataflow deadlocks. Cyclic networks can deadlock without an initial
token. Multiple producers/readers need explicit arbitration/broadcast; duplicate
drivers are rejected. Cross-clock communication needs a separate asynchronous FIFO.
Use existing structural `StreamBlocks.Map/Pipeline/SkidBuffer` for higher-throughput
stages and packet-preserving buffering today.

## Requirements and evidence

| ID | Requirement | Status / evidence |
|---|---|---|
| PROC01 | Typed channel width comes from token layout; explicit ownership and port direction | Implemented; four-bit wraparound and foreign/duplicate binding tests |
| PROC02 | Read/write block without dropping, duplicating or reordering tokens | Implemented; randomized two-actor scoreboard and emitted RTL replay |
| PROC03 | Offered payload/valid remain stable under backpressure | Implemented; long initial stall and randomized downstream stalls |
| PROC04 | Explicit bounded FIFO, simultaneous full push/pop, reset flush | Implemented; queue-model tests at depths 1, 2, 3 and 16 |
| PROC05 | Concurrent actors lower actual CIL branches/arithmetic/calls to clocked RTL | Implemented; two actors, signed finite loop, UBits, branch and fault tests |
| PROC06 | Reset restarts actors; return/fault halt with inspectable diagnostics | Implemented; reset while blocked, finite return, overflow/divide/assert tests |
| PROC07 | AXI boundary defines byte and packet metadata semantics explicitly | Implemented; scalar output sidebands checked in simulator and RTL replay; unsafe import requires opt-in |
| PROC08 | Document cycle model, unsupported constructs and meaningful bus/PC comments | Implemented; this document and generated example RTL |
| PROC09 | Reject ambiguous channel ownership/provenance rather than infer runtime wiring | Implemented; aliases, duplicate bindings/drivers and read/write-same-channel rejected |
| PROC10 | Automatic pipelining with requested II and scheduling report | Partial: `AttachPipeline` schedules stateless arithmetic loops at II=1 and emits a stage report; arbitrary II, resources and device timing remain future (PIPE01–PIPE12) |
| PROC11 | Automatic FIFO sizing, deadlock analysis and explicit fork/join process operators | Future; current composition and diagnostics are explicit |
| PROC12 | Packet-record CIL processes and verified F#/VB process examples | Future; existing packet structural blocks remain available |

Run `bash tests/run.sh` for the core suite and all generated Icarus RTL benches.
For a focused compiler/simulator run after building the core suite:
`dotnet build/core/core.dll --stream-processes`. The suite regenerates
[commented Verilog](generated/streaming_processes.v) and its deterministic RTL
trace bench. Tests establish functional behavior, not placed-and-routed timing.
