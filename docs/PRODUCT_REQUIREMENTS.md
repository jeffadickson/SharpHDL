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
