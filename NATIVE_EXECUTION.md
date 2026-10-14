# Native .NET execution without synthesis

The original stream algorithm can now run through the .NET JIT, with ordinary
software input/output endpoints. No Module, CIL translation, Verilog generation,
or graph simulation is involved in this path.

```csharp
using var input = HwStream<uint>.FromEnumerable(new uint[] { 3, 5, 2 });
using var output = HwStream<uint>.ToAction(Console.WriteLine);
NativeProcess.RunUntilInputEnds(() => FeedbackExamples.RunningSum(input, output));
// Prints 10, 15, 17: the original method starts sum at 7.
```

`FromEnumerable` reads an enumerator directly. `ToAction` calls a synchronous
consumer delegate. The process runs on the calling thread, with no implicit task,
per-token message allocation, or queue. End-of-input raises a private termination
signal caught by `RunUntilInputEnds`; callback and algorithm exceptions propagate.
Dispose endpoints to release their enumerators. The algorithm's original CIL is
still recognized by the hardware compiler: the hardware path lowers the Read and
Write calls as intrinsics rather than executing their software implementation.

## Memory on the PC

For the currently supported array frontend, use normal `uint[]` and the same
ordinary method. [NativeMemory.Bump](examples/NativeMemory.cs) is a worked example:

```csharp
var cells = new uint[16];
uint first = NativeMemory.Bump(cells, 3, 7);  // 7
uint next  = NativeMemory.Bump(cells, 3, 4);  // 11
```

The hardware form compiles that same method with an owned RAM and controller:

```csharp
var hardware = CilCompiler.BuildSequential(
    typeof(NativeMemory).GetMethod("Bump"), "BumpHardware",
    new SequentialOptions {
        MemoryDepth = 16,
        MemoryOptions = new MemoryOptions(MemoryTarget.Xilinx7Series, MemoryStorage.Block)
    });
```

On the PC, array access is immediate software access. In hardware, the compiler
inserts synchronous-read wait states and bounds faults. The tests compare the
native method with all three target configurations over repeated calls and a
controller reset that retains RAM contents. Target settings do not affect native
array contents or native execution. The native array is a reference model, not a
live connection to hardware RAM.

## Functional execution versus cycle simulation

| Mode | Preserved contract | Intended use |
|---|---|---|
| Native .NET method and endpoints | Values and source ordering for supplied inputs | Fast algorithm development and reference execution |
| Module simulator | Register edges, enables, reset and backpressure | Hardware timing/handshake verification |
| Generated RTL and target tools | RTL behavior and target mapping | Hardware implementation and verification |

Native execution has no ready/valid signal simulation. A synchronous consumer
callback must finish before the process continues, but this is not cycle-accurate
backpressure. Multiple native actors are not automatically scheduled concurrently;
this release does not implement software FIFO networks, cancellation of blocked
threads, or a simulator for hardware deadlock. Use explicit finite inputs and
callbacks. Calling an infinite process without a terminating input can run forever.
If a process consumes multiple inputs per iteration, exhaustion can stop a partial
iteration; no transactional rollback is promised. State captured in ordinary C#
locals lasts for that invocation, not across separate runner calls.

Native array exceptions remain .NET exceptions; hardware bounds violations use
fault outputs. Do not expect identical exception transport or cycle counts.
Structural graph-building C# such as TinyRisc.Build still constructs hardware;
it is not a native instruction emulator. Its architectural reference model is a
separate implementation.

## Performance and requirements

The regression runs one million running-sum tokens and one million native array
updates and prints elapsed time. It gates correctness, never wall-clock speed.
There is still enumerator/delegate overhead; JIT warm-up, host load, code shape and
CPU affect performance. This is native execution, not a claim of zero overhead.

| ID | Requirement | Evidence |
|---|---|---|
| NATIVE01 | Execute original stream methods without hardware elaboration | Native endpoints; finite/empty inputs, packet boundaries, wrapping |
| NATIVE02 | Preserve errors and release input resources | Consumer exceptions propagate; disposed endpoints reject access; IDisposable enumerator ownership |
| NATIVE03 | Preserve native array execution alongside target lowering | Same Bump method compared against three target RAM/controller configurations and RTL benches |
| NATIVE04 | Separate functional and cycle semantics | Explicit execution-mode contract above; no native concurrent-network or timing claims |
| NATIVE05 | Measure throughput without brittle timing gates | Million-token and million-array-update workloads print measurements and verify results |
