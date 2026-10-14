# SharpHDL product requirements

Status: Baseline proposal for version 0.15 and later. Owner: SharpHDL maintainers.
Last revised: 2026-10-14.

## 1. Vision and principles

SharpHDL is a .NET-native hardware construction, compilation, simulation, verification, and visualization environment. It shall let engineers write statically typed hardware in C#, F#, and VB.NET, compile the supported subset to a canonical hardware intermediate representation, evaluate designs locally or in a browser, and generate portable synthesis-ready RTL and reports. FPGA and scientific/streaming applications are first-class use cases.

Principles: deterministic semantics; reject unsupported constructs instead of silently falling back; one documented meaning per supported operation across backends; measurement before performance claims; explicit cycle, reset, overflow and resource contracts; interoperability before proprietary lock-in; reproducible builds with freely available reference tools; approachable APIs without hiding hardware costs.

## 2. Acceptance conventions

Each requirement has a persistent identifier, priority (P0 critical, P1 high, P2 later), and verification criterion. A feature is **Done** only with tests, documented limits, a reproducible invocation, and an entry in the conformance matrix. **Partial** means an existing implementation does not yet satisfy the full criterion. Proposals below are not claims of existing functionality.

## 3. Language, typing, elaboration

- **LANG-001 P0:** Preserve structural graph API and existing CIL synthesis; both produce a common IR. Verify same circuit semantics on representative counter, FSM, RAM and streaming designs.
- **LANG-002 P0:** Provide a concise attributed hardware-module frontend with declared ports and clocked/combinational methods. Verify C# examples, diagnostic behavior, and generated HDL. Annotations alone do not imply that arbitrary CLR objects synthesize.
- **LANG-003 P0:** Define explicit bit-width, signedness, truncation, extension, comparison, shift and overflow rules; expose type-safe diagnostics. Differential tests include 1, 8, 32, 64, 65, and 257 bits.
- **LANG-004 P1:** Support typed aggregate ports (record/bundle, nested fields), packed and unpacked arrays, and constant-sized vectors with deterministic flattening and reversible metadata. Tests verify round trips and packed layout.
- **LANG-005 P1:** Support hardware-indexed vector access and dynamic slices with specified out-of-range semantics and synthesis-cost reports.
- **LANG-006 P1:** Expand synthesizable CIL types to signed and unsigned 64-bit, enums, arrays and value-type records within explicitly documented ownership/aliasing limits. Reject unsupported object, reference and dynamic cases with source locations.
- **LANG-007 P1:** Add first-class fixed-point, saturating, rounding and error/overflow operators, with exact reference models and full-width arithmetic. Test signed boundaries and ties-to-even.
- **LANG-008 P1:** Provide parameterized, reusable modules with deterministic elaboration, generic types and compile-time configuration.
- **LANG-009 P1:** Emit actionable compiler diagnostics with source method, IL offset, path, offending construct, and supported alternative.
- **LANG-010 P2:** Support reusable interface types with producer/consumer direction reversal and CDC-aware clocks.
- **LANG-011 P2:** Provide a documented compatibility and migration plan for existing public APIs; examples from previous versions must continue to build or receive explicit migration guidance.

## 4. Compiler IR and optimization

- **IR-001 P0:** Define a versioned, serializable typed hardware IR for modules, ports, operations, registers, clock domains, memories, processes, streams and schedule metadata. Validate schema/version on load.
- **IR-002 P0:** Specify combinational evaluation order, clock-edge commit, reset, initialization, X/Z behavior at boundaries, and deterministic scheduling. Check equivalent results from all supported execution backends.
- **IR-003 P0:** Implement IR validation for widths, multiple drivers, combinational cycles, clock-domain mismatches, invalid memory ports and unschedulable constructs, with negative test fixtures.
- **IR-004 P1:** Track provenance from source and CIL through IR operations to generated Verilog and simulator symbols; debugger round trip works on representative examples.
- **IR-005 P1:** Implement pass infrastructure with before/after dumps and golden tests: constant folding, dead-code elimination, bit-width reduction and common subexpression elimination.
- **IR-006 P1:** Generate a stable machine-readable netlist/resource report, including logic, registers, BRAM, DSP and any uncharacterized estimate fields.
- **IR-007 P2:** Provide gate-level lowering and diagrams for supported primitives; do not imply technology-mapped timing accuracy from a generic gate model.

## 5. Pipeline scheduling and science

- **SCHED-001 P0:** Preserve verified ready/valid elastic pipelines and II=1 for supported stateless streaming operations under an always-ready sink.
- **SCHED-002 P0:** Generate stage-by-stage schedule reports with latency, II, register payload count, clock boundaries, backpressure rules and source provenance.
- **SCHED-003 P1:** Implement operation latency models for multiplication, division, RAM, vendor DSP and other multicycle units, with explicit supported target profiles.
- **SCHED-004 P1:** Extend scheduling to bounded control flow, statically analyzable loops and RAM accesses without duplicating side effects. For rejected cases, produce precise reasons.
- **SCHED-005 P1:** Support resource sharing subject to initiation interval and latency constraints; report area-throughput tradeoffs.
- **SCHED-006 P1:** Support explicit target-clock objectives with pre-route estimates and post-route timing feedback, always distinguishing requested and achieved frequencies.
- **SCHED-007 P1:** Provide deterministic numerical reference models and RTL implementations for FIR, IIR (with feedback constraints), CIC, FFT building blocks, CORDIC, matrix/vector kernels and calibrated fixed-point quantization.
- **SCHED-008 P2:** Support scheduling across multiple kernels, FIFOs and dataflow graphs with deadlock, rate-balance and capacity diagnostics.
- **SCHED-009 P2:** Add design-space exploration across pipeline stages, DSP usage, registers, BRAM and throughput; report Pareto candidates based on verified synthesis outputs.

## 6. Memories, buses, targets

- **MEM-001 P0:** Preserve synchronous RAM semantics including read/write collision ordering, reset retention, bounds handling, initialization and target-independent simulator behavior.
- **MEM-002 P1:** Implement 1R1W and true dual-port RAM, byte enables, configurable write-first/read-first/no-change semantics, and deterministic same-address collision handling; verify against target-specific primitives.
- **MEM-003 P1:** Provide vendor mappings for Xilinx and Intel families with explicit target/primitive compatibility and mapping tests; no mapping claim without evidence.
- **MEM-004 P1:** Support ROM/file initialization with provenance, checksum and portable build artifacts.
- **MEM-005 P1:** Provide external memory interfaces and reusable buffering/arbitration components; track latency and outstanding requests.
- **BUS-001 P0:** Preserve and test AXI4-Stream protocol including stalled transfers, backpressure, TLAST and exactly-once delivery.
- **BUS-002 P1:** Expand AXI4-Lite/register-bank composition, metadata and autogenerated conformance tests.
- **BUS-003 P1:** Support black-box Verilog/SystemVerilog modules, parameter passing, port widths and mixed-language integration; verify elaboration and testbench interoperability.
- **BUS-004 P2:** Add clock-domain crossing primitives with attributes, synchronizer constraints, asynchronous FIFOs and independent CDC checks.

## 7. Simulation and validation

- **SIM-001 P0:** Establish a backend-independent cycle-accurate reference contract (clock edge, reset, delta-cycle settling, memory read/write ordering and register atomicity).
- **SIM-002 P0:** Supply reproducible benchmark suites with design hashes, warm-up, timing methodology, throughput, correctness checks and environment metadata; compare only equal workloads.
- **SIM-003 P0:** Introduce a compiled .NET simulation backend for a documented IR subset with interpreted fallback *only when explicitly requested* and diagnostic reporting; verify equivalence and measure speedup.
- **SIM-004 P1:** Support deterministic waveform/event tracing and VCD output with selectable scopes and runtime cost measurement.
- **SIM-005 P1:** Support source-level breakpoints, signal-value conditions, watches, stepping, reset/restart and bounded trace buffers.
- **SIM-006 P1:** Define four-state values (0/1/X/Z) for supported simulation boundaries; specify unknown propagation and explicit two-state fast-mode behavior.
- **SIM-007 P1:** Implement snapshots/checkpoints including design compatibility hashes, memory contents and deterministic restore.
- **SIM-008 P1:** Differential-test against generated RTL using Verilator/Icarus and independent software reference models; preserve first failing cycle and minimized input trace.
- **SIM-009 P2:** Add incremental/event-driven execution to avoid recomputation when signals are stable, with correctness and performance counters.
- **SIM-010 P2:** Provide WebAssembly simulation for browser use, and benchmark JS/WASM/native parity.

## 8. Verification and contracts

- **VER-001 P0:** Add assertion/assumption/cover model with explicitly separated simulation and formal meanings, clear reset/clock scope and source metadata.
- **VER-002 P1:** Support design-by-contract syntax for preconditions, postconditions and invariants in the supported hardware subset; distinguish illegal inputs from implementation failures.
- **VER-003 P1:** Emit bindable SVA or equivalent properties and provide a formal invocation path using freely available solvers when applicable.
- **VER-004 P1:** Prove or bounded-check FIFO preservation, reset, no duplicate/lost tokens, memory semantics and CDC adapter invariants for representative fixtures.
- **VER-005 P1:** Support IR-to-RTL equivalence harnesses and produce counterexamples importable by the waveform viewer.
- **VER-006 P2:** Add property-based input generation, mutation/regression testing and coverage accounting.

## 9. Workbench, learning, integration

- **UI-001 P0:** Create a cohesive browser workbench with source/design explorer, runtime controls, signal watch, waveform viewer and compiler diagnostics.
- **UI-002 P1:** Add interactive schematic, hierarchy, and pipeline-stage visualizations with cross-probing to source and waveforms.
- **UI-003 P1:** Provide RAM inspectors and editors, binary/hex export, snapshots and reset controls.
- **UI-004 P1:** Provide an example gallery with counter, FSM, FIR, AXI pipeline, TinyRisc and mixed-language designs, and an executable verification command for each example.
- **UI-005 P1:** Support locally hosted/offline operation; never require cloud access for simulation or design editing.
- **UI-006 P2:** Provide accessible keyboard navigation, responsive layout and reusable UI components for desktop and mobile browsers.
- **DEV-001 P0:** Automated build/tests for supported OS/SDK versions and pinned toolchain matrix, with reproducible generated artifacts.
- **DEV-002 P0:** Record feature maturity as implemented/partial/planned, evidence and explicit limits in a living conformance matrix.
- **DEV-003 P1:** Provide versioned CLI commands for compile, elaborate, simulate, export, check, benchmark and report with stable structured output.
- **DEV-004 P1:** Document tutorials, engineering reference, target limitations, examples, architecture and migration practices.
- **DEV-005 P1:** Implement diagnostics and telemetry as locally inspectable artifacts by default, with opt-in external exporting.
- **DEV-006 P2:** Provide an extension interface for target technologies, peripheral models, code generators and third-party simulator plugins.

## 10. Milestones and acceptance gates

**0.15 — Simulation baseline and compiler foundations**
- Freeze the simulation semantics and benchmark methodology (SIM-001, SIM-002).
- Implement a measured first compiled-simulator subset (SIM-003), not just an API stub.
- Design the versioned IR and conformance matrix (IR-001, IR-002, DEV-002).
- Ship a first integrated browser waveform/control experience (UI-001, scoped subset).
- Gate: interpreter, compiled .NET and independent RTL agree cycle-for-cycle on counters, arithmetic, state machine, single-port RAM and a ready/valid pipeline; report throughput with environment details.

**0.16 — Source-to-signal developer experience**
- Attributed frontend proof of concept, diagnostics and source mapping.
- Browser breakpoints, watches, snapshots and source-linked waveforms.
- Gate: users can inspect and replay a failing example from C# source to waveform.

**0.17 — Target-aware high-level synthesis**
- Multicycle scheduling and explicit resource allocation; larger memory shapes.
- Gate: reference FIR and matrix kernel pass correctness, II/latency and area/timing reports with vendor-tool limitations disclosed.

**0.18 — Verification and interoperable subsystems**
- Contracts, formal harnesses, additional AXI integration and mixed-language tests.
- Gate: sample properties and imported counterexamples run in CI and viewer.

**1.0 readiness (not a version promise)**
- Defined supported language subset; stable IR versioning; reproducible toolchains; examples and published benchmark methodology; cross-backend conformance; documented known failures; users able to build and debug without hidden setup.

## 11. Non-goals and scope discipline

Not arbitrary C#/CLR synthesis; not automatic translation of GC, threads, reflection, IO or dynamically allocated objects; not guaranteed MHz from analytical timing estimates; not a replacement for FPGA implementation tools; not claims of signoff-level analog or transistor behavior; not universal support for every SystemVerilog construct; not hidden fallback to host execution when synthesis fails.

## 12. Implementation plan: first sprint

1. Add this specification and a machine-checkable conformance register.
2. Add deterministic simulator benchmark fixtures, seeded inputs, common trace/oracle, and JSON/Markdown result schema.
3. Verify current simulator against that oracle before attempting optimized execution.
4. Implement an IR-compiled combinational/edge executor for counters and arithmetic, add RAM and streams after parity.
5. Add CI gates; publish results with CPU/OS/runtime, workload hash and correctness evidence.

Track each workstream as an issue; do not mark a requirement implemented on the strength of documentation alone.

## 13. Integrated debugging architecture and developer tooling (expanded)

The primary engineering editor is VS Code (and compatible Debug Adapter Protocol clients). The browser workbench is a second client of the **same** simulator/debug service. Never build a second hardware execution model merely to support the browser. Standard .NET debugging of elaboration and host code remains independent of hardware-clock debugging.

- **DBG-001 P0:** Define a versioned simulator-debug service API for launch/attach, run/pause, reset, single-edge/multi-edge step, snapshot, state inspection and trace subscription. Specify deterministic responses and feature capability negotiation. Verify operation from a CLI without a UI.
- **DBG-002 P0:** Add a Debug Adapter Protocol implementation supporting launch, disconnect, continue, pause, step, scopes, variables and breakpoint lifecycle, backed by simulator state (not .NET CLR variables). Test against DAP protocol fixtures and VS Code.
- **DBG-003 P0:** Preserve compiler source provenance (C#/F#/VB method, source span and CIL offset) through IR optimization, scheduling and RTL emission. Round-trip from source to stage/signal and generated HDL. Where multiple mapped nodes exist, show all.
- **DBG-004 P0:** Implement clock-cycle, signal value/edge/change, state-transition and assertion-failure breakpoints. Define whether a breakpoint observes pre-edge or post-edge values; test reset and multi-cycle stages.
- **DBG-005 P1:** Support hardware watches and hover values in binary, decimal, signed, hexadecimal and fixed-point formats; show X/Z when enabled and mark unavailable/optimized-away signals explicitly.
- **DBG-006 P1:** Add signal hierarchy exploration, watch groups, search/filter, and exportable watch configurations. Verify stable hierarchical identities between simulator/RTL.
- **DBG-007 P1:** Add trace cursors, value-change database, bounded buffering and streaming VCD/FST where supported; compare emitted traces to cycle oracles. Expose capture overhead and dropped-event conditions.
- **DBG-008 P1:** Provide RAM/ROM and peripheral inspectors: address, width, byte order, reads/writes, diff, binary/hex dumps, import, and safe paused-state editing. Preserve snapshots and determinism.
- **DBG-009 P1:** Add portable simulation snapshots with memory, peripheral and pipeline state, versioned format, IR/build hashes, optional compressed backing, and atomic import. Test resumed-cycle identity.
- **DBG-010 P1:** Add pipeline debugger that visualizes stage occupancy, accepted/stalled tokens, side-effect commits, bubble propagation, latency and II. Verify displays against instrumented traces.
- **DBG-011 P1:** Support custom VS Code extension contributions: SharpHDL launch configurations, run/debug tasks, waveform and schematic webviews, RTL links and diagnostics. Reuse existing C# language server/editor functionality; do not fork it.
- **DBG-012 P1:** Provide target-aware hardware/Verilog mapping for diagnostic symbols and support external simulator waveforms. Explicitly distinguish RTL simulation from post-placement timing simulation.
- **DBG-013 P2:** Add backwards stepping via deterministic trace replay/checkpoints, with documented performance and device-I/O limitations.
- **DBG-014 P2:** Provide multi-clock time-based stepping, domain-specific breakpoint scoping, and CDC signal annotation.
- **DBG-015 P2:** Add terminal/TUI debug client built on the shared debug service for headless and SSH environments.
- **DBG-016 P1:** Add a formal-counterexample importer mapping witness values to source-linked waveforms and step controls.
- **DBG-017 P1:** Capture a reproducible failure bundle: IR/hash, input stimuli, simulator version, seed, breakpoints, trace and environment; redact secrets and allow offline replay.

## 14. Feature completeness: simulation, visualization and system-level examples

This is a requirements inventory for an end-to-end hardware development environment. These requirements may overlap the core requirements; overlapping IDs establish more specific acceptance criteria without replacing the baseline.

- **ENV-001 P0:** Unified navigation among source, hierarchy, waveform, watch table, memory, schematic, synthesized RTL and console; persist layout/settings locally without network access.
- **ENV-002 P1:** High-performance scalable waveform navigation with time cursors, search, grouping, radix selection, zoom, triggers and VCD import/export; large traces must not lock the UI.
- **ENV-003 P1:** Interactive hierarchical schematics with auto-layout, pan/zoom, port/net highlighting, drill-down, live activity and SVG/PNG/DOT export. Ensure an accessible non-WebGL fallback.
- **ENV-004 P1:** Schematic generated both at pre-synthesis RTL/IR and generic gate-level; distinguish inferred logic from technology-mapped primitives.
- **ENV-005 P1:** Browser runners for generic combinational/clocked modules, a small CPU, TinyRisc, streaming science pipeline and a memory/peripheral system.
- **ENV-006 P1:** Provide reusable synthesizable library blocks: logic gates, encoder/decoder, multiplexers, counters, FSMs, registers, arithmetic units, RAM/ROM, FIFO, serial I/O, and CPU datapath helpers.
- **ENV-007 P1:** Provide circuit metrics: primitive/gate count, registers, nets, logical depth, inferred memories/DSPs, switching activity and resource mapping; qualify estimates until technology mapping.
- **ENV-008 P1:** Provide backend selection with runtime capabilities (reference interpreter, optimized native, external RTL, browser WASM), performance counters, setup costs and fallbacks reported explicitly.
- **ENV-009 P1:** Provide a CLI with discover/list, elaborate, simulate, synthesize, diagram, export, inspect, benchmark, verify and debug subcommands; produce structured output and stable exit codes.
- **ENV-010 P1:** Provide an embeddable example/system harness with CPU, ROM/RAM, MMIO, UART, keyboard/display emulation and binary loading; verify against an architectural software oracle.
- **ENV-011 P1:** Provide debug console commands for help, run/step/pause/reset, watch/breakpoint management, memory reads/writes and backend selection; commands should work in desktop and browser.
- **ENV-012 P1:** Provide portable system snapshots including memory contents and reset/start addresses, and optionally associate user-generated snapshots with a named example.
- **ENV-013 P2:** Add desktop packaging of the web workbench for offline use, without duplicating backend code.
- **ENV-014 P1:** Provide formal and simulation regression dashboards with passing/failing tests, runtime, design revisions and downloadable failure traces.
- **ENV-015 P2:** Support optimized gate-level simulation and optional acceleration where benchmarks justify it; never require a GPU to use the tool.
- **ENV-016 P1:** Provide an explicit supported hardware semantic profile for 2-state fast simulation and 4-state validation, including reset and initialization rules.
- **ENV-017 P1:** Offer source browsing for all generated code and per-component export, plus HDL/IR/netlist files downloadable independently.
- **ENV-018 P2:** Provide guided interactive educational labs using actual generated designs, shared simulation engine and waveform view, not a separate toy model.

## 15. Debugging acceptance scenario

A user opens a SharpHDL design in VS Code, compiles with the supported frontend and starts a hardware debugging session. They select a clock domain, set a signal-change breakpoint, inspect a fixed-point register, examine its source expression, and step a clock. A waveform pane and schematic show the same post-edge value; pipeline occupancy explains stalls. They export VCD and a reproducible trace bundle. The same example is loadable offline in the browser, with matching waveforms and memory state. If the design violates a formal assertion, its counterexample imports into the same debug timeline.

Gate: all observable state is derived from the canonical IR and simulator, cross-client values match, and no unsupported construct silently executes only as CLR code.

## 16. Testing infrastructure and quality gates

- **TEST-001 P0:** Introduce xUnit-based deterministic unit and parameterized tests for simulation, language type rules, reset/clock edges and dataflow semantics; preserve existing regression cases during migration.
- **TEST-002 P0:** Use NSubstitute only at external seams (debug observers, tool invokers, trace sinks and host peripherals) to verify callback ordering, error paths and event counts. Never substitute the DUT for correctness tests.
- **TEST-003 P1:** Run actual Icarus, Verilator and Yosys integrations in isolated Testcontainers environments with pinned tool images/digests. Compare generated output against independent reference models and keep real container artifacts.
- **TEST-004 P0:** Separate fast deterministic unit tests from Docker-dependent integration tests and long synthesis benchmarks; ensure CI requires and reports the correct tier, never silently skipping required evidence.
- **TEST-005 P1:** Publish per-commit trace checksums, RTL hash, simulator version, seed, first failing cycle and machine-readable test results.
- **TEST-006 P1:** Add mutation/property-based regression for widths, signedness, FIFO backpressure, collision behavior, reset and parameter configurations, with reproducible minimized counterexamples.
- **TEST-007 P1:** Add compiler IR-to-RTL equivalence testing with explicit assumptions and coverage; require independent tools where appropriate.

## 17. Competitive capability coverage and change management

- **PARITY-001 P0:** Maintain [feature parity matrix](FEATURE_PARITY.md), with stable PAR identifiers, source evidence, implementation/test maturity, owner issues and explicit acceptance goals. Review weekly and after significant releases.
- **PARITY-002 P1:** Close each parity gap with runnable examples and automated verification, not document-only equivalence claims. Focus on debugger, browser, gate simulation, visual diagrams, four-state semantics and system examples.
- **PARITY-003 P1:** Store reviewed upstream source commit and SharpHDL source commit, substantive changes and impacts in a dated review log. Keep design, requirements and tracked issues synchronized.

## 18. Visualization priorities (revised 2026-10-14)

- **VIZ-001 P0:** Produce stable machine-readable pipeline schedule graph (nodes, edges, stage, latency, initiation interval, resource estimates, source provenance) from each supported scheduler; guarantee equivalence to emitted RTL behavior through tests.
- **VIZ-002 P0:** Visualize stalls, FIFO occupancy, token advancement, bubbles and valid/ready state on the dataflow graph with clock-step synchronization.
- **VIZ-003 P1:** Display module hierarchy, interfaces, clock domains, memory blocks, AXI channels and source links without exploding designs into individual gate-level cells.
- **VIZ-004 P1:** Source-linked synthesis/performance explorer shall distinguish pre-route estimates from post-placement utilization and timing reports; identify expensive operations and implementation choices.
- **VIZ-005 P2:** Support optional scoped gate-cone schematic and SVG/DOT export for debugging/education. Do not require or prioritize a full-design interactive gate editor; preserve gate-level IR/equivalence requirements.
