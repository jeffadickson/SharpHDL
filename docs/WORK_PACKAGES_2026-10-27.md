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

## Execution record

WP-01 is complete within its stated architectural qualification scope. Windows
and Linux full suites pass 233/233; Icarus, Verilator and iCE40 mapped replay pass
56,514 edges. Two-state temporal induction and its negative control pass. Six
scoped ALU mutations are killed; bounded fuzzing completes 273 executions with no
saved crashes/hangs. [Evidence](Z80_COMPLETION_2026-10-27.md) retains limitations.
WP-02 has five new diagnostic tests passing and is undergoing regression/mutation
checks. WP-03 follows; neither is declared complete until its acceptance checks run.
