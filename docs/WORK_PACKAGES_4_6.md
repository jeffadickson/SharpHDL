# Work packages 4–6 continuation

Baseline: clean main at 5179ddf, origin https://github.com/jeffadickson/SharpHDL.git.
CI remains disabled. Commit each package locally after verification, then push.

## WP-04: continuous Z80 differential adapter

The test-only Z80dotNet 1.1.0 dependency runs independently authored CPU semantics;
SharpHDL's hardware executes through the existing Core simulator. Four continuous
programs cover loops, indexed/ED operations, alternate registers, arithmetic
boundaries, RAM writes, DI/EI and extended 16-bit ports. At every retirement the
adapter compares all exposed register pairs, I/R, IFF1/2, IM, HALT, the full 64 KiB
RAM and cumulative ordered RAM writes and port accesses. Three out of four bus
edges stall; accepted writes must occur once. State is seeded only at program
start. Negative controls corrupt RAM or port history and must fail.

Library fit: managed .NET Standard 2.0, usable from .NET 10, synchronous stepping
and injectable memory/ports avoid writing another CPU emulator. The package is
confined to the non-packable unit-test project. NuGet records version 1.1.0 as
published on 2025-04-20. Its sparse release history and behavioral gaps require
qualification rather than an assumption of silicon conformance. Upstream permission
terms are preserved in `licenses/Z80dotNet.txt`; this notice is not mislabeled as
SharpHDL's own MIT license. No CPU reference code is modified or copied into production.

Reference limitations: upstream documents BIT flag 3/5 and block-I/O H/C/PV gaps;
the latter is characterized by a dedicated test. Its interrupt-source interface
uses ValueOnDataBus only for IM2; do not use it to qualify arbitrary IM0 injected
opcodes. This campaign has no external interrupt injection. Existing independent
interrupt scenarios and pinned SingleStepTests vectors remain separate oracles.
WZ/Q behavior, pin timing, exhaustive ISA and CP/M qualification remain unclaimed.

Primary sources: [reference API/limitations](https://github.com/Konamiman/Z80dotNet),
[interrupt source](https://github.com/Konamiman/Z80dotNet/blob/master/Main/Dependencies%20Interfaces/IZ80InterruptSource.cs).

Windows SDK 10.0.403: seven focused tests pass, zero failed/skipped, 11 seconds.
Log: `build/verification/wp04-qualified.log`; TRX and Coverlet XML:
`build/verification/wp04-qualified`. With IncludeTestAssembly=true, the adapter
class measures 100% line / 97.05% branch; its TraceMemory transport measures
80% line / 75% branch (unused bulk-copy interface paths remain unmeasured).
These are adapter execution metrics, not ISA or HDL toggle coverage.
The two deliberate side-effect corruptions are detected. Stryker.NET 5.0.0
kills all six selected ALU conditional mutations using only the four positive
continuous programs, with no survivors/timeouts. This is a six-mutant scope,
not a whole-CPU score; other compile-invalid/filtered mutations are excluded.
Configuration: `tests/SharpHDL.UnitTests/stryker-z80-program.json`; raw report:
`build/verification/wp04-mutation/reports/mutation-report.json`.

## WP-05: dependency-aware static resource scheduling

`ScheduledOperationPlan.Create` accepts immutable one-edge operation declarations,
explicit dependencies and per-resource capacities. QuikGraph-backed component
analysis rejects all cycles, including disconnected ones. Allocation uses ordinal
operation-name priority; resource/dependency keys remain ordinal even if the caller
uses case-insensitive dictionaries. `ValidateAssignments` rejects incomplete,
out-of-range, dependency-violating or conflicting proposed schedules. Work is
bounded at 4,095 operations and 65,536 dependency/root edges. A no-progress guard
also rejects an inconsistent external capacity provider rather than spinning.

`EmitStates` emits real builder states and parks at completion. The existing lowerer
independently validates actual physical accesses; callbacks cannot hide a same-port
conflict behind logical resource aliases. The callback is domain integration glue,
not an alternative hardware model. Declare registers/memories before emission and
state predicates afterward; callback failures propagate and leave that builder
unsuitable for reuse.

The reusable `examples/ScheduledPlannedBanks.cs` example reads two banks in parallel,
serializes two operations through one multiplier, writes and rereads the result.
It computes `(left*right + left*3) mod 256` in six edges, then parks. The replay suite
exports its schedule JSON and tests reset at every phase with independent numeric
expectations. Memory alias/order dependencies are caller-declared; automatic alias
extraction, multi-cycle operators, dynamic arbitration and optimal schedules are
not implemented by this profile.

Library choice: reuse the already pinned QuikGraph 2.5.0 component algorithm;
keep capacity/edge policy in SharpHDL and use BCL ordinal dictionaries/read-only
snapshots. A general task scheduler cannot substitute for hardware edge semantics.
No new production dependency is added in this package. NSubstitute is used only to
represent a malformed external configuration provider; RAM, arithmetic and DUT
behavior remain real.

The combined directed gate passes 23/23 (seven planner and sixteen AXI tests),
zero failed/skipped, with 100 fixed-seed FsCheck analytic capacity cases.
Planner coverage is 100% line / 96.15% branch in that scope; defensive branches
remain unmeasured. Log: `build/verification/wp05-06-directed.log`.
The initial example failed because its register and output shared a name; the
register was renamed and rerun successfully. An exploratory mutation run was
stopped after a prolonged phase and logged a Stryker comment-parser regex timeout;
it has no final score. Following source review, explicit progress/edge bounds and
an inconsistent-provider regression were added. The bounded rerun's results follow
when complete; no causal claim about all host delays is inferred from the log.

The completed bounded Stryker run reports 109 killed, 25 survived, two uncovered,
zero timeouts: 80.15% of 136 selected testable/uncovered mutants. Twenty-four
compile-invalid and ten ignored selected-file variants are excluded. Review found
gaps for descending priority, mixed valid/invalid declarations and exactly 65,536
edges; explicit tests were added and the affected ranges are rechecked separately.
The initial whole-file result is retained, not relabeled as a newer whole-file score.
The focused rerun kills 27/29 selected variants (93.10%), with two surviving
message-string changes and no timeouts. It catches descending priority, mixed
null/valid operations, mixed valid/invalid capacities and the exact-edge-budget
mutation. Configuration: `stryker-operation-plan-review.json`; raw report:
`build/verification/wp05-mutation-review/reports/mutation-report.json`.

Icarus 12.0 and Verilator 5.020 each pass all nine scheduled designs. The new
planned-bank example passes 84 edges across seven reset phases; Yosys 0.33 generic
checks pass it and both shift designs. Artifact root: `/tmp/sharphdl-rtl-owlDwD`;
log: `build/verification/wp05-rtl.log`.

SharpFuzz 2.2.0/AFL++ 4.09c runs for 60 seconds with seed 261026: 15,783 executions,
138 corpus entries, 2.43% host bitmap occupancy, zero saved crashes/hangs. The
existing graph/shift/table harness now checks planner cycle rejection, strict
dependency edges and bank capacity against an independent closure classification.
Raw artifacts: `/tmp/sharphdl-fuzz-u1MsE9`, log `build/verification/wp05-fuzz.log`.
These are bounded host campaigns, not exhaustive scheduling proofs or HDL toggles.

## WP-06: protocol checkers and campaigns

WP-06 reuses FsCheck's current xUnit integration and shrinking for protocol
campaigns. Hedgehog was also evaluated: integrated generator shrinking is useful,
but adding a second framework is not necessary for these existing C# xUnit
properties. Preserve replay seeds and negative controls; no mocks of DUT behavior.
See [FsCheck](https://fscheck.github.io/FsCheck/) and
[Hedgehog](https://github.com/hedgehogqa/fsharp-hedgehog).
