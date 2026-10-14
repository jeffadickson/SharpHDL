# Language coverage and requirements

SharpHDL 0.13 has structural, combinational CIL and explicit sequential CIL interfaces. The structural library executes C#/F#/VB on the
host to construct a graph; the behavioral frontend decodes selected CIL methods
into combinational graphs or clocked operation state machines. A construct available during host execution does not
necessarily translate inside a behavioral method. This matrix describes the
current implementation, not all constructs the source languages can express.

Typed channels and free-running blocking CIL actors are now available through
`AttachProcess`; see [stream process requirements PROC01–PROC12](STREAM_PROCESSES.md).
Their support subset and timing differ from the combinational and start/done backends.
[Automatic pipeline scheduling](PIPELINE_SCHEDULER.md) additionally supports stateless
single-read/single-write arithmetic loops with elastic stages at II=1, including
predicated scalar if/else and switch; see PIPE01–PIPE14.
[Feedback pipelines](FEEDBACK_PIPELINES.md) additionally support constant-initialized
scalar recurrence state with one iteration in flight; optional same-edge commit forwarding removes the extra admission clock; see FEED01–FEED11.

## Supported constructs

| Construct | Current behavior | Evidence |
|---|---|---|
| Scalar if/else, ternary, switch, early return | CIL branches become comparisons and muxes | Optimized/debug C#, F#, VB differential tests |
| Constant-controlled for and while | Unrolled combinational logic, no loop register | Loop, Loop12, While4 |
| Constant-controlled do/while | At least one iteration, then unrolled condition | DoWhile4 |
| Nested constant loops | Unrolled within the instruction budget | NestedLoops |
| break and continue on constant conditions | Compiler branches resolved during unrolling | BreakContinue |
| Static same-assembly helper calls | Inline supported bodies, reject recursion | Call and rejection cases |
| Int32/UInt32/Boolean arithmetic, logic and comparisons | Native instruction signedness and 32-bit behavior | BEHAVIORAL_CIL.md operator table |
| Runtime integer shift count | Five-bit-masked barrel shift | ShiftMix, LogicalShift |
| Checked add/subtract/multiply and divide faults | Explicit aggregate fault bit; result invalid on fault | Native CLR exception oracle |
| SFixed constant shifts in structural library | Same W/F format; left wrap, right floor or nearest even | Exhaustive eight-bit tests for counts 0..8 |
| Named explicit state machine | Binary state register, ordered transitions, hold, reset and illegal-state recovery | StateMachine API and DrillController |
| Explicit sequential CIL | Runtime for/while/do and nested control flow; one CIL operation per clock | AdvancedBehavioral optimized/debug CLR and RTL tests |
| UBits and packet records | Per-operation wrapping; explicit nonoverlapping read-only field layout | TypedAccumulate and Packet |
| Hardware assertions | Path-selected fault and assertion_failed; sequential abort | AssertBranch, AssertLoop |
| ROM and single-port synchronous RAM | Explicit memory interfaces with documented timing | MEMORY.md and mapped RAM test |

`for`, `while`, and `do` are source syntax. Their compiled branch structure and
values determine acceptance. These tests establish the listed cases, not every
possible arrangement of those keywords. Unsupported explored instructions fail;
there is no silent execution of a behavioral method on the host.

## What is missing and why

| Gap | Why additional design is needed | Present alternative / requirement |
|---|---|---|
| Implicit clocked loop scheduling | Select BuildSequential explicitly: runtime loops now compile to a state machine with captured arguments and a watchdog | Implemented subset; LOOP02 |
| Bounded data-dependent unrolling, including runtime break/continue | Need iteration bounds, active-path masking and bounded path growth; current backward-branch checks can reject it | Explicit mux stages or FSM; LOOP03 |
| foreach over arrays or enumerators | Array layout, length, load/store and enumerator calls are not translated | Host construction loops; TYPE02 |
| General arrays, mutable/nested structs and aliasing | Read-only explicit packet records work in combinational mode; one owned uint[] RAM works in sequential mode. Other representations need alias/layout rules | Partial TYPE02/MEM02 |
| SFixed/UInt/SInt objects inside CIL methods | UBits<W> is supported for unsigned 1..32-bit values; signed/fixed structural wrappers still need dedicated recognition | Use wrappers in structural builder; NUM03 |
| Variable-count SFixed shift convenience API | Must choose count range, overshift behavior and rounding logic cost | Constant shift operators; NUM02 |
| Saturating fixed left shift and more right-shift rounding modes | Need an explicit overflow/rounding contract; operators currently wrap/floor | Widen before shift and explicitly convert; NUM02 |
| Direct fixed-point division | Need binary-point alignment, widened quotient, residual, rounding and flags | Integer divider with explicit scaling; NUM04 |
| Int64 and smaller scalar ports/locals in CIL | Stack widths and conversion/overflow rules require extending the type system | PortWidth on int/uint for I/O domain; TYPE01 |
| General enum CIL signatures | Enum types are not accepted by the scalar signature validator | Named StateMachine or integer state; TYPE01 |
| IEEE float/double/decimal operations | Require substantial arithmetic implementations and numerical contracts | Existing custom compact-float library; NUM05 |
| Automatic System.Math to CORDIC/ROM replacement | Accuracy, domain, latency and resource choices cannot be guessed from a call | Explicit library blocks; NUM05 |
| Arbitrary external calls, virtual dispatch or recursion | No implicit runtime, dynamic call target or hardware call stack | Supported static helpers; TYPE03 |
| Objects, strings, dynamic allocation, GC | Hardware needs fixed resources and explicit representations | Host elaboration only; TYPE02 |
| try/catch/finally/throw | Fault signaling is implemented, but exception propagation/unwinding is not | Check Fault and explicit FSM recovery; ERR01 |
| async/await, threads, tasks, locks, yield | CLR scheduling and synchronization are not hardware clocks/handshakes | Explicit streams and FSMs; CTRL02 |
| Automatic pipelining and resource sharing | Need dependency scheduling, latency contracts, stall control and timing feedback | Registers/Map and iterative blocks; SCHED01 |
| Automatic counter-width proof/advisories | Must cover reset, terminals, signedness and overflow; narrow state can alter semantics | Explicit width today; WIDTH01 |
| Multiport/byte-write/async RAM and external memory controllers | Need collision, arbitration, target inference and interface contracts | Single-port RAM; MEM02 |
| Multiple clocks, CDC, asynchronous reset | Need independent timing domains and crossings beyond the present single-clock core | Engineered external wrappers; SYS01 |
| Four-state X/Z, analog delays and event timing in the C# or browser simulator | Both currently evaluate two-state graphs at discrete clock edges | External HDL simulator; SIM02 |
| General Verilog compilation in the HTML document | Current page evaluates exported graphs; it does not parse HDL | Verified native Icarus traces; SIM02 |
| Formal equivalence and broad tool qualification | Differential traces cover examples, not all possible inputs/programs | Explicit verification scope; VERIFY02 |

## Traceable requirements and acceptance gates

“Implemented” means code and executable checks are included. “Planned” is not a
promise of current synthesis support. IDs supplement BEHAVIORAL_REQUIREMENTS.md.

| ID | Requirement | Status and acceptance |
|---|---|---|
| LOOP01 | Translate finite constant-controlled for/while/do, nesting and constant break/continue | Implemented/tested; optimized and debug CIL versus CLR and RTL |
| LOOP02 | Explicit Sequential mode: one reachable CIL operation per active edge, saved values, start/busy/done and watchdog | Implemented; runtime for/while/do, nested loops, break/continue, zero iterations, reset, ignored busy-start, captured inputs and timeout tested. Refines the earlier proposed iteration-per-clock contract; a source iteration takes multiple clocks |
| LOOP03 | Offer bounded Unroll mode for runtime conditions, preserving break/continue and path faults; diagnose missing/insufficient bounds | Planned; exhaustive small-bound equivalence and graph-size cap |
| FSM01 | Build named states with minimum binary state width, first-match transition priority, hold by default and synchronous reset to first state | Implemented; independent controller model, overlapping-condition priority and pause/reset tests |
| FSM02 | Recover unused state encodings to reset state on next edge and annotate encoding in RTL | Implemented; RTL injected-state test and browser-engine check; this is not general fault tolerance |
| NUM01 | SFixed << and >> preserve total/fraction width; left wraps, right sign-extends with floor; explicit NearestEven option | Implemented; exhaustive all 256 raw values and shifts 0..8 against numerical oracle and RTL |
| NUM02 | Add dynamic fixed shifts, explicit saturating left shift, and documented overshift/rounding behavior | Planned; signed boundaries, ties, overshifts, invalid policies and resource evidence |
| NUM03 | Recognize typed arithmetic without host execution | Partial: UBits<W>, unsigned widths 1..32, wrap at every operator; optimized/debug C# tested in both modes. Signed/fixed and new cross-language typed tests remain future work |
| NUM04 | Add fixed divide with explicit format, rounding, residual and faults | Planned; overflow, negative ties, divide-zero and all supported policies |
| NUM05 | Define opt-in math lowering and floating formats with domain, error and latency contracts | Planned; existing explicit math blocks remain available; require reference accuracy and backpressure tests |
| TYPE01 | Extend CIL scalar types/conversions and enum signatures without changing promotion/overflow semantics | Planned; opcode/type matrix and native-oracle coverage |
| TYPE02 | Define finite aggregate/array layouts and memory operations | Partial: explicit flat read-only HardwareStruct fields in combinational CIL; one uint[] in sequential CIL. Native-oracle field and RAM tests; no general allocation, aliases, nested/mutable records |
| TYPE03 | Add only explicitly recognized external intrinsic calls; reject arbitrary dispatch and recursive hardware call stacks | Planned extension; current rejection tests remain mandatory |
| ERR01 | Path-selected arithmetic faults and Hardware.Assert status | Implemented: separate assertion_failed plus fault; sequential assertion aborts. Tests cover conditional assertion paths and loop failures. No CLR unwinding or rollback |
| CTRL02 | Keep clock and concurrency boundaries explicit; do not reinterpret .NET task/thread constructs silently | Implemented rejection boundary; broader process DSL planned with cycle/handshake tests |
| SCHED01 | Add explicit latency/throughput targets and prove stall-safe scheduling before automatic pipelining/resource sharing | Planned; multi-transaction scoreboards, reset and mapped timing evidence |
| WIDTH01 | Prove retained-counter ranges including terminal/reset values before suggesting narrowing; preserve native overflow semantics | Planned; static unrolled indexes already disappear |
| MEM02 | Define CIL array-to-memory lowering | Partial: one owned uint[] maps to 32-bit synchronous RAM; ldlen, load/store, bounds faults, initialization, retained contents and load wait state tested. Additional ports/modes remain planned |
| SYS01 | Require explicit domain/CDC/reset contracts before multi-clock support | Planned; asynchronous-clock tests and target constraints required |
| EDU01 | Deliver an offline HTML clock/bit/loop companion with source and emitted RTL views | Implemented; responsive layout, native keyboard controls and manual clock stepping |
| SIM01 | Drive HTML behavior from actual exported graphs; use exact-width arithmetic and simultaneous register updates | Implemented; browser-engine/reference trace tests; reject unsupported graph nodes |
| SIM02 | Launch actual generated RTL plus a teacher bench in VeriSim using its documented fragment format | Implemented launch links and separate on-page WASM lab; see WASM_WORKBOOK.md for seven browser runs and native trace comparisons. Upstream hosted UI is separate. No engine bundled in the offline graph lesson |
| SIM03 | Bound browser execution, allow stop/reset, and keep files/code local by default | Implemented fixed graphs and bounded rendering/history; the separate WASM lab uses a worker, cancellation and a 60-second deadline; see WASM_WORKBOOK.md |
| VERIFY01 | Compare new math/control behavior to independent expected values and generated RTL; retain reproducible artifacts | Implemented for this release; see generated/learning and generated/behavioral |
| VERIFY02 | Expand equivalence testing and formal proof before claiming general compiler correctness | Planned; present counts are evidence for tested cases, not proof |

## Why a clocked loop needs an explicit mode

`for(i=0;i<12;i++) sum+=data` can mean twelve chained adders, one adder reused for
twelve cycles, or a pipeline accepting a new item each cycle. Syntax alone cannot
choose the architecture. The frontend offers combinational unrolling and explicit BuildSequential
with an execution-edge budget. It must never silently turn a combinational
function into a variable-latency transaction. Runtime while loops also need a way
to keep the engine responsive and explain what happens if they never terminate.

## A state machine today

```csharp
var fsm = new StateMachine(m, "drill", "Ready", "Running", "Done");
fsm.When("Ready", start, "Running")
   .When("Running", stop, "Ready")       // First match wins.
   .When("Running", finished, "Done")
   .When("Done", acknowledge, "Ready");
fsm.Build();
m.Drive(running, fsm.Is("Running"));
```

The helper generates the state register and next-state muxes. Data registers
remain explicit so all assignments observe pre-edge state. Names are annotations;
the first state is reset, the minimum binary width is selected, and an unused
encoding recovers on the next edge. BuildSequential separately extracts a state machine from supported CIL, including runtime while loops. Unused-encoding
recovery cannot detect a bit upset that lands in another valid state.

## Fixed-point shift contract

For signed `SFixed<W,F>`, real value = signed(raw)/2^F. `x << n` keeps W and F,
scales by 2^n and wraps; `x >> n` keeps W and F, scales by 2^-n and rounds down to
the format grid. This differs from signed integer division's toward-zero rule.
`x.ShiftRight(n, Rounding.NearestEven)` is explicit rounded scaling. Constant
counts from 0 through W are accepted; negative or larger counts are rejected
rather than using C# integer count masking. At n=W, left produces zero, floor
right produces raw zero or minus one, and nearest-even produces zero. Operators
do not change the binary-point position. Use Convert to change format.

## Workbook use and simulation honesty

The HTML companion is useful after the register/clock lesson: ask a child to
predict, advance one edge, and explain a mismatch. The hockey drill exposes
states, pause, reset and a four-bit count. The fixed-point and loop panels are
optional extensions; no testbench syntax is required from the child. Adult-owned
verification remains essential even when the student interface hides it.

The offline engine evaluates exported Expr graphs using JavaScript BigInt.
“Open in VeriSim” launches the generated Verilog and a compact teacher-owned
testbench in https://senolgulgonul.github.io/verisim/. VeriSim uses Icarus compiled
to WebAssembly. Its initial hosted load needs network access. Links use its
documented #d=...&t=... base64url format. The payloads pass local Icarus; the hosted
UI has not been exercised in this environment. The offline graph engine is separate.

## Release implementation reference

SEQUENTIAL_CIL.md defines exact timing, handshake, width, assertion and RAM contracts.
Run tests/run-advanced.sh with DOTNET_ROOT and Icarus on PATH. Tests retain CIL, graph,
annotated RTL and per-edge HDL checks in generated/advanced. General CIL correctness,
new language-specific aggregate forms, physical FPGA timing and browser UI validation
are not inferred from these tests.

## Measured hardware baseline

[Packet benchmark requirements BENCH01–BENCH08](examples/PacketBenchmark/README.md#requirements)
cover equal-contract handwritten comparison, independent traffic tests, technology
mapping and routed timing. Physical FPGA measurements and eligible recurrence-state
optimization remain explicit open requirements.
