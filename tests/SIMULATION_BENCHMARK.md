# Simulation benchmark (initial baseline)

Run from the repository root with .NET 8 installed:

```sh
dotnet run --project tests/Tests.csproj -c Release -- --simulation-benchmark
```

The command writes `generated/simulation-benchmark.json` and exits with a failure if any cycle disagrees with its independent unsigned 16-bit accumulator oracle.

- Fixed input generator: 32-bit LCG, seed 104729, enabled by the top input bit.
- Fixed warm-up: 10,000 cycles. Measurement: 5 runs × 100,000 cycles.
- Each run creates a fresh simulator, asserts reset, applies inputs and verifies the accumulator **at every clock**.
- Reported throughput is measured cycles / median repetition wall time.
- JSON records .NET runtime, OS, process architecture, RTL SHA-256, correctness result and individual run durations.

## Interpretation and caveats

This exercises the current *graph simulator only*. It is not a comparison to compiled .NET, gate-level simulation, RHDL, Icarus or Verilator. Compilation, initialization and reset are included in each timed repetition; the numbers are therefore workload-level and intentionally conservative. JIT tiering, CPU throttling and GC affect results. Measurements are not reproducible performance promises.

Next: add other workloads (combinational arithmetic, FSM, RAM, ready/valid pipeline), canonical transaction traces and cross-backend parity. Benchmark fixtures must remain identical across backends; never compare unlike workloads. Preserve the JSON schema or explicitly version it if changed.

The project was updated via the GitHub connector. The runtime's direct GitHub network access was unavailable when this file was authored, so no execution results are claimed here.
