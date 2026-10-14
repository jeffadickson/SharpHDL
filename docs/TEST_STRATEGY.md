# SharpHDL automated testing strategy

## Three layers

1. **xUnit:** fast deterministic unit tests and bounded randomized/property-like tests for bit widths, combinational operations, reset, clock edges, FIFO backpressure, memories, scheduling and hardware-to-RTL equivalence harness construction. The older self-checking test runner remains the broad regression suite during migration.
2. **NSubstitute:** mock only external side-effect collaborators at architectural seams: debugger event observers, trace sinks, external simulator/process runners, clock-independent host devices and exporters. Never mock the DUT itself, RTL arithmetic, memory collision behavior, or synthesis tool results. The first slice introduces `HardwareDebugSession` and its `IHardwareDebugObserver`; tests verify exactly-once post-edge notification and observable hardware state.
3. **Testcontainers:** integration tests running real external compilers/simulators and services in pinned, isolated containers. Use for reference RTL tools (Icarus, Verilator, Yosys), eventual web debug-service transport, and storage only if storage is introduced. Do not insert an unnecessary database into the core synthesis or simulation dependency graph.

## Running the first slice

```sh
dotnet test tests/SharpHDL.UnitTests/SharpHDL.UnitTests.csproj -c Release
dotnet run --project tests/Tests.csproj -c Release
```

Tests for external synthesis simulators shall live in a separate integration-test project (not the fast default test suite), require Docker, and run against image digests pinned in CI. The integration suite must never silently pass or skip when Docker or an external simulator is required by its CI job. Prefer self-contained oracle tests as the gating suite.

Current limitation: this initial slice adds xUnit and NSubstitute executable tests but does not yet introduce or exercise a Docker image. Tracking issue covers Testcontainers implementation and CI gating. No passing test run is claimed until build execution is recorded.

## Prioritized integration fixtures

- Generated Verilog + Icarus: compile, run seeded trace, compare reference model output.
- Yosys: synthesize and check RAM inference and register/DSP accounting.
- Verilator: medium/large pipeline runs and differential trace validation.
- Browser debug service: run the same client protocol tests locally and with a containerized host, verifying shutdown and reconnect.

Capture image digest, SDK version, tool version, design hash, seed, expected trace hash and output artifacts in CI.
