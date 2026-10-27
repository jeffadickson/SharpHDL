# SharpHDL handoff — follow-up complete (2026-10-27 UTC)

## Current implementation

Started from f34cad1. Five requested follow-up items have separate local commits:
8852c88 arithmetic resources; 36e06fc semantic/fuzz tooling; 94fe404 stateful CIL;
ee047ab expanded HDL parity; aa9dd53 memory banking/target metadata/baselines.
Final correction b119041 fixes portable replay/fuzz orchestration, preserves old
memory API signatures and adds conformance tests. The end-of-task evidence commit
contains no further production semantic changes.

Core <- HLS, Simulation and RTL remains enforced. Typed combinational resources
now build generic operand arbitration/result routing; FIR uses that binder.
BuildScheduledSequential reuses production CIL decoding/stack/CFG helpers and
lowers a documented runtime-loop/RAM subset directly through ScheduledMachineBuilder.
Explicit banks, exact initialization and Core-native inference hints are supported.

## Fresh verification

- Windows .NET SDK 10.0.403 and isolated Linux 10.0.402: 180/180 xUnit tests pass.
- Full standalone regression passes from an isolated output directory.
- Icarus and Verilator pass table 2/16/256-word, CIL-loop and two banked variants.
  Refactored FIR Icarus 2/4/8/16-tap replays also pass.
- Yosys xc7 mapping/check asserts exactly two RAMB18E1 cells for the banked example.
  Post-map functional simulation is unverified: installed BRAM declaration lacks
  a functional model. No physical timing, placement or board evidence.
- Coverlet: 61.07% line / 47.81% branch across recorded managed scope.
- Compiler mutation: 86.48% (44 survivors, 4 uncovered, 9 timeouts explicitly
  retained); table mutation: 100% (38 killed, no survivors/timeouts).
- SharpFuzz/AFL++: 60 seconds, seed 261026, 6157 executions, 82 corpus entries,
  no saved crashes/hangs. Bounded campaign, not exhaustive qualification.
- BenchmarkDotNet short baseline: construction + 256 edges, 3.350/3.892 ms at
  address widths 1/8; broad intervals, no optimization/speedup claim.
- StrictDoc, layer check and dormant workflow YAML parse pass.

[Full report](docs/FOLLOWUP_2026-10-27.md),
[evidence JSON](docs/verification/followup-2026-10-27.json),
[mutant dispositions](docs/verification/followup-mutant-dispositions.json),
[library tradeoffs](docs/LIBRARY_FIT_2026-10-27.md), and
[library-fit tasks](docs/NEXT_HLS_MILESTONE.md) contain exact scope and reproduction.
Raw logs, reports, generated vectors and benchmark exports remain local/ignored.

## CI and Git policy

The user authorized a push after final tests, then explicitly turned GitHub CI off.
Repository Actions permissions are confirmed enabled=false; no running jobs were
present. Root AGENTS.md preserves this instruction. Do not enable or dispatch CI.
Workflow definitions are dormant. Verify remote HEAD after the authorized push;
never force-push or discard unknown local work.

## Remaining boundaries

Stateful CIL supports static UInt32 returns, full-width scalar/Boolean values,
unchecked integer operations, branches/switch, runtime loops and one uint[] RAM.
Calls, checked arithmetic, division/shifts, packed/narrow ports, exception handlers
and legacy vendor options reject explicitly. InitialMemory now works. IL state
provenance exists; full source-line mapping remains future work.

Automatic optimal scheduling/bank partitioning, iterative arithmetic latency,
shared cross-kernel arbitration, true multiport RAM, multiclock Core, formal
proof and physical FPGA qualification remain open. Independent-machine isolation
is not a multi-kernel resource scheduler. Intel hints have emission tests, not
Quartus mapping. Existing legacy analyzer/documentation limitations remain.
Library candidates are assigned to matching future tasks; no new production
NuGet dependency was added. Preserve existing APIs and independent oracles.
