# SharpHDL engineering design and implementation record

**Revision:** 2026-10-14

**Continuously maintained feature coverage:** [Feature parity matrix](FEATURE_PARITY.md). Treat PAR-001–PAR-018 as minimum verified user-facing parity targets; PAR-019–PAR-024 are differentiation targets. Review evidence weekly and on milestone completion.  
**Status:** Active architecture and development record. For normative requirements and acceptance gates see [PRODUCT_REQUIREMENTS.md](PRODUCT_REQUIREMENTS.md).  
**Evidence policy:** *Committed* means present in Git; *verified* requires executed tests and retained results. Neither a committed test nor a CI workflow automatically establishes a passing test.

## 1. Mission and system boundaries

SharpHDL is a .NET-native hardware design tool supporting C#, F# and VB.NET construction and a documented synthesizable CIL subset. It targets portable synthesizable RTL, predictable behavior, FPGA-oriented streaming/scientific computation, simulation, equivalence checking, formal verification and both desktop/editor and offline web design/debug experiences. It is not an arbitrary-CLR-to-hardware compiler.

The existing source tree includes: structural graph nodes (`Hardware.cs`), behavioral and process CIL frontends, stateless and feedback pipeline schedulers, stream/AXI blocks, numeric units, RAM inference policies, an interpreter, Verilog generation, TinyRisc and browser demonstrations. Maintain these working APIs while introducing improved internals.

## 2. Logical architecture

```text
   C# / F# / VB.NET                        Verilog / IP descriptions
          |                                          |
   CLR compiler to CIL                        interop descriptors
          |                                          |
 structural API / supported CIL frontends / hierarchy elaboration
          |                                          |
          +--------------- canonical typed hardware IR ------------+
                                  |
            validate semantics, optimize and preserve provenance
                                  |
             +--------------------+-------------------------+
             |                    |                         |
     synthesis backend     reference/fast simulation     instrumentation
       Verilog/targets     .NET interpreter/compiled       trace/debug
             |                    |                         |
       Icarus/Yosys/       independent reference           shared debug
       Verilator/FPGA       models and checker              service
             |                    |                         |
        area/timing                +---------- VS Code DAP / browser UI
        evidence
```

The canonical, versioned hardware IR is a **planned design**; today the structural graph and multiple CIL frontends remain distinct. Do not claim that they have already converged.

### Critical semantic invariants

- Signal widths, signedness, truncation, literal ranges and reset behavior must be explicit and checked.
- Each clock edge samples pre-edge inputs/registers/memory outputs, resolves next-state and commits registers atomically; memory collision ordering must be defined per target-independent logical contract.
- A combinational mux evaluates only its selected branch where appropriate; all produced values are masked to defined widths.
- Streams must preserve ready/valid stability and exactly-once transfer on a handshake. Stalls must not duplicate a side effect.
- Simulator modes must identify their 2-state or 4-state behavior, not accidentally mix the semantics.
- Compiled execution must reject unsupported operations rather than silently evaluating them on the CLR in a way that changes synthesizability.

## 3. Current implementation map

| Component | Source / artifact | State | Verification |
|---|---|---|---|
| Structural graph, Verilog writer, reference simulator | `src/SharpHDL/Hardware.cs` | Existing | Legacy tests exist; latest run not verified here |
| CIL processing and pipelines | `CilCompiler.cs`, `PipelineScheduler.cs`, `PipelineCil.cs`, `FeedbackPipeline.cs` | Existing scoped implementations | Existing differential scripts; no new result claimed |
| Vendor RAM inference | `SynchronousRam.cs`, `MemoryOptions.cs`, `BankedRam.cs` | Existing target policies | Mapping documented; physical-vendor timing is not proven |
| VCD export for watch traces | `src/SharpHDL/HardwareTraceVcd.cs` | Initial two-state VCD export committed | xUnit serialization cases committed; not executed here |\n| Hardware debugger façade | `src/SharpHDL/DebugSession.cs` | Implemented initial stepping, watches, value/change breakpoints, trace retention | xUnit cases committed, not executed here |
| xUnit/NSubstitute seam tests | `tests/SharpHDL.UnitTests` | Committed | Windows/Linux CI configured; outcome not confirmed |
| Docker/Icarus integration | `tests/SharpHDL.IntegrationTests` | Initial fixture committed | Docker execution not confirmed; Ubuntu package install is non-hermetic |
| Reference simulator baseline | `tests/SimulationBenchmark.cs` | Committed | Reports per-cycle oracle and environment; no numbers claimed |
| Compiled combinational executor | `src/SharpHDL/CompiledCombinational.cs` | **Partial:** pure combinational cones only | xUnit parity checks committed |
| Graph-versus-compiled benchmark | `tests/SharpHDL.Benchmarks` | Committed | Combinational evaluations/sec only; not clock cycles/sec |
| DAP adapter, canonical IR, native compiled cycle simulator | Tracked issues | Planned | Not implemented |
| Browser unified workbench, schematic, formal counterexample replay | Tracked issues | Planned | Existing separate demos do not imply unified workbench |

## 4. Debugger design

Two distinct concepts must not be conflated:

1. **Host debugger:** Existing C#/.NET debugging in VS Code debugs elaboration, static helpers and application code.
2. **Hardware debugger:** Debugs elaborated circuit state at clock boundaries and internal signal transitions. VS Code is a client via the standard Debug Adapter Protocol; the browser workbench is another client of the same engine.

`HardwareDebugSession` currently encapsulates an existing `Simulator`; it provides `Set`, `Read`, `Step(reset)`, `Run(maximumEdges)`, named `Watch`, `BreakWhen`, `BreakOnChange`, `RemoveBreakpoint`, `ClearTrace`, `MaxTraceSamples` and `LastBreakpointId`. The observer callback occurs after the edge, and values in the trace represent post-edge state. Value-breakpoints compare post-edge values and change-breakpoints arm on the first observed sample. Reset reestablishes the breakpoint baseline without triggering stops; zero max samples disables retention. `MaxTraceSamples` is a sample-count cap; if there are multiple watches, it is *not* a cycle-count limit. Current eviction uses list removal, which is sufficient for small fixtures but needs a ring-buffer implementation for large traces.

Current limitations: no multi-clock timestamp, no async/event delta cycles, no source/IR provenance, no DAP, basic VCD watch exporter exists, but no streaming VCD writer, no state snapshot, no memory editor, no pause-in-the-middle-of-an-edge, and no four-state inspection. A hardware watch is not an ordinary CLR local variable.

The shared service should expose: version/feature negotiation, controlled start/stop, atomic clock-edge stepping, grouped state reads, breakpoint events, trace subscriptions, snapshot/checkpoint and breakpoint metadata. DAP should translate protocol concepts without duplicating simulation or waveform behavior. A source breakpoint requires source/CIL-to-IR-to-RTL provenance (DBG-003), not line numbers guessed from generated Verilog.

## 5. Compiled simulation strategy

`CompiledCombinational` compiles a supported *pure combinational* expression cone into a cached expression-tree delegate. It enforces input ranges and explicit error for register-dependent or unsupported operations. It can be tested against the graph simulator using identical inputs. Currently this is not a compiled clocked simulation engine.

Roadmap:
1. Extend compiled expression evaluation to take a *read-only prior-state snapshot* plus inputs.
2. Compile every next-state expression independently against that same snapshot.
3. Commit all pending next-state values atomically, with reset semantics identical to reference simulation.
4. Add RAM operations with explicit old-data/read-write collision rules and bounds.
5. Add elastic streams, processes, multiple domains and four-state validation where appropriate.
6. Cross-check each step with independent reference oracles, generated RTL and deterministic stimuli.

Never benchmark compiled combinational *evaluations/sec* against reference *clock cycles/sec*. The benchmark in `tests/SharpHDL.Benchmarks` currently measures only the former, with identical 16-bit conditional arithmetic and fixed stimuli; it records compilation time separately.

## 6. Verification pyramid

**Fast, Docker-free:** xUnit tests for widths, arithmetic, edge/reset atomicity, breakpoint ordering/retention, graph-versus-compiled parity and algorithmic reference oracles. NSubstitute belongs at seams such as `IHardwareDebugObserver` or future external-process abstractions, **not** as a replacement for actual DUT execution.

**Real HDL integration:** Testcontainers provides real Icarus, Verilator and Yosys processes and isolated toolchains. The committed first Icarus test creates a container, installs the simulator, generates Verilog, compiles a 40-edge counter and checks RTL results. It needs Docker and network access to install Ubuntu packages. Next make a digest-pinned, prebuilt image and add differential seeded trace comparison, Verilator and Yosys. Required CI jobs must fail on missing Docker, not silently skip.

**Timing and board evidence:** Technology mapping, utilization, routing seeds, timing closure, and vendor-tool claims are separate from simulator correctness. Record precise tool versions and workloads.

### Commands

```sh
dotnet test tests/SharpHDL.UnitTests/SharpHDL.UnitTests.csproj -c Release
dotnet test tests/SharpHDL.IntegrationTests/SharpHDL.IntegrationTests.csproj -c Release
dotnet run --project tests/SharpHDL.Benchmarks/SharpHDL.Benchmarks.csproj -c Release
dotnet run --project tests/Tests.csproj -c Release -- --simulation-benchmark
```

Workflow definitions: `.github/workflows/xunit.yml` and `.github/workflows/hdl-integration.yml`. Source commits and workflows are not substitutes for a successful test run.

## 7. Frontend and library roadmap

Build an optional declarative typed C# frontend atop existing graph/CIL synthesis. Support strongly typed ports, explicit clocked processes, record/bundle interfaces, vector and indexed operations, parameterized modules, deliberate sizing/fixed-point rules, source diagnostics and compiler provenance. Preserve C#/F#/VB interop. Reuse the existing AXI/FIFO, CORDIC, numeric and memory blocks; their synthesis behavior must remain target-independent unless the user selects an explicit implementation strategy.

Automatic scheduling should add characterized DSP/RAM/multi-cycle latencies, bounded control flow and resource sharing without silently violating initiation interval, latency or side-effect semantics.

## 8. Integrated UI / hardware system environments

One offline-capable developer workbench, two clients: VS Code extension and browser/desktop UI. Planned panels: source and generated Verilog, hierarchy, waveforms with search and VCD, watches, breakpoints, memory inspection, schematics, pipeline stage occupancy and schedule/resource reports. Include portable snapshots and CPU/peripheral/ROM runner examples; system demonstrations must have an independent architectural oracle.

Schematic levels: structural RTL/IR, generic gate primitive, and mapped vendor netlist (only when a mapping tool runs). Annotate estimates rather than present them as post-route timing.

## 9. Performance measurement protocol

Use fixed seeds and input generators, declare exact design/synthesis hash, control warm-up/JIT, record every run, median and compilation overhead, CPU/OS/runtime/container image, and verify complete output equivalence. Compare equivalent workloads at equal state/output visibility and trace settings. Never extrapolate speedups between different CPU/SoC examples. Publish failure traces and first divergent cycle.

## 10. Delivery tracking and gates

- **0.15:** Clean, passing xUnit unit suite; real containerized RTL differential tests; bounded debugger tracing and breakpoint service; a *measured* compiled simulation path with honest subset description; basic shared debug API and IR specification.
- **0.16:** Initial VS Code DAP; source spans and live waveforms; memory/watch UI; attributed hardware module frontend.
- **0.17:** Target-aware scheduling and parameterized DSP/memory models, independent scientific benchmarks, synthesis-derived resource/timing reports.
- **0.18:** Formal contracts and counterexample imports, complex CDC and interoperability tests, more comprehensive system demonstrations.
- **1.0 readiness:** Stable compatibility policy, complete supported-subset documentation, independent verification, reproducible CI/toolchains and measured example artifacts.

Development issues: [#1–#17](https://github.com/jeffadickson/SharpHDL/issues). The product requirements are the normative backlog; this document records design decisions, dependencies, current implementation and evidence status.

## 11. Remaining open technical risks

- Multiple CIL frontends still need a unified serialized hardware IR and stable source mapping.
- The graph interpreter has two-state assumptions and calls `Read` by recursive evaluation; debug instrumentation/trace recording incurs overhead.
- Current compiled executor rejects sequential state and does not yet deliver clock-cycle acceleration.
- Native compilation and optimized operators must preserve bit masking, signed semantics and short-circuit mux behavior.
- Shared debug state, variable watches and boundary semantics need isolation, snapshots and large-trace ring-buffer optimization.
- Integration tests currently rely on runtime package installation and must become reproducible with pinned toolchain images.
- New code and workflows are **unverified in this GitHub-only session**; build/test failures remain possible and must be triaged before declaring a milestone complete.

## 12. Visualization priority decision (2026-10-14)

**P0:** Source-linked pipeline/dataflow explorer: stage boundaries, operations, graph edges, estimated operator latency, payload register bits, FIFO occupancy and backpressure. **P1:** Module/clock/bus/memory hierarchy inspector and synthesis reports tied to generated Verilog and actual target-tool evidence. **P2:** Optional generic gate-cone schematic/diagram for focused diagnostics and education. Do not build a full-chip interactive gate schematic as a primary workflow. Gate-level *IR, simulation and equivalence* remain important independently of diagram UI.

Current first step: `PipelineHandle.Operations` exposes structured stage/level/width/operation/name data for the structural pipeline scheduler. This does **not** yet include edges, critical paths, real delay, timing closure, DSP mapping or the other CIL pipeline backends. All later viewers must consume structured metadata, not parse human-formatted schedule text. Track source provenance and source-to-stage links explicitly.

## 13. Dataflow graph implementation increment (2026-10-14)

The structural scheduler now emits `PipelineHandle.DataflowGraph`, a serializable DAG with deterministic postorder node IDs, operation/width/logic-level/stage metadata, edges with operand positions, latency, II and payload-register total. The public `StructuralPipeline.Attach` method allows a hardware expression graph to be explicitly attached to typed channels. `DataflowGraph.ToJson()` exports the metadata for offline viewers; xUnit validates topological IDs, stage placement, stable JSON and RTL generation. These are **committed, not yet runtime-verified**.

Scope boundaries: this graph is an *elaboration/scheduling graph*, not a mapped-gate graph or real device timing. Edges represent expression dependencies, not measured hardware routing. Determinism assumes identical expression construction order. It does not yet cover the CIL pipeline scheduler, preserve source locations, show run-time token occupancy, or encode source-to-RTL provenance. The next pass should unify the graph model across CIL scheduling, map register boundaries explicitly, and instrument stage valid/ready transitions without sacrificing throughput.

## 14. CIL pipeline graph and live handshake sampling (2026-10-14)

`CilCompiler.AttachPipeline` already calls `PipelineScheduler.Attach`, which means the CIL frontend now inherits the same deterministic `PipelineHandle.DataflowGraph` as explicitly attached structural pipelines. No second DAG compiler is necessary. The node names incorporate existing CIL method/IL-offset hints when available. **Source file/line provenance is not yet implemented.**

`PipelineFlowProbe` and `PipelineSignalProbe` inspect the *real simulator* at a specified pre-edge observation point and return `PipelineFlowSample` containing stage occupancy, input/output valid/ready, accepted transfers, and stalls. `PipelineSignalProbe` accepts exposed AXI or native ready/valid expressions, so it can inspect a CIL-generated pipeline with AXI adapters. These probes do not advance the simulation or predict token identity. `CilDataflowTraceTests` is a newly committed self-contained xUnit fixture covering CIL DAG generation and backpressure, but no passing test execution has been verified.

Next: stable source-span mapping, trace persistence across clocks, token sequence IDs/correlation, visual inspector and an executable CI build run. Timing numbers and DSP/BRAM mapping are separate future steps.

## 15. Browser pipeline inspector milestone (2026-10-14)

A static browser inspector now lives at [docs/pipeline/index.html](pipeline/index.html) with [instructions](pipeline/README.md). `PipelineTraceRecorder` writes versioned JSON combining the scheduler dataflow DAG and actual pre-edge `PipelineSignalProbe` measurements. The `--pipeline-browser` command runs a structural 16-bit streaming pipeline in the real .NET graph simulator and writes `docs/pipeline/trace.json` (generated file, not presently bundled). The UI supports cycle seek, next/previous edge, playback, stage occupancy, stall/transfer indicators, graph operation list, and selected node dependencies and CIL-derived hint names where present.

**Scope:** this is an interactive **recorded-trace replay viewer**, not yet a live in-browser simulator or VS Code debug adapter. There are no inferred token identities or exact C# source spans. Existing APIs and tests are committed, but .NET SDK and GitHub networking are unavailable in this environment, so the milestone is **implemented preview, unverified runtime**. Next release gate: independent xUnit/CI run, Playwright UI smoke tests, CIL-generated fixture, source span provenance, and a common live debug transport.

## 16. Local live pipeline debugger (2026-10-14)

`tools/SharpHDL.Live` is a new ASP.NET Core minimal API host bound to `127.0.0.1:8093`, serving the pipeline browser assets and a shared, locked, genuine .NET `Simulator` instance. Endpoints: GET `/api/state`, POST `/api/reset`, POST `/api/step` with `{valid,data,ready}`. Live stepping updates the real SharpHDL stimulus and rising edge, then returns current stage-valid state, handshakes and data. A transaction-order queue assigns monotonically increasing IDs to input *acceptance* and retires them on output acceptance; these are **not yet per-stage IDs**, and the queue is not a substitute for internal token instrumentation. Pre-edge handshake reporting and post-edge state sampling must be distinguished. This host is deliberately localhost-only with no authentication and must not be exposed to untrusted networks.

Source mapping currently relies on CIL-generated method/IL-offset hints in the existing DAG nodes; genuine source-file/line Portable PDB mapping and editor navigation remain open. The new live API and UI have not been executed in CI in this session. Next gates are .NET build, API integration test, Playwright UI smoke test, and compiler-PDB provenance plus per-stage tag model, followed by a reusable debug protocol for VS Code.


**2026-10-14 refinement:** The localhost host now maintains a stage-by-stage transaction-ID shadow register model, advancing IDs according to observed ready/valid and stage occupancy, and checks tag occupancy against actual simulator `StageValid` bits after each edge. `stageTokenIds` in `/api/state` identifies the accepted transaction occupying each stage of the supported single-clock elastic pipeline. This is a debug overlay rather than tags physically synthesized into RTL; generalized multicycle actors, feedback, multi-clock and reset-in-flight behavior require independent validation. The code is still unexecuted in this session. Genuine Portable PDB source spans remain open.

## 17. Portable PDB and VS Code adapter development (2026-10-14)

A new `PortablePdbSourceMap` maps compiler-generated `IL_xxxx` operation names to matching Portable PDB sequence points, storing `SourceFile`, `SourceLine`, `SourceColumn` and `IlOffset` on `PipelineGraphNode`. `CilCompiler.AttachPipeline` applies this mapping after graph scheduling. Missing PDBs leave fields unset rather than inventing locations. An xUnit test checks offsets and an actual CIL-pipeline mapping with debug symbols when present. Mapping is based on the nearest preceding visible sequence point, which may cover several IL operations and requires careful validation for optimized and inlined code.

The experimental [VS Code extension](../vscode/sharphdl/README.md) uses an inline Debug Adapter Protocol adapter. It talks to `tools/SharpHDL.Live` and provides connect/launch, stepping a real hardware clock, hardware scopes and variables, and stop notifications. **This is not a finished debugger:** source-line breakpoints deliberately report `verified:false`; the live demo remains a fixed structural circuit, not the CIL pipeline bearing Portable PDB mappings. The adapter does not yet implement continuous execution, real source-oriented stack frames, conditional source breakpoints or per-token source stopping. CI/build/UI validation remains pending. Future work must connect DAP launch to a named compiled design, translate source spans to hardware stage/token predicates, and test both successful and rejected breakpoint semantics.

## 18. Build and developer tooling policy (2026-10-14)

Root `justfile` is the recommended human entry point, with a minimal compatibility `Makefile` delegating to `just`. The existing .NET/MSBuild projects remain authoritative for C# and F# compilation; existing shell/Python verification tasks remain authoritative for specialized HDL flows. `global.json` selects the .NET 8 SDK line (8.0.100, latest installed patch in that feature band). `tools/dev.py doctor` checks local tool availability, `setup` restores .NET projects without elevated privileges or implicit package-manager changes, and `verify` runs core and container tests but **fails with a nonzero status** if required Docker is unavailable. External RTL simulators are not installed by `setup`; Testcontainers remains the preferred isolation boundary, while legacy scripts may need native tools for full release runs. Avoid conflating successful restore/build with a complete verified FPGA toolchain. Initial scripts and manifests are committed but not verified on all host OSs.


**2026-10-14 automatic toolchain provisioning:** `just setup` now builds the [development container](../tools/Dockerfile.dev) with the .NET SDK, Icarus, Verilator, Yosys, nextpnr/IceStorm, Python and Node, and restores .NET dependencies. Container commands run builds and xUnit tests without a native .NET installation. Docker itself must be installed and running; image and package versions are not yet digest locked and build results have not been verified in this GitHub-only session.

## 19. Fresh clone toolchain smoke gate (2026-10-14)

A new `tools/container-smoke.sh` and `.github/workflows/provisioning.yml` execute actual .NET compilation and xUnit, reference simulation, Icarus parsing and Yosys elaboration from the auto-provisioned environment. `just container-verify` exposes the same path locally. The `setup` command now restores all .NET projects and checks installed HDL executables. It does not validate real vendor timing closure. The code is committed but not yet confirmed passing on GitHub Actions.

## Simulator checkpoint increment (2026-10-14)

`src/SharpHDL/SimulatorSnapshot.cs` adds `Simulator.CaptureSnapshot()` and `RestoreSnapshot()` for in-process rollback of every tracked signal and scratch RAM cell. Snapshots are deep-copied and tied to an elaborated Module instance; restoration rejects another module. Pre-reset uninitialized registers remain uninitialized. `tests/SharpHDL.UnitTests/SimulatorSnapshotTests.cs` exercises replay, RAM rollback and invalid ownership. This is the first increment for PAR-010, not an on-disk save-state format, debugger memory editor, or proof of performance. Test execution has not been confirmed.

## Debugger-level checkpoint/replay increment (2026-10-14)

`src/SharpHDL/DebugCheckpoint.cs` provides `CaptureCheckpoint`/`RestoreCheckpoint` on `HardwareDebugSession`. It wraps the simulator deep-copy RAM/register snapshot and records cycle number, last-triggered breakpoint, value/change breakpoint baselines and retained watch samples. Restore does not fabricate a hardware clock edge or notify an observer. A checkpoint is scoped to the owning session and restore refuses changed breakpoint sets before state mutation. Test fixtures: `tests/SharpHDL.UnitTests/DebugCheckpointTests.cs`.

**Boundaries:** In-process only; watch registration/retention settings and breakpoint membership must remain stable; trace restoration is a state rewind rather than timestamp-preserving branch history. Hosted live ASP.NET debug service and VS Code DAP do not yet expose rewind; no passing new test execution was observed in this session. Portable snapshots, memory editing and integration with actual TinyRisc debugger remain planned.

## Shared VS Code/browser live rewind (2026-10-14)

`tools/SharpHDL.Live/Program.cs` now keeps a bounded in-memory pre-edge checkpoint stack (128 cycles). Each checkpoint includes full graph simulator state (registers, inputs, RAM), cycle, accepted transaction sequence, exact per-stage transaction-ID shadow array and the pending token queue. `POST /api/stepBack` restores this jointly under the existing synchronization gate; reset empties history; `/api/state` reports `canStepBack` and `rewindDepth`. The implementation only stores a checkpoint after a completed step to avoid adding corrupt history on a failed step. `vscode/sharphdl/extension.js` implements Debug Adapter Protocol `stepBack`; the browser inspector has a live Back button. Both use the same service. Boundaries: in-process only, fixed pipeline example, no source breakpoints, no hardware multi-clock rewind, no persistence, and no passing runtime test evidence yet.

## Transactional live step + API verification (2026-10-14)

`tools/SharpHDL.Live/Program.cs` wraps each /api/step in a pre-step checkpoint and an exception rollback path, including both simulator state and the pipeline debug overlay. `tests/test_live_rewind_api.py` uses Python standard-library HTTP requests against the real ASP.NET Core server, checking complete round-trip state signatures, exact clock positions, no mutation after 400 invalid input, 409 on empty history, reset and deterministic re-execution. `.github/workflows/live-rewind.yml` builds and launches the server then executes this test. Test run outcomes have not been independently confirmed; in-process checkpoints and the fixed example are not yet source-line debugger or portable full-system validation.

## Dual-level authoring and compiled RAM backend

SharpHDL deliberately offers two complementary authoring levels: explicit structural hardware (`Module`, `Signal`, `Expr`, `Register`, `Next`, `NextWhen`, `Mux`, `SynchronousRam`) for RTL-level control, and scoped CIL behavioral/process synthesis with scheduling for high-level specification. Both should ultimately lower into a canonical typed hardware IR. RHDL's user-facing DSL is Ruby; Rust/Cranelift accelerate its runtime simulation rather than authoring the hardware in Rust.

`CompiledSequentialSimulator` now compiles register D-input cones and synchronous RAM address/write-enable/write-data expressions into .NET expression-tree delegates, with one mutable RAM image per memory. Next-state logic samples all old register and memory values; writes commit after sampling; reset clears registers and synchronous read-output registers but retains RAM contents. The initial differential RAM test covers randomized writes/reads and reset. Verified compilation, execution, broader signed semantics and TinyRisc performance evidence remain open. This first version prioritizes correctness; per-register BigInteger arrays are not yet the intended optimized packed-machine-code backend.

## Compiled sequential full-CPU differential gate

`CompiledSequentialSimulator.Read(Expr)` compiles output probes against pre-existing register/input state and the backend's mutable RAM images. `examples/TinyRisc/Program.cs` provides `--compiled-tinyrisc`, which elaborates a shared TinyRisc16 Hello World module, runs reference and compiled engines with identical inputs and periodic UART stalls, and compares every hardware output each edge. It checks exactly-once byte payload and successful HALT. `tests/run-tinyrisc.sh` now invokes this mode before Verilog testbenches and the serial UART decoder. This is a **test gate committed to the repository, not a claim of passing execution**. A full simulator release still needs passing .NET/Icarus/Yosys results, benchmarking, four-state/timing scope decisions and migration of the implementation to the Simulation assembly.

## Sequential simulation performance qualification

`tools/SharpHDL.SimulatorBench` is an executable baseline for the graph interpreter versus compiled delegate engine on an identical enabled 32-bit counter circuit. It warms up both engines, reports JIT setup separately, times equal numbers of clock edges, and rejects disagreement in final state. Run `make setup` then `make benchmark-simulator`, or use `just benchmark-simulator` with a native .NET SDK. **This is not a TinyRisc or RHDL head-to-head result.** No execution results have yet been observed and performance claims remain prohibited until validated. The TinyRisc differential gate now uses literal CR/LF bytes in its payload assertion.

## Compiled simulator hot-path buffer reuse

`CompiledSequentialSimulator` now maintains a preallocated operand vector shared by compiled next-state and probe delegates, plus a reusable pending-register vector. Input updates refresh the operand vector immediately, while a clock edge evaluates all D cones and RAM writes from pre-edge values before committing registers and updating the vector. `CompiledCombinational.EvaluateUnchecked` is internal, and only called by the state-owning compiled engine after width checks at the public Set boundary. Tests include a 100-edge mixed input/register/output comparison. This reduces buffer allocations but is not yet a fused full-circuit JIT, a packed 32/64-bit fast path, or an independently measured performance improvement. The current session lacks .NET/Icarus/Yosys, so execution is unverified.

## Simulator performance increment: fused clock-edge path

The compiled backend now emits a single .NET JIT expression-tree delegate for all register next-state assignments, directly writing into a preallocated pending-state buffer. Widths <=64 use unchecked UInt64 arithmetic for add/subtract/multiply/bitwise operators, with the existing end-of-node width mask; wider values retain BigInteger operations. RAM-write owner/address/data buffers are reused across edges. Correctness coverage added for 8/32/64/65-bit overflow, existing synchronous RAM, and TinyRisc16 UART differential tracing. `make benchmark-tinyrisc` runs correctness gates before an active-ROM CPU timing run and separately compiles/executes the same UART RTL testbench under Verilator. **Verilator wall-clock testbench time is not yet a cycle-matched throughput comparison.** No passing results or performance improvements are claimed until CI evidence is reviewed. 

## VS Code Verification Explorer (2026-10-14)

The existing VS Code DAP prototype remains a client of the localhost reference simulator. The extension now additionally contributes an Explorer tree for the TinyRisc verification workflow, without replacing Verilog syntax/language extensions. It runs `make setup` and `make benchmark-tinyrisc` through VS Code process tasks and opens existing verification logs, generated RTL, and waveforms through VS Code's registered editors. A separate Verilator lint command publishes source diagnostics in Problems. Artifacts are constrained to known workspace-relative paths, and task results are not inferred from stale output files. The Explorer's parser/manifest tests and JavaScript syntax are checked in `.github/workflows/vscode-sharphdl.yml`.

**Scope:** first iteration supports the repository-root TinyRisc example. The DAP adapter does not execute Verilator; no automatic source-to-RTL cross-probing, general project manifest, live wave viewer or verified graphical extension-host run is claimed. Track generalization and integration in [issue #25](https://github.com/jeffadickson/SharpHDL/issues/25).

## VS Code generalized Verilator workflow (issue #25, October 2026)

The v0.3 VS Code extension adds a versioned `sharphdl.project.json` workspace manifest. Each design has a stable ID, top-level testbench module, ordered HDL source files, build directory, optional waveform files and an optional exact source/RTL provenance JSON map. Inputs are validated as workspace-relative paths; arbitrary build commands are not accepted. The **SharpHDL Projects** Explorer uses VS Code ProcessExecution tasks for `verilator --binary --timing --trace`, then executes the produced testbench binary only after a successful build. The default task runner uses the existing provisioned `sharphdl-dev:local` Docker image; a native-host runner is configurable. VS Code task termination cancels execution, and a problem matcher captures Verilator source diagnostics. Waveform files open through registered editors, not an invented built-in viewer.

The existing **SharpHDL Verification** view continues to launch the full TinyRisc differential regression and performance benchmark. The new `make vscode` target pulls a pinned Node 22 Docker image, runs JavaScript tests and packages an installable VSIX; CI repeats packaging and runs a real multi-file Verilator manifest fixture. The documentation is in [VSCODE_GETTING_STARTED.md](VSCODE_GETTING_STARTED.md).

Limitations: provenance navigation only operates when the compiler supplies an exact map (not yet generated automatically), and the reference simulator DAP is not connected to Verilator. Full graphical extension-host, multi-platform GUI, wave-signal cross-probing and hardware-condition breakpoints are tracked in [issue #25](https://github.com/jeffadickson/SharpHDL/issues/25).

## VS Code VCD cross-probe and hardware conditions (October 2026)

Extension v0.5 adds a bounded VCD parser and webview timeline with digital value inspection, captured clock rising-edge navigation, and a signal-to-C# action when exact CIL-to-RTL provenance exists. The Projects Explorer uses the current C#/RTL cursor to preselect mapped signals. TinyRisc UART can emit a real six-signal VCD using `+sharphdl_trace`; the manifest and Verilator project task expose it. The independent VCD file's timebase is not conflated with the localhost reference simulator's clock count. VCD size and signal/event limits are enforced; FST opens in an external registered editor.

The localhost DAP now supports explicit post-edge hardware conditions (cycle threshold, ready/valid/stall and pipeline occupancy) and bounded Continue/Pause, retaining step-back. This is **not** Verilator debugging, source-line breakpoint equivalence or replay of an arbitrary Verilator trace. A VS Code extension-host smoke test now runs in Linux, Windows and macOS CI; GUI acceptance depends on the latest workflow outcome. The Makefile packages extension v0.5 as a VSIX. [Issue #25](https://github.com/jeffadickson/SharpHDL/issues/25) tracks remaining integration validation and unsupported features.
