# Library-fit review — scheduled compiler continuation

Reviewed against current primary documentation on 2026-10-27. Each decision maps
to one of the five follow-up tasks, including opportunities in existing code.
A useful library does not itself establish hardware semantics or correctness.

| Task / code area | Library or facility | Description and rationale | Decision / tradeoff |
|---|---|---|---|
| 1. Arithmetic binding, ScheduledArithmetic | .NET Dictionary, HashSet, Queue and ReferenceEqualityComparer | Existing BCL structures give deterministic declaration-order allocation and identity-based DAG tracking with no runtime dependency. The small fixed-state lane allocator needs domain-specific semantics rather than a general graph framework. | Keep BCL. No replacement needed for this explicit-state allocator; validate widths, capacity and register dependencies in SharpHDL. |
| 1. Future automatic binding | QuikGraph | Maintained graph structures and topological algorithms could replace custom dependency/SCC traversals when extracting arbitrary operation graphs. | Add to the automatic scheduling investigation, not the current explicit-state implementation. Its topological order does not solve resource binding; require stable tie-breaking and cycle diagnostics before adoption. |
| 1. Future optimal scheduling | Microsoft Z3 | Constraint solving could express resource capacities/dependencies and generate checkable schedules or counterexamples. | Defer to automatic scheduling with a precise encoding and real solver integration. Native deployment cost and incomplete encodings must not be presented as proofs. |
| 2. Existing randomized tests | FsCheck.Xunit 3.3.3 | Generates and shrinks inputs, retains replay seeds, integrates with xUnit. Replaces ad hoc generation for property campaigns while directed tests express boundary contracts. | Already adopted; retain reproducible seeds. It is bounded property testing, not coverage-guided fuzzing. |
| 2. Existing manual mutation opportunities | Stryker.NET 5.0.0 | Established .NET mutation engine supplies instrumentation, test selection and reports. Separate example assembly permits table mutation rather than custom mutation code. | Already adopted and expanded. Report equivalent/uncovered/surviving/timeout dispositions honestly; avoid asserting a score proves correctness. |
| 2. Coverage-guided traffic | SharpFuzz 2.2.0 with AFL++ | Upstream out-of-process integration instruments managed assemblies and uses coverage feedback, corpus evolution and crash retention. | Adopted in a separate tool; no hardware runtime dependency. Linux/WSL instrumentation and bounded corpus runs have setup costs. Keep oracle logic independent and preserve crashes/seed/statistics. |
| 2/4. Coverage in already-written tests | Coverlet collector 10.1.0 | VSTest collector measures managed line/branch execution, including the separate example assembly, without a custom coverage engine. | Retain. Does not measure HDL branch/toggle coverage or the standalone runner unless explicitly instrumented; no comparison across differing scopes. |
| 3. Current loaded-method frontend | System.Reflection and existing decoder | Existing public MethodInfo/IL facilities and shared production decoder preserve instruction identity and diagnostics. | Retain for this path. Replacing it mid-feature would add a second decoder and compatibility risk. Reuse existing stack/CFG analysis, keep hardware lowering domain-specific. |
| 3. Offline assemblies / PDB provenance | System.Reflection.Metadata / PEReader | BCL facilities can read PE method bodies and Portable PDB metadata without executing code. Existing provenance work already benefits from this ecosystem. | Prefer for a future offline frontend; add to that task. It is lower level and still needs a validated opcode/CFG layer. No new package needed for the current MethodInfo path. |
| 3. Alternative offline IL model | Mono.Cecil | Reads/writes CIL assemblies with a mature instruction/object model without loading the target runtime assembly. | Evaluate alongside SRM for offline support; defer now. Adds a dependency and object-model translation, and instruction decoding does not establish hardware legality. |
| 4. External tool orchestration | Testcontainers for .NET | Existing integration project can run real HDL tools in isolated images. Digest pinning can reduce differences between local/hosted environments. | Reuse existing integration system when pinning tool images. Do not add Docker to fast unit tests or silently skip required tool checks. Current GitHub apt jobs remain non-hermetic and are labeled accordingly. |
| 4. Generated HDL snapshots | Verify.Xunit | Snapshot testing can improve review of diagnostic text and generated HDL while retaining approved expected output. | Candidate for stable textual interfaces after provenance/path normalization. Defer broad adoption: source IDs can cause noisy goldens, and snapshots cannot replace differential HDL execution. |
| 4. Existing process wrappers | CliWrap | Managed subprocess composition/cancellation/exit/output handling could simplify future C# HDL runners. | Candidate for a unified runner task, not needed for current small shell/GitHub CLI invocations. Preserve failures/timeouts and bound output; evaluate against System.Diagnostics.Process before adding a runtime dependency. |
| 5. Existing stopwatch benchmarks | BenchmarkDotNet | Handles warmup, process isolation, statistical reporting and allocation diagnostics for Windows/Linux .NET. Supports standard exporters and reproducible parameter cases. | Adopted in a separate scheduled benchmark tool. Existing stopwatch reports remain historical; new baselines include construction plus 256 edges and are not directly comparable. No optimization is claimed. |
| 5. Memory banking / bit mapping | .NET BitOperations, BigInteger and read-only collections | Power-of-two validation, exact-width initialization and immutable boundaries use existing BCL facilities. Cyclic bank/address mapping and reset/write semantics are SharpHDL domain policy. | Retain BCL; no generic array library improves these few explicit operations. Avoid a tensor/numerical dependency for memory semantics. |
| 5. Target qualification | Yosys libraries and target synthesis passes | Existing synthesis libraries map generic memory/operators into actual technology cells, providing independent resource evidence beyond emitted hints. | Reuse external tool libraries, not a managed reimplementation. Keep Xilinx mapping evidence separate from Intel inference hints, physical timing, placement and formal equivalence. |

Primary sources:

- [QuikGraph algorithms](https://kernelith.github.io/QuikGraph/api/index.html)
- [Z3 repository](https://github.com/Z3Prover/z3)
- [FsCheck execution/replay](https://fscheck.github.io/FsCheck/RunningTests.html)
- [Stryker configuration](https://stryker-mutator.io/docs/stryker-net/configuration/)
- [SharpFuzz upstream API/instrumentation](https://github.com/Metalnem/sharpfuzz)
- [Coverlet collector compatibility](https://packages.nuget.org/packages/coverlet.collector/)
- [System.Reflection.Metadata method bodies](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.metadata.methodbodyblock?view=net-10.0)
- [Mono.Cecil](https://www.mono-project.com/docs/tools%2Blibraries/libraries/Mono.Cecil/)
- [Testcontainers for .NET](https://dotnet.testcontainers.org/)
- [Verify](https://github.com/VerifyTests/Verify)
- [CliWrap](https://github.com/Tyrrrz/CliWrap)
- [BenchmarkDotNet overview](https://benchmarkdotnet.org/articles/overview)
- [Yosys documentation](https://yosyshq.readthedocs.io/projects/yosys/en/latest/)

Adopted packages are test/benchmark tooling only. HLS, Core, Simulation and RTL
retain their existing dependency direction and no new production NuGet dependency.
Execution status belongs in the final verification report, not in this fit review.

## Applied continuation decisions: shifts and scheduled source maps

- **System.Reflection.Metadata / PEReader:** now used for matched scheduled-state
  source provenance, including embedded Portable PDB fallback and loaded/on-disk
  module identity checks. This uses the existing BCL facility rather than adding
  Mono.Cecil or implementing PDB identity/byte parsing. Offline assembly compilation
  remains a separate task; this adoption does not claim that task is complete.
- **NSubstitute:** reused only for an invalid external symbol-stream capability
  seam (readable but non-seekable). The hardware, CLR shift oracle and PDB reader
  itself are not mocked. Existing test dependency; no new package.
- **BinaryPrimitives:** the expanded SharpFuzz harness uses BCL little-endian
  UInt32/Int32 decoding for operand/count bytes instead of custom packing code.
  The native C# expression is an independent oracle for scheduled shift execution.
- **Custom glue retained:** active sequence-point lookup needs the rightmost
  boundary not exceeding an IL offset, including hidden boundaries. Its bounded
  binary-search glue implements that domain rule; no graph/parser package is
  warranted for it. The fixed barrel network encodes CLR semantics as Core IR,
  which ordinary host shift helpers cannot substitute for hardware construction.

Sources: the .NET 10 PEReader.TryOpenAssociatedPortablePdb API and C# shift-count
reference linked in the continuation report. No production NuGet dependency was
added for these changes.
