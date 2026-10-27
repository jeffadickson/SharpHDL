# SharpHDL work packages

Requested on 2026-10-27: define ten future packages, then complete 1–3. Keep CI
disabled, commit each item locally, and push after verification. The original Z80
implementation commits are 5ec0c1c (indexed), 42d96a0 (ED/I/O), 7a4c975 (interrupts).
The packages below do not imply the whole long-term roadmap is already implemented.

| ID | Scope and acceptance | Library decision |
|---|---|---|
| WP-01 | Finish Z80 qualification: managed/RTL regressions, request-stability proof, mapped replay, coverage, reproducible exports and honest evidence. | Reuse FsCheck, SharpFuzz, Stryker, Coverlet and Yosys. No new production dependency. |
| WP-02 | Structured CIL rejection diagnostics with method, IL offset, reason and matched source span. Preserve exception compatibility and missing/hidden-symbol behavior; retain symbol failures explicitly. | BCL PEReader/Portable PDB support already present; avoid a second symbol parser. |
| WP-03 | Validated, deterministic instruction-flow graph analysis, reachable offsets and canonical loop components integrated into scheduled compilation. Negative and randomized graph checks; no hardware semantic changes. | QuikGraph for traversal/SCC algorithms, with domain validation and deterministic ordering in SharpHDL. Keep graph types out of Core/public results. |
| WP-04 | Continuous-program Z80 differential adapter with memory, ports and instruction boundaries. Qualify reference limitations before claiming broader conformance. | Evaluate test-only Z80dotNet (.NET Standard 2.0); retain pinned vectors as a separate oracle. |
| WP-05 | Dependency-aware RAM-bank scheduling and shared-resource arbitration, including conflict rejection and deterministic replay. | Reuse QuikGraph analysis and existing typed resources; hardware latency/policy stays domain code. |
| WP-06 | AXI4-Lite/Stream conformance campaigns for backpressure, reset, ordering and malformed transactions with minimized counterexamples. | FsCheck generators/shrinkers, Stryker and existing simulator; NSubstitute only at external seams. |
| WP-07 | Portable C# HDL-tool runner with argument safety, cancellation, missing-tool detection, logs, manifests and nonzero-exit propagation. | Evaluate CliWrap versus BCL Process; add only if repeated output/timeout handling warrants it. |
| WP-08 | Sound bit-vector equivalence for a declared IR subset, with explicit rejection of unsupported constructs and retained counterexamples. | Evaluate Microsoft.Z3 with pinned native assets, alongside Yosys. Solver execution—not illustrative output—is required. |
| WP-09 | Core simulator throughput improvements with equal-output regressions, controlled baselines and Native AOT fallback. | Evaluate BCL expression compilation; use existing BenchmarkDotNet, not inferred speedups. |
| WP-10 | Built documentation, stable requirement IDs, working examples and requirements-to-code-to-test traceability. | StrictDoc, Sphinx and generated C# XML documentation; preserve existing docs and historical evidence. |

Primary sources: [QuikGraph](https://github.com/KeRNeLith/QuikGraph),
[Z80dotNet](https://github.com/Konamiman/Z80dotNet),
[CliWrap](https://github.com/Tyrrrz/CliWrap),
[FsCheck](https://fscheck.github.io/FsCheck/),
[SharpFuzz](https://github.com/Metalnem/sharpfuzz).
Candidate dependencies in packages 4–10 are evaluation tasks, not approved blanket additions.

## Description and rationale for each library decision

1. **Qualification infrastructure:** FsCheck generates and shrinks input cases;
   SharpFuzz/AFL++ supplies coverage-guided campaigns; Stryker challenges tests;
   Coverlet measures managed execution; Yosys supplies independent HDL checks.
   Reusing them avoids custom verification engines. Each has a distinct scope:
   host coverage and sampled vectors do not establish complete ISA correctness.
2. **BCL metadata:** the existing Portable PDB reader already validates image
   identity and sequence points. Reuse avoids a second parser/dependency such as
   Mono.Cecil for a loaded-method diagnostic. Offline compilation remains separate.
3. **QuikGraph:** established BFS/SCC algorithms reduce graph-algorithm code.
   SharpHDL still validates input, bounds work and canonicalizes output. Version
   2.5.0 has an older release cadence and MS-PL terms; keep types internal and
   qualify the selected algorithms on .NET 10. See `INSTRUCTION_CONTROL_FLOW.md`.
4. **Z80dotNet candidate:** a C# executable reference can check continuous
   programs, including state accumulated across instructions, which isolated
   fixtures cannot cover alone. Qualify interrupt/I/O/undocumented behavior and
   licensing before pinning a test-only version; agreement with one emulator
   does not establish silicon timing or replace the independent fixture set.
5. **Graph reuse for memory scheduling:** reuse dependency/component analysis
   where it fits; banking, latency, hazards and arbitration remain domain policy.
   Start with deterministic legal schedules before considering a solver. A generic
   topological traversal alone cannot prove an optimal hardware schedule.
6. **FsCheck and existing test libraries:** protocol generators/shrinkers can
   retain minimal backpressure/reset failures instead of accumulating ad hoc random
   loops. Use NSubstitute for external interfaces only; test real DUT semantics.
   Keep reproducible seeds and bounded campaigns distinct from exhaustive proofs.
7. **CliWrap candidate:** async piping, exit handling and cancellation could
   simplify repeated subprocess wrappers. Compare with BCL `Process` first:
   simple invocations may not justify another package. Validate safe argument
   transport, output limits and process-tree cancellation on Windows/Linux.
8. **Microsoft.Z3 candidate:** bit-vector constraints can express fixed-width
   IR equivalence with concrete counterexamples. Native packaging and sound
   translation are costs; reject unsupported IR and handle timeout/unknown
   explicitly. Cross-check a declared subset with Yosys rather than claiming
   proof from an incomplete or illustrative encoding.
9. **BCL expressions and BenchmarkDotNet:** expression compilation may reduce
   interpreter overhead; BenchmarkDotNet supplies controlled timing/allocation
   measurements. Dynamic code has AOT/trimming restrictions and startup cost.
   Preserve an interpreter fallback and compare identical outputs before claiming
   a speedup; reuse the existing benchmark dependency.
10. **StrictDoc, Sphinx and XMLDoc:** these preserve stable requirement IDs,
    engineering narratives and source-generated API documentation. Reuse the
    established systems rather than inventing a requirements format. The cost is
    documentation-tool dependencies and maintaining real traceable evidence;
    successful rendering alone does not close requirements.

These decisions cover existing custom traversal, PDB, random-test and process
wrapper opportunities as well as future capabilities. Primary sources above and
the repository library-fit review retain API references; adoption requires package
compatibility and failure-path tests, not just a recommendation.

## Execution record

WP-01 is complete within its stated architectural qualification scope. Windows
and Linux full suites pass 233/233; Icarus, Verilator and iCE40 mapped replay pass
56,514 edges. Two-state temporal induction and its negative control pass. Six
scoped ALU mutations are killed; bounded fuzzing completes 273 executions with no
saved crashes/hangs. [Evidence](Z80_COMPLETION_2026-10-27.md) retains limitations.
WP-02 is implemented and committed as `1aee8b3`, with eight diagnostic tests and
the 226-test integrated Windows regression passing. Focused mutation kills 22/26
testable mutants; four formatting-only survivors are retained. WP-03 passes
226/226 on Linux, eight designs in both HDL simulators, 41/42 testable mutations
and a bounded 2,798-execution fuzz campaign. All 24 managed projects build and
the final Windows recheck passes 226/226. WP-03 is committed as `5937579`.
Packages 1–3 are complete within their stated scopes; packages 4–10 remain backlog. The
[completion evidence](WORK_PACKAGES_COMPLETION_2026-10-27.md) records actual scope.
