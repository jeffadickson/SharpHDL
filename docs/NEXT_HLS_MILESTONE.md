# SharpHDL next implementation milestone: stateful Core HLS

## Goal
Extend the existing CIL → Core IR → elastic RTL flow beyond stateless single-input expression graphs, without bypassing Core IR or regressing the legacy RTL backend.

## Architectural invariants
1. Core IR is the single semantic representation for synthesis and reference simulation.
2. The HLS scheduler owns latency, initiation interval, dependencies and ready/valid stalls; RTL emitters must not independently invent scheduling.
3. Existing `Module.Verilog()` remains compatible while native `SharpHDL.RTL` is qualified against it.
4. Every generated sequential component has explicit reset semantics and retains data and validity under backpressure.
5. C# public APIs and generated source receive XML documentation comments; emitted RTL documents state and handshake behavior.

## Incremental acceptance gates
### Gate A: synchronous memory
- Represent typed, width-constrained memory read/write operations and their address/data dependencies in Core IR.
- Define read-during-write semantics, one-cycle read latency, reset policy and address bounds.
- Lower a single-port synchronous RAM kernel to an elastic pipeline with backpressure; do not silently turn a synchronous read into combinational logic.
- Test directed RAW/WAR/WAW hazards, simultaneous stall/reset, randomized input/output stalls and memory contents against a cycle-independent software reference.
- Verify native and compatibility RTL using Verilator; report Yosys memory inference separately from FPGA primitive mapping.

### Gate B: multiplier resources
- Integrate explicit latency/II/resource constraints with the existing multiplier implementations.
- Model iterative multiplier as a non-II=1 resource; schedule arbitration and backpressure correctly.
- Provide a stallable registered DSP wrapper with transaction validity preserved across reset/stalls.
- Keep target Fmax/DSP/LUT claims conditional on actual device implementation evidence.

### Gate C: simulator parity
- Run identical timestamped stimuli against Core reference, compiled .NET simulator and both RTL backends.
- Compare accepted transaction order and payloads, reset epochs and memory state.
- Add reproducible seeds and minimized failing traces to CI artifacts.

### Gate D: usability and CI
- Provide a runnable C# streaming FIR or packet-processing example using RAM and multiplication.
- Generate source provenance and XML API documentation as part of CI.
- Require Linux .NET tests and Verilator RTL replay before declaring the milestone complete; Windows/macOS remain separately qualified.

## Non-goals
- Automatic timing closure or placement without vendor tools.
- Claiming a complete C# language frontend.
- Replacing the existing simulator or legacy RTL emitter before differential parity.

## Implementation order
Start with the memory semantic model and tests (Gate A), then its Core IR lowering, then scheduling and RTL; proceed to Gate B only after both RTL paths pass the same randomized memory tests.

## Library-fit follow-up tasks (2026-10-27 UTC)

These are evaluation tasks linked to [the description/rationale review](LIBRARY_FIT_2026-10-27.md), not authorization to add every dependency. GitHub Actions remains disabled.

- **Automatic resource scheduling:** evaluate QuikGraph for dependency/SCC/topological operations and Z3 for capacity/dependency constraints. Require deterministic tie-breaking, preserved IL diagnostics and independently replayed schedules. The current explicit-state allocator keeps BCL collections.
- **Offline CIL input/provenance:** compare System.Reflection.Metadata/PEReader with Mono.Cecil on the same assemblies, Portable PDBs and malformed inputs. Prefer the existing BCL facility unless a higher-level instruction model materially reduces risk; preserve the production decoder's semantics.
- **Reproducible external-tool qualification:** reuse the existing Testcontainers project with digest-pinned images and recorded HDL tool versions. Keep local fast tests independent of Docker and fail required qualification when a tool is absent.
- **Unified managed HDL runner:** evaluate CliWrap against System.Diagnostics.Process for cancellation, timeouts, bounded output and failure propagation. Current shell orchestration does not need a new runtime dependency.
- **Stable diagnostic/HDL goldens:** trial Verify.Xunit only after normalizing source paths and unstable IDs. Keep direct assertions for simple naming contracts and retain differential simulator checks for behavior.
- **Measured simulator allocation work:** retain BenchmarkDotNet baselines and profile before changing dictionaries/snapshots. Prefer BCL facilities for any measured hot path; pool storage only if immutable snapshot ownership and deterministic behavior can be preserved and tested.
