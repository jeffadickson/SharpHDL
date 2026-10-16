# SharpHDL and RHDL: technical comparison (October 2026)

This comparison distinguishes verified features from roadmap goals. RHDL reference:
https://github.com/samitbasu/rhdl

| Dimension | SharpHDL | RHDL |
|---|---|---|
| Host language | C# / .NET; some F#/VB CIL lowering | Rust subset |
| Core representation | Canonical Core IR, separate HLS and RTL layers | RHIF, compiler transformations |
| RTL-style state | Explicit registers, state machines, synchronous RAM | Functional state-machine kernels |
| Simulation | .NET reference and generated RTL differential tests | High-performance Rust simulation |
| Verilog output | Existing and native Core RTL backends | Verilog generation |
| Algorithmic pipelining | Implemented for stateless elastic single-input arithmetic; limited stateful feedback elsewhere | Not equivalent to unrestricted automatic HLS |
| Memory | Standalone synchronous RAM with documented RTL verification; **not yet automatic elastic HLS RAM scheduling** | State and memory features require case-specific verification |
| Timing closure | Open-source synthesis/mapping experiments, no general target timing guarantee | No general automatic timing-closure guarantee established here |
| Production qualification | Requires more end-to-end tests and FPGA deployment evidence | Project describes prior RustHDL deployed firmware; this does not establish equivalent qualification for every RHDL feature |

## Immediate SharpHDL engineering priorities

1. Run and publish a deterministic end-to-end synchronous RAM oracle / .NET simulator / Verilog comparison using the existing `SynchronousRam` engine.
2. Extend Core HLS with explicit memory transaction scheduling and one-cycle read latency.
3. Verify stalls, collisions, reset retention and exactly-once writes.
4. Benchmark generated RTL against a handwritten baseline and report actual mapped resources.
5. Preserve distinct evidence for algorithmic HLS, RTL description, and FPGA physical implementation.

**Do not claim** that a roadmap HLS feature already exists, or that generic Yosys cell counts equal device LUT/DSP utilization.