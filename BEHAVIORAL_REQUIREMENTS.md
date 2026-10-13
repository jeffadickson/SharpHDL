# Behavioral synthesis requirements

Version 0.11 adds a real, restricted CIL-to-hardware frontend. Ordinary scalar
methods must be analyzed without executing them with sample hardware inputs.
The earlier explicit Expr builder remains the backend and structural interface.

## Required and implemented in this release

| ID | Requirement | Implementation and acceptance |
|---|---|---|
| CIL01 | Consume compiled method bodies without source files | Reflection MethodInfo and CIL decoder; C#, F#, VB assembly tests |
| CIL02 | Preserve native scalar semantics | Int32, UInt32 and Boolean; 32-bit intermediates, signed/unsigned instructions and masked shift counts |
| CIL03 | Translate conditionals rather than choosing one host execution path | Symbolic branch exploration and mux joins; nested branches, early returns and switch tests |
| CIL04 | Handle locals, arguments and finite constant-controlled loops | Symbolic stack/locals; constant folding permits bounded unrolling; optimized and unoptimized compiler outputs tested |
| CIL05 | Inline supported pure static helpers | Same-assembly calls only; recursion rejected; no implicit execution of arbitrary code |
| CIL06 | Preserve checked arithmetic and divide faults | One aggregate fault output, selected by execution path; compare against actual CLR exceptions |
| CIL07 | Make hardware widths explicit without changing CIL arithmetic | PortWidth on parameters and return; input extension follows type; output narrowing explicitly keeps low bits |
| CIL08 | Reuse the existing backend | Expr graph, graph dump, simulator and commented Verilog; generated RTL tests |
| CIL09 | Compose with explicit clocks and streams | A compiled next-state function drives a register; compiled functions can be wrapped in Map and skid-buffer pipelines |
| CIL10 | Reject unsupported behavior with diagnostics | Method and IL offset where applicable; unsupported signatures, handlers, calls, opcodes rejected; dynamic back edges require BuildSequential |
| CIL11 | Bound compiler work | Instruction, symbolic-branch-depth and call-depth limits; infinite loop and recursion rejection tests |
| CIL13 | Reduce literal power-of-two arithmetic without changing results | Shift lowering, negative signed division correction and checked overflow; numeric and structural tests |
| MEM01 | Explicit clocked memory with portable simulation semantics | Single-port initialized RAM, read-before-write, reset retention, bounds; generated and mapped RTL tests |
| CIL12 | Use independent validation | Actual .NET method execution is the numeric/exception oracle; native execution is confined to the test harness |

Faulted result bits are unspecified. The frontend does not implement CLR exception
unwinding: checked exceptions become a hardware status output. Callers must handle
that flag. The top-level module helper exposes it unconditionally.

## Specific ways to go beyond the MyHDL workflow

MyHDL already supports if/elif/else, loops, function conversion, signed arithmetic,
and memory-related constructs. We do not claim those as new or absent in MyHDL.
Its documented conversion analyzes generator source after elaboration. Our
comparison is to that documented workflow, not every possible custom extension.

| Opportunity | Release status | Evidence or remaining acceptance gate |
|---|---|---|
| One behavioral frontend for C#, F# and VB | Implemented | Independently compiled ordinary functions use the same CIL compiler |
| Translate selected methods from assemblies without Python-style source inspection | Implemented | Frontend only uses MethodInfo and method-body bytes; build harness separates compilation and translation |
| Checked software arithmetic converted to explicit hardware fault signals | Implemented for the stated subset | Native CLR exceptions versus hardware fault tests |
| Select arithmetic architectures and numerical policy in a reusable stream library | Existing explicit API | Parallel/serial multiply and parallel/iterative rounded divide; ordinary CIL arithmetic remains combinational |
| Preserve native software arithmetic while constraining I/O widths | Implemented | PortWidth defines a bounded input domain and explicit output truncation; no implicit change to int arithmetic width |
| Explain each generated operation using CIL offsets and reports | Implemented for arithmetic and method reports | Saved actual CIL, hardware graphs, branch/step counts and RTL annotations |
| Automatically pipeline a behavioral function to a target latency or clock | Deferred | Needs scheduling, live-value storage, sideband alignment, stalls and timing validation |
| Automatically share multiplier/divider resources inside a method | Deferred | Needs scheduling and transaction control; an enum alone cannot implement this |
| Formal equivalence of CIL semantics and generated RTL | Deferred | Current evidence is differential testing, not proof |
| Automatic array-to-RAM translation, structs, wider scalars and loops controlled by hardware data | Deferred | Needs memory/state models and additional opcode/type semantics |

Primary comparison source: MyHDL conversion manual,
https://docs.myhdl.org/en/stable/manual/conversion.html (checked 13 October 2026).
The manual explicitly documents the constructs above; this release is not a
claim of overall feature parity or superiority over MyHDL.

## Implementation boundaries

A combinational method has no inferred clock or pipeline. Static loop iterations
replicate logic. A runtime-dependent backward conditional branch is rejected;
other nonterminating or excessively expanding paths hit the instruction limit.
Symbolic paths are explored recursively and joined at returns, rather than using
an optimized SSA merge at every basic block. Branch-heavy code can duplicate
logic and exhaust the budget even when its mathematical function is simple.

The first type subset excludes Int64, floating point, references, arrays, pointers,
by-reference arguments, instance methods, exception handlers, external library
calls and open generics. Unsupported instructions are not silently executed on
the host and are not treated as constants. Full prevalidation of unreachable
instructions is not promised: operand decoding is global, instruction translation
is applied to explored paths. See BEHAVIORAL_CIL.md for exact API and examples.

## Width optimization requirement

Resolve static loop indexes completely instead of warning about their software
width. This is implemented for the supported constant-controlled loops. Future
sequential-loop lowering must prove all reachable states, including terminal and
reset values, before narrowing an index. Emit an advisory when an explicit width
is unnecessarily wide, and reject a contract too narrow for a proven required
range. This range-analysis/advisory pass is deferred; retained state is explicitly
sized today. See BEHAVIORAL_CIL.md and MEMORY.md for implemented behavior.

The v0.12 extension requirements, support matrix, reasons for gaps, explicit FSM,
fixed-point shift contract and browser/HDL-engine distinction are specified in
CONSTRUCTS_AND_REQUIREMENTS.md (LOOP01 through VERIFY02).

## v0.13 extension

SEQUENTIAL_CIL.md specifies the new UBits operator matrix and clocked CIL subset.
CONSTRUCTS_AND_REQUIREMENTS.md tracks LOOP02, NUM03, TYPE02, MEM02, ERR01 and SIM02
with implemented scope and remaining limitations.
