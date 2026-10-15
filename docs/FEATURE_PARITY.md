# Feature parity and engineering comparison: SharpHDL / RHDL

**Updated:** October 14, 2026, Pacific time, after executed simulator CI.
**Current execution evidence:** [SIMULATOR_VERIFICATION.md](SIMULATOR_VERIFICATION.md).
**Current compiler design:** [COMPILED_SIMULATOR_DESIGN.md](COMPILED_SIMULATOR_DESIGN.md).
**Preserved prior matrix and complete implementation history:** [pre-verification record](history/FEATURE_PARITY_before_verified_simulator_0082cc0.md).

The RHDL column retains the earlier documented baseline from `skryl/rhdl`: README blob `126d57957df17643f9bb921d2e0d20e23da7cf3c` and web guide blob `1171ef92deeaaeebed958994485319a269e563f9`. RHDL was not executed or re-audited during this simulator qualification. Documented feature presence is not a claim of identical semantics, maturity or measured performance.

A verified SharpHDL subset is not necessarily complete parity for the row. No numerical performance comparison with RHDL is claimed. The matched execution comparison is between SharpHDL's interpreter, compiled .NET backend and Verilator on the same exported TinyRisc CPU.

| ID | Capability | RHDL documented baseline | SharpHDL current evidence / remaining gap |
|---|---|---|---|
| PAR-001 | Hardware DSL | Combinational/sequential [DSL](https://github.com/skryl/rhdl/blob/master/docs/dsl.md) | Structural Module/Expr and scoped CIL authoring present. Attributed frontend and unified production IR remain incomplete. |
| PAR-002 | Synthesizable Verilog | [Export](https://github.com/skryl/rhdl/blob/master/docs/export.md) | Executed Icarus/Verilator acceptance fixtures and TinyRisc UART; Yosys structural check passed. Broader target qualification remains. |
| PAR-003 | Memories, FSM, hierarchy, components | [Components](https://github.com/skryl/rhdl/blob/master/docs/components.md) | RAM/FSM/library present; verified single-port RAM and FSM subset. General hierarchy, bundles and multiport policy coverage remain partial. |
| PAR-004 | Fast native simulation | Rust interpreter/JIT/AOT [simulation](https://github.com/skryl/rhdl/blob/master/docs/simulation.md) | **Verified scoped two-state .NET DAG JIT**, register/RAM/stream/CPU tests and measured throughput. Multi-clock/four-state behavior and larger design scaling remain open; no RHDL speed comparison. |
| PAR-005 | Gate-level lowering and simulation | [Gate backend](https://github.com/skryl/rhdl/blob/master/docs/gate_level_backend.md) | General internal gate-level backend/equivalence remains a gap. External RTL tools are not a substitute for this capability. |
| PAR-006 | IR/native/external backend integration | [Performance/backend guide](https://github.com/skryl/rhdl/blob/master/docs/performance.md) | Icarus and matched Verilator execution verified for selected workloads. Serialized canonical IR and generic external adapters remain incomplete. |
| PAR-007 | In-browser WASM simulation | [Web guide](https://github.com/skryl/rhdl/blob/master/docs/web_simulator.md) | Existing TinyRisc/learner demos; live localhost host is not browser-native WASM. General user-loadable hardware remains partial. |
| PAR-008 | Waveforms and VCD | [Web guide](https://github.com/skryl/rhdl/blob/master/docs/web_simulator.md) | Basic two-state VCD/watch trace tests pass in unit suite. Unified large-trace viewer, streaming VCD/FST and navigation remain partial. |
| PAR-009 | Watches, breakpoints, stepping | [Debugging](https://github.com/skryl/rhdl/blob/master/docs/debugging.md) | Unit-tested hardware value/change conditions and passed live step/back API. Handshake helper samples post-edge levels, not a pre-edge transfer event. VS Code desktop acceptance/source breakpoints remain incomplete. |
| PAR-010 | Memory editor and snapshots | [Web guide](https://github.com/skryl/rhdl/blob/master/docs/web_simulator.md) | Reference simulator/debug-session checkpoints and live replay tests pass. Portable files, generic memory editor and compiled-backend checkpoint integration remain open. |
| PAR-011 | Source-to-HDL explorer | [Web guide](https://github.com/skryl/rhdl/blob/master/docs/web_simulator.md) | Annotated RTL and Portable PDB mapping present; complete source/IR/waveform cross-probing remains partial. |
| PAR-012 | Hierarchical diagrams | [Diagrams](https://github.com/skryl/rhdl/blob/master/docs/diagrams.md) | General hierarchy diagrams remain a gap. Pipeline/dataflow inspection has priority over full-chip interactive schematics. |
| PAR-013 | CPU/system demonstrations | 8-bit, 6502, Apple II, Game Boy, RISC-V in baseline [README](https://github.com/skryl/rhdl/blob/master/README.md) | TinyRisc16 ROM/RAM/MMIO/serial Hello World now has executed oracle, JIT, RTL and UART checks. Richer general system runner/snapshots and broader CPU library remain partial. |
| PAR-014 | Desktop packaging | [Web guide](https://github.com/skryl/rhdl/blob/master/docs/web_simulator.md) | VS Code prototype and browser tooling; no complete offline desktop package acceptance. |
| PAR-015 | Unified CLI / terminal tooling | [CLI](https://github.com/skryl/rhdl/blob/master/docs/cli.md) | Executable examples, Make/just and qualified benchmark runners; general compile/simulate/debug/export CLI remains partial. |
| PAR-016 | Four-state 0/1/X/Z values | [Simulation](https://github.com/skryl/rhdl/blob/master/docs/simulation.md) | Gap. Both SharpHDL graph engines remain two-state; no signal-resolution or X/Z mode claimed. |
| PAR-017 | Parametric bundles/vectors | [Feature analysis](https://github.com/skryl/rhdl/blob/master/docs/chisel_feature_gap_analysis.md) | Typed streams/packets present; general nested/directional aggregate and indexed-vector frontend remains partial. |
| PAR-018 | Backend and FPGA benchmarks | [Performance](https://github.com/skryl/rhdl/blob/master/docs/performance.md) | **Executed repeated three-engine TinyRisc clock benchmark**, hashes, raw samples and trace/state qualification. Existing FPGA records are separate. More workloads, independent machines and direct RHDL comparison remain open. |
| PAR-019 | FPGA targeting / memory mapping | Equivalent vendor-mapping depth unverified | Targeted inference policies present. This simulator qualification does not establish new vendor DSP/BRAM mapping, post-route timing or board execution. |
| PAR-020 | High-level pipelining / dataflow | Comparable automatic scheduling unverified | Scoped II=1 scheduler and structural/CIL pipeline tests present; multi-cycle resource-aware scheduling and broader feedback cases remain open. |
| PAR-021 | Scientific / fixed-point library | Specific equivalence unverified | Scoped arithmetic/CORDIC/FIR/streams present. This run is not a complete numerical-kernel qualification. |
| PAR-022 | Formal contracts / witnesses | Unverified in baseline | Assertions/faults present; solver-backed proofs and generic counterexample playback remain incomplete. |
| PAR-023 | .NET language interoperability | Ruby user DSL; not an equivalent .NET feature | C#/F#/VB CIL examples exist. This CI record does not add new F#/VB cross-platform acceptance. |
| PAR-024 | Unit and real-tool integration | RSpec/actions documented | **60 passing xUnit cases on Linux and Windows**, real Icarus/Verilator CPU and conformance runs, Yosys structural check, retained artifacts. Digest-locked toolchains, broader Testcontainers and release packaging remain partial. |

## Priorities after the verified simulator baseline

Preserve the passing correctness gates while expanding representative workloads and reducing state/RAM boundary overhead. Complete issue #24's actual Core/HLS/Simulation extraction without duplicating hardware semantics. Continue hardware-aware debugging and full-system usability; source breakpoints are not the leading priority. Four-state simulation and general gate-level equivalence remain separate workstreams rather than being implied by the two-state JIT milestone.

## Review policy

Keep PAR identifiers stable. Update execution status from actual logs and artifacts, with the tested commit, workload, toolchain and remaining limitations. Retain the distinction between historical unverified implementation entries and current executed evidence. Performance must use matched work, visibility and timing scope; no cross-project superiority claim from unlike CPU examples.

## Review log

- Earlier October 14 implementation and planning entries are preserved verbatim in the linked history file.
- October 14 executed verification: corrected the PDB build failure and typed-channel test, measured the first fused-tree slowdown, replaced repeated tree expansion with typed shared-DAG scheduling, passed the expanded 60-case suite, qualified independent-oracle/RTL fixtures, and measured all three CPU engines. Exact sources and results are in the linked verification report.
