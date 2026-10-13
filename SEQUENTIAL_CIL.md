# Explicit clocked CIL, widths and packet storage

Version 0.13 implements five bounded extensions. This is a compiler for a declared
CIL subset, not a general .NET runtime. The language compiler supplies typed CIL;
SharpHDL still performs hardware typing, control-flow analysis, scheduling, state
construction and RTL generation. CIL does not itself specify clocks or gates.

## Three ways to build hardware

| API | Loop meaning | Clock boundary |
|---|---|---|
| Structural Module builder | Host loop constructs repeated resources | Explicit register/stream block |
| CilCompiler.BuildModule | Constant loops unroll into combinational logic | None inside method |
| CilCompiler.BuildSequential | Runtime branches become a hardware program counter | One reachable CIL operation per active edge; RAM load adds a wait edge |

A static unroll does **not** make each iteration a new clock. It can produce a long
combinational path; synthesis may simplify it. Sequential mode saves scalar
arguments, locals and stack slots in registers. There is no CPU fetching CIL from
RAM: each reachable instruction becomes a statically generated state/datapath.
This initial backend does not optimize resource sharing or automatically pipeline.
Multiply/divide instructions still contain combinational arithmetic and may limit
clock frequency. Debug builds can require more cycles because they contain more CIL.

```csharp
public static int Sum(int n, int seed) {
    while (n > 0) { seed = unchecked(seed + n); n--; }
    return seed;
}
var circuit = CilCompiler.BuildSequential(
    typeof(MyFunctions).GetMethod("Sum"), "SumEngine",
    new SequentialOptions { MaxCycles = 512 });
File.WriteAllText("SumEngine.v", circuit.Verilog());
```

`MyFunctions` is the class containing Sum. n=3, seed=10 returns16. Each load,
comparison, branch, arithmetic operation and store consumes its own execution
edge. Do not infer the total latency from the number of source statements.
See generated/advanced/CsSeqRuntimeWhile.cil.txt and the RTL program-counter comments.

## Handshake and failure contract

| Port | Meaning |
|---|---|
| start | Request acceptance when idle. Deassert after acceptance; holding high can retrigger when idle |
| accept | Combinational start && !busy; synchronous reset overrides acceptance |
| argN | Captured on accepted-start edge; later changes cannot affect that transaction |
| busy | One transaction in flight; start is ignored while busy |
| done | Sticky completion on success or failure; cleared by reset or next accepted start |
| result | Declared return width; valid only when done && !fault; held until reset/new start |
| fault | Checked arithmetic, division exception, array bounds, assertion or watchdog failure |
| assertion_failed | Sticky selected Hardware.Assert failure; also sets fault |
| timeout | Sticky watchdog failure; also sets fault |

Synchronous reset aborts the operation and clears engine registers. RAM retains
contents. Fault, timeout and reset do **not** roll back completed writes. No output
ready input or AXI stream is implicit; a wrapper can map the transaction protocol
to a stream with its own backpressure contract.

MaxCycles counts execution edges, excluding the start edge. Up to MaxCycles edges
are permitted; if still busy, the following edge aborts. Defaults:4096 cycles,
512 decoded instructions, RAM depth16. Limits: cycles1..1,000,000;
instructions1..4096; stack height32; RAM depth1..65536.

Runtime for/while/do, nesting, switch, early return, break and continue compile
through their CIL branches. Static int/uint/bool/UBits signatures are supported.
No exception handlers, recursion, general helper calls, tasks, heap allocation,
records in sequential mode or arbitrary reference parameters are supported.
Sequential calls currently recognize only UBits operators and Hardware.Assert.
Combinational mode retains its existing supported same-assembly helper inlining.

## Per-operation unsigned width

```csharp
public static UBits<W12> Add(UBits<W12> a, UBits<W12> b) {
    UBits<W12> sum = a + b; // 4095 + 1 becomes 0 here
    return sum >> 1;       // 0, not 2048
}
```

UBits<W> is a CPU-executable unsigned value with width1..32. Native execution is
also the independent numerical oracle for these tests. Every arithmetic/logic
operator returns the declared width. CIL working slots use32 bits with explicit
truncation at UBits operator boundaries; synthesis can trim unused upper bits.
This guarantees width semantics, not minimum area or range-inferred registers.

| UBits operation | Semantics |
|---|---|
| + - * | Modulo 2^W, including within checked source blocks |
| & \| ^ ~ | W-bit logic |
| << >> | Count masked with31 like uint; right is logical; result wraps toW |
| < > <= >= | Unsigned compare |
| implicit uint to UBits | Truncate toW |
| explicit UBits to uint | Zero extend |
| / % == != | Not provided by this new wrapper; native int/uint CIL and existing structural APIs remain available |

Signed width-bearing values, fixed-point CIL wrappers, int64 and automatic range
narrowing remain future work. Existing structural UInt/SInt/SFixed APIs are
separate from UBits. Source loop counters remain native32 bits; the generated PC
and watchdog counter are sized for their declared state/budget range.

## Read-only packed packet headers

```csharp
[HardwareStruct]
public struct Header {
    [HardwareField(0)]  public UBits<W12> Length; // bits11:0
    [HardwareField(12)] public byte Kind;        // bits19:12
    [HardwareField(20)] public bool Urgent;      // bit20
}
public static uint Packet(Header h, uint limit) {
    uint n = (uint)h.Length;
    Hardware.Assert(n <= limit);
    return h.Urgent ? n + h.Kind : n;
}
```

BuildModule accepts this21-bit header port. Offsets are explicit, independent of
CLR memory padding and platform ABI. All fields must have HardwareField; overlap
is rejected. Flat fields may be UBits, bool, byte/sbyte, ushort/short, int/uint;
packed width must be1..4096. Signed fields sign-extend on reads. Gaps are unused.
No nested records, construction or mutation through stfld is supported.

## One owned synchronous RAM

```csharp
public static uint Increment(uint[] words, int index) {
    uint old = words[index];
    words[index] = unchecked(old + 1);
    return old;
}
var circuit = CilCompiler.BuildSequential(
    typeof(MyFunctions).GetMethod("Increment"), "IncrementEngine",
    new SequentialOptions { MemoryDepth = 16, MaxCycles = 512 });
```

The array parameter denotes owned internal32-bit RAM, not a pointer port. There
is at most one uint[] parameter. Length is the configured constant depth.
InitialMemory supplies contents (otherwise zero); scalar arguments remain ports.
ldelem issues a synchronous read; a generated wait state captures it on the next
edge. stelem writes on its instruction edge. Invalid signed-negative or too-large
indexes fault and stop; invalid writes are suppressed. Reset retains memory.
General array aliases, new arrays, arrays as locals, multiple RAMs, external host
access, struct arrays and multiple ports are not supported by this frontend.
See MEMORY.md for the underlying explicit RAM primitive and collision contract.

## Assertions

Hardware.Assert(condition) throws HardwareAssertionException in native execution.
Combinational CIL exposes path-selected assertion_failed and fault; result is
unspecified when fault is set. Sequential CIL stops on the failed assertion and
sets done/fault/assertion_failed. These are observable hardware checks, not formal
proof or synthesized CLR exception unwinding. Combinational subsequent pure
expressions may still exist in the graph; there are no combinational side effects.

## Verification and browser lesson

Run tests/run-advanced.sh with DOTNET_ROOT pointing at.NET8 and Icarus on PATH.
The harness compares optimized/debug native C# execution with graph simulation,
then emits per-edge HDL checks for Icarus. Fixtures include input capture,
busy-start suppression, sticky completion, reset, timeouts, RAM faults and typed
wrap boundaries. Existing C#/F#/VB regression tests remain separate; new aggregate
and sequential fixtures currently use C# only.

The HTML companion has Open in VeriSim links with generated RTL plus compact
teacher-owned benches, including a runtime loop. tests/check-verisim.py decodes
the exact links, compiles/runs their payloads with Icarus and checks PASS output.
The hosted browser UI is not end-to-end tested here. VeriSim is external and needs
network for its initial load; offline lessons continue using exported graphs.
Primary integration specification: https://github.com/senolgulgonul/verisim
