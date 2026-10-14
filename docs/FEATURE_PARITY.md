# Feature parity and engineering comparison: SharpHDL / RHDL

**Baseline captured:** 2026-10-14. **Next review:** weekly and whenever a synthesis/debug milestone closes.
**Reference repository:** https://github.com/skryl/rhdl (master, README blob `126d57957df17643f9bb921d2e0d20e23da7cf3c`; browser guide blob `1171ef92deeaaeebed958994485319a269e563f9`).
**SharpHDL basis:** https://github.com/jeffadickson/SharpHDL (main; see links in each row).

This is a **feature-coverage matrix**, not a claim of equivalent fidelity, maturity, timing or performance. "Present" means documented implementation exists, not independently tested here. "Partial" means a narrower implementation; "Gap" means no equivalent established. "Unverified" is used when public docs do not establish a capability.

| ID | Capability | RHDL evidence/status | SharpHDL evidence/status | SharpHDL gap / acceptance target |
|---|---|---|---|---|
| PAR-001 | Hardware DSL with combinational and sequential modules | Present [DSL](https://github.com/skryl/rhdl/blob/master/docs/dsl.md) | Present structural API [Hardware.cs](../src/SharpHDL/Hardware.cs) and scoped CIL [CONSTRUCTS](../CONSTRUCTS_AND_REQUIREMENTS.md) | Attributed typed module frontend; IR/RTL parity |
| PAR-002 | Synthesizable Verilog export | Present [export](https://github.com/skryl/rhdl/blob/master/docs/export.md) | Present [README](../README.md) | Regression breadth and netlist/source mapping |
| PAR-003 | Memory, FSM, hierarchy, reusable components | Present [components](https://github.com/skryl/rhdl/blob/master/docs/components.md) | Partial: RAM/FSM/library, [MEMORY](../MEMORY.md) | Bundles, parameterized hierarchical reuse and multiport RAM |
| PAR-004 | Native/fast simulation backends | Rust IR interpreter/JIT/AOT, external engines [simulation](https://github.com/skryl/rhdl/blob/master/docs/simulation.md) | Partial: graph interpreter and **combinational only** compiled delegates [DESIGN](DESIGN.md) | Compiled sequential/memory/stream engine + measured parity and performance |
| PAR-005 | Gate-level lowering and gate simulation | Present generic primitive lowering [gate backend](https://github.com/skryl/rhdl/blob/master/docs/gate_level_backend.md) | Gap: general gate-level IR/execution not established | Gate netlist, structural verification against RTL |
| PAR-006 | IR/native backend simulations and CIRCT validation | Present external support [performance](https://github.com/skryl/rhdl/blob/master/docs/performance.md) | Partial: generated Verilog/Icarus, external synthesis; no common fast IR | Stable serialized IR and third-party simulator adapters |
| PAR-007 | In-browser WASM simulation | Present [web guide](https://github.com/skryl/rhdl/blob/master/docs/web_simulator.md) and [demo](https://skryl.github.io/rhdl) | Partial: existing TinyRisc and learner browser demos [README](../README.md) | General loadable user-designed circuits, shared simulator |
| PAR-008 | Waveform viewer and VCD streaming/export | Present [web guide](https://github.com/skryl/rhdl/blob/master/docs/web_simulator.md) | Partial: existing demos and new debug watch samples [DebugSession.cs](../src/SharpHDL/DebugSession.cs) | Unified waveform pane; proper VCD/FST and large-trace cursor |
| PAR-009 | Watches/value breakpoints/step and reset | Present [debugging](https://github.com/skryl/rhdl/blob/master/docs/debugging.md) | Partial: `HardwareDebugSession` step/reset/value/change/retained watch trace | DAP, multi-clock, source mapping, pause/status protocol |
| PAR-010 | Memory editor, dumps and snapshots | Present [web guide](https://github.com/skryl/rhdl/blob/master/docs/web_simulator.md) | Gap: simulator RAM exists but general debug editor/checkpoint absent | Inspect/edit memory, portable snapshots and deterministic restore |
| PAR-011 | Source-to-HDL component explorer | Present [web guide](https://github.com/skryl/rhdl/blob/master/docs/web_simulator.md) | Partial: source and annotated generated RTL exist | Cross-probe source↔IR↔waveforms↔Verilog |
| PAR-012 | Gate/RTL hierarchy diagrams (SVG/PNG/DOT) | Present [diagrams](https://github.com/skryl/rhdl/blob/master/docs/diagrams.md) | Gap: equivalent integrated hierarchical diagram pipeline not established | Export and interactive zoom/click-through diagrams |
| PAR-013 | CPU and system demonstrations | 8-bit, 6502, Apple II, Game Boy, RISC-V [README](https://github.com/skryl/rhdl/blob/master/README.md) | Partial: TinyRisc16 and learning demos [TinyRisc](../examples/TinyRisc/README.md) | Memory/peripheral system, binaries, architectural oracle and richer runner |
| PAR-014 | Desktop app packaging | Present [web guide](https://github.com/skryl/rhdl/blob/master/docs/web_simulator.md) | Gap | Package offline desktop client from shared browser workbench |
| PAR-015 | Native CLI, terminal simulator tools | Present [CLI](https://github.com/skryl/rhdl/blob/master/docs/cli.md) | Partial: executable demos and shell scripts; no consistent CLI | Compile/simulate/inspect/trace/export/debug subcommands |
| PAR-016 | Four-state values (0/1/X/Z) | Present in signal value docs [simulation](https://github.com/skryl/rhdl/blob/master/docs/simulation.md) | Gap: 2-state graph core | Explicit 4-state verification mode and boundary rules |
| PAR-017 | Parametric bundles and vectors | Documented [feature gap review](https://github.com/skryl/rhdl/blob/master/docs/chisel_feature_gap_analysis.md) | Partial: typed streams/packets but not complete general bundle/Vec frontend | Nested structs, port directions, dynamic index, reverse metadata |
| PAR-018 | Timing/resource and backend benchmarking | Present backend sample measurements [performance](https://github.com/skryl/rhdl/blob/master/docs/performance.md) | Partial: mapped FPGA benchmarks and newly committed but unverified compiled-cone benchmark | Apples-to-apples, hashed workload, matched visibility and post-route timing |
| PAR-019 | FPGA targeting and memory mapping | RHDL has Verilog export; equivalent vendor mapping depth unverified | Present targeted inference [MEMORY](../MEMORY.md) | Expand vendor DSP/dual-port/byte-write and verify mapping |
| PAR-020 | High-level pipelining/dataflow | RHDL specific automatic scheduling parity unverified | Present scoped II=1 [PIPELINE_SCHEDULER](../PIPELINE_SCHEDULER.md) | Multi-cycle/DSP, RAM and bounded loop scheduling with resource sharing |
| PAR-021 | Scientific/typed fixed-point/dataflow library | Specific parity unverified | Present scoped numeric, CORDIC, FIR and streams [README](../README.md) | More numerical oracle tests, fixed point and FFT/matrix kernels |
| PAR-022 | Formal contracts and counterexample playback | Unverified | Partial assertion/faults, broader formal engine planned [requirements](PRODUCT_REQUIREMENTS.md) | Solver-backed proof and trace import |
| PAR-023 | .NET language interoperability | Ruby-specific host integration, not equivalent feature | Present C#/F#/VB CIL examples [README](../README.md) | Shared source mapping across language frontends |
| PAR-024 | Modern unit and toolchain integration testing | RSpec/build actions [README](https://github.com/skryl/rhdl/blob/master/README.md) | Partial: xUnit/NSubstitute and initial non-hermetic Icarus Testcontainers fixture [TEST_STRATEGY](TEST_STRATEGY.md) | Passing builds, digest-pinned images, Verilator/Yosys, equivalence and CI artifacts |

## Coverage policy and objective

**Minimum parity objective:** PAR-001 through PAR-018 must reach a *verified, user-usable* SharpHDL implementation; PAR-019–024 are differentiators that should outperform in measured FPGA engineering workflows. Different internal architecture is acceptable where observable capability is equivalent.

No row can be marked verified based solely on README text or a committed but unexecuted test. To mark parity achieved, link a runnable example, an automated test with result, and a documented limitation. For performance, run the *same* design and input trace on both tools and publish exact hashes and machine metadata.

## Highest-priority gaps

1. **PAR-004/006/016:** Canonical IR, atomic compiled edge simulation and strict two-/four-state modes.
2. **PAR-007–012/014–015:** One debugger service powering VS Code, offline browser, schematics, memory panels, waveforms and CLI.
3. **PAR-005:** Generic gate-level lowering, interpreter and structural equivalence.
4. **PAR-013:** Larger executable system with ROM, peripherals, video/keyboard or serial I/O and save/restore.
5. **PAR-017:** Better typed language ergonomics and packed/array interfaces.

## Ongoing review procedure

Review both repositories **weekly** and after SharpHDL milestone merges. Compare latest source/docs for changes to every PAR entry; record the reference upstream commit/blob, current SharpHDL commit, test evidence, changed maturity status and new gaps. Edit this file, [DESIGN.md](DESIGN.md), and [PRODUCT_REQUIREMENTS.md](PRODUCT_REQUIREMENTS.md) together when decisions or scope change. Add actionable GitHub issues, avoid duplicates, and leave claims unverified if CI/test data is absent.

Use `git log` and upstream `git diff` when accessible. Never auto-merge upstream code or copy licensed source without a separate review. Update links, keep parity IDs stable, append an entry to the changelog below.

### Review log
- **2026-10-14:** Baseline matrix created from public upstream README, simulation, debugger, gate backend, performance and web documentation; SharpHDL main source/docs and existing tests. Passing CI and runtime parity not established in this session.
