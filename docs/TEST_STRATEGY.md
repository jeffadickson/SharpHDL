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

## Follow-up item 2 — semantic verification campaigns

ScheduledSemanticTests adds independent checks for priority/hold/simultaneous
commit and malformed boundaries identified by the previous mutation report. The
new SharpFuzz/AFL++ harness fuzzes table traffic against host array arithmetic;
`bash tools/run-scheduled-fuzz.sh 60` retains native Linux publish/instrumentation,
coverage queue and crashes with seed 261026. Input length is bounded to 256 bytes;
this is a bounded campaign, not a universal correctness proof. No exceptions are
silently ignored. SharpFuzz 2.2.0 follows its upstream out-of-process API.

The table example is now a separate .NET 10 example assembly so Stryker can mutate
its implementation independently of the xUnit/oracle assembly. Existing namespace
and Build API are preserved. Run saved stryker-config.json for compiler sources
and stryker-table.json for the table. Mutants reversing order across mutually
exclusive states, Single-to-SingleOrDefault after validation, and removing a DAG
visited-set optimization can be behaviorally equivalent; do not weaken tests or
invent kills to obtain 100%. Final results/dispositions follow execution at the
end of the requested item sequence.

## Follow-up item 4 — broader parity and hosted verification

Compact file-driven testbenches now replay table depths 2/16/256 and actual CIL
RAM-loop output in both Icarus and Verilator. CIL generation first compares native
array arithmetic and legacy/Core outputs edge by edge, including partial writes
on a bounds fault. A method/IL map accompanies generated hardware. Directed unit
tests reset at every table phase and test independent-kernel state isolation;
shared multi-kernel resource arbitration is still a separate scheduler feature.
The Windows/Linux xUnit job collects coverage; scheduled-core-suite is a new RTL
gate. Hosted workflow outcomes are pending the final authorized push.

CI policy update: the user explicitly disabled GitHub Actions during this task.
Repository Actions permissions were changed to enabled=false and read back to
confirm. No running Actions jobs were present. Retained workflow definitions are
dormant; final verification is local and the authorized push must not enable CI.
See root AGENTS.md. This supersedes the earlier hosted-run plan above.
