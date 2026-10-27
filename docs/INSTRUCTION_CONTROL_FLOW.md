# Instruction control-flow analysis

`InstructionControlFlow.Analyze(successors, entryOffset)` in SharpHDL.HLS accepts
nonnegative instruction offsets and declared successor lists. Its immutable
snapshot exposes sorted distinct successors, sorted reachable offsets, canonical
strongly connected components and reachable cyclic components (including self
edges). Unreachable cycles remain in `StrongComponents` but not `LoopComponents`.

The adapter rejects empty graphs, absent entry/targets, negative vertex offsets,
null lists, more than 4,096 vertices or 65,536 raw edge entries. Duplicates count
against the input budget before normalization. Collections must remain stable
during analysis; later mutations cannot change any result. These are analysis
budgets, not a general defense against malicious custom collection implementations.

`CilCompiler.BuildScheduledSequential` validates instruction boundaries and uses
this graph before its existing stack-height worklist. Inspect
`compilation.ControlFlow.HasReachableLoops` or `LoopComponents` for original IL
cycles. Hardware state declarations still follow decoded instruction order;
RAM wait states and idle are not instructions in this graph. Stack validation,
cycle watchdogs, memory latency and hardware lowering remain SharpHDL domain code.
A cycle is not a proof of infinite execution, feasible data-dependent paths or
resource-optimal scheduling. Legacy frontends retain their existing traversal.

Run `bash tools/run-scheduled-rtl.sh` from a checked repository directory with
the .NET SDK, Icarus, Verilator and Yosys available. The existing example suite
exports `ScheduledCilLoop.control-flow.json` and both shift-kernel graph files
beside the original source maps and replay RTL. This exercises the public API
on real compiled methods and retains readable analysis without coupling the
example to QuikGraph types.

## Library choice

QuikGraph **2.5.0**, pinned in the HLS project, supplies breadth-first traversal
and strongly connected components. Its types do not enter public result APIs or
the dependency-free Core project. SharpHDL owns validation, limits, immutable
boundaries and canonical ordering rather than relying on traversal/component
numbering. The restored NuGet manifest identifies upstream commit
`9cd6b49292e09041258708a37bed99c56177b0ef` and **MS-PL**, not MIT. The package has
managed .NET Standard assets and no dependency is needed in the simulator or RTL
projects themselves.

This replaces duplicating established graph algorithms while retaining small
domain glue. Tradeoffs are a transitive compiler dependency, MS-PL redistribution
terms and an older release cadence (latest tagged package 2.5.0, released in 2022).
Only the selected algorithms and supported limits are qualified here. No blanket
claim about all QuikGraph algorithms, Native AOT or trimming follows from tests.
The previous provisional library-fit decision is superseded for this specific
instruction graph; automatic scheduling/resource binding remains future work.

Primary sources: [upstream repository and license](https://github.com/KeRNeLith/QuikGraph),
[breadth-first API](https://kernelith.github.io/QuikGraph/api/QuikGraph.Algorithms.Search.BreadthFirstSearchAlgorithm-2.html),
[component API](https://kernelith.github.io/QuikGraph/api/QuikGraph.Algorithms.ConnectedComponents.StronglyConnectedComponentsAlgorithm-2.html).

Tests in `InstructionControlFlowTests` cover diamonds, unreachable cycles,
reachable multi-node/self cycles, nonzero entry, insertion order, duplicates,
deep immutability, malformed inputs, exact resource boundaries and actual scheduled
methods. FsCheck uses seed `1145655947,296144285` for 200 graphs up to eight nodes;
an independent Floyd-Warshall closure supplies reachability and component oracles.
Executed platform, mutation and fuzz results belong to the completion report.
