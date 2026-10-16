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
