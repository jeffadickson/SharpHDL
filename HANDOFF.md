# SharpHDL handoff

## Current status — 2026-10-26 continuation

Baseline: clean main at 4e6d34c. Local implementation commits 04a7c7b and
7ca5ed3 restore the standalone regression, make provenance tests work with isolated
Linux outputs, harden scheduled expression ownership/freeze/recovery and add a
non-FIR scheduled RAM table with independent protocol/array verification.
Nothing was pushed. Five substantive iterations were completed, not twenty.

The dependency direction remains Core <- HLS, Simulation and RTL. The scheduled
FIR still uses one shared multiplier and explicit one-cycle synchronous RAM reads.
The table uses the same ScheduledMachineBuilder and Core-native simulation/RTL,
with conditional 16-bit updates, four-edge no-stall II and held responses. Reset
aborts pending responses while retaining committed RAM updates. See
[architecture](docs/MODULAR_ARCHITECTURE.md) for the precise contract.

## Fresh verification

- Windows SDK 10.0.403 and isolated Linux SDK 10.0.402: 148/148 xUnit pass.
- Standalone regression executable passes, including its full existing suites.
- Icarus: existing FIR 2/4/8/16 and new table depth 2/16/256 replays pass.
- Yosys table checks retain one memory and one adder; Verilator lint passes.
- Coverlet: 56.71% line/40.22% branch across recorded scope; new table 100% both.
- Stryker scheduled-builder scope: 66.28%, 50 surviving/37 uncovered mutants;
  9 timeouts are not confirmed assertion kills.
- StrictDoc, project layers and workflow YAML parse pass. Hosted CI not run.

[Development log](docs/DEVELOPMENT_PROGRESS_2026-10-26.md) contains commands,
failures/fixes, iteration acceptance, tool versions, raw artifact locations and
limitations. [Evidence JSON](docs/verification/continuation-2026-10-26.json)
records source hashes, metrics and project/workflow inventory. Requirements are
in [StrictDoc](docs/SCHEDULED_VALIDATION.sdoc), with existing requirement IDs kept.

## Remaining boundaries and next work

Arithmetic resources still reserve capacity without automatic typed binding or
operand/result arbitration. Add that next, including capacity >1 and dependency
validation, then connect supported stateful CIL control flow. Broaden mutation,
coverage-guided fuzzing, reset-phase and Verilator parity; execute hosted CI when
authorized to push. SCHED-011 remains partial. Multiport RAM, multiclock Core,
physical FPGA mapping/timing and formal equivalence are not established.

Existing compiler/analyzer warnings and stale older documentation remain. No
performance optimization or controlled simulator speedup measurement was made.
Generated replay outputs and raw verification artifacts are local/ignored; the
standalone regression regenerates tracked artifacts, so preserve them or run it
in an isolated output directory. Never discard unknown local work.
