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
below; no causal claim about all host delays is inferred from the log.

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

`SharpHDL.Verification.AxiStreamConformance` checks declared widths, strict
payload/VALID stability through an accepting edge and cumulative transfer counts.
The Lite checker independently holds all five channels, pairs prior AW/W credits,
requires prior AR credit and rejects active EXOKAY. Reset cancels protocol state
without erasing lifetime counters. Field checking is a strict bounded profile,
including invalid-channel field widths; alignment belongs to the peripheral.
Neither checker promises eventual READY/response or complete standard coverage.

The shared campaign uses real Stream FIFO and Lite register hardware. A BCL queue
checks stream order and every sideband, including null TKEEP beats. An independent
byte-array/BinaryPrimitives model checks strobes, independent AW/W arrival,
read-only/unmapped/unaligned errors, held responses and reset cancellation. A
directed concurrent read/write checks the old-data snapshot. FsCheck shrinks the
command array; the driver preserves legal offered transactions as it shrinks.
No failure counterexample was found in passing campaigns, so no minimized failing
case is claimed. Eleven directed corruptions challenge all stream fields/VALID
and all Lite channels; malformed and credit-budget cases are separate tests.

Library choice: BCL records, BigInteger, EqualityComparer, Queue and
BinaryPrimitives provide representation/collections/endian conversion. Protocol
edge and reset rules are domain semantics, so a generic assertion library would
not replace them. Existing FsCheck, SharpFuzz/AFL++, Stryker and Coverlet are reused;
no additional production package or second property framework is introduced.
The protocol basis is [Arm IHI0022H A3.3/B1.1](https://developer.arm.com/-/media/Arm%20Developer%20Community/PDF/IHI0022H_amba_axi_protocol_spec.pdf).

Icarus 12.0 and Verilator 5.020 each pass all eleven replay designs, including
4,165 Lite edges and 321 Stream edges. Yosys 0.33 generic synthesis checks pass
both AXI designs, the planned-bank example and both shift designs (five total).
Artifacts: `/tmp/sharphdl-rtl-1gKCkA`; log: `build/verification/wp06-rtl.log`.
The replay vectors are generated only after the independent transaction models
agree with the Core simulator; actual emitted Verilog then replays every edge.
Generic synthesis is structural evidence, not technology timing or FPGA deployment.
The final all-project sweep caught a linked-consumer regression: a CoreReplay
overload referred directly to legacy CoreGraphExport, unavailable in the Core-only
Z80 demo. The overload now accepts Core components; no project dependency was
added. Both affected tools rebuild successfully. Repeating all eleven RTL replays
and five Yosys checks after that fix passes at `/tmp/sharphdl-rtl-3IBrMz`, log
`build/verification/wp06-rtl-final.log`.

The extended SharpFuzz 2.2.0/AFL++ 4.09c campaign completes 60 seconds with seed
261026: 4,671 executions, 197 corpus entries, 2.81% host bitmap occupancy and
zero saved crashes/hangs. Each case adds up to eight AXI command bytes to the
existing graph/planner/shift/table checks; immutable exports are reused with fresh
simulation/checker state. This is a bounded sampled campaign, not a speed comparison
with the earlier smaller harness. Artifacts: `/tmp/sharphdl-fuzz-oBgdyn`;
log: `build/verification/wp06-fuzz.log`.

The first whole-campaign AXI mutation run was stopped after a prolonged test phase
and has no final mutation score. The retained `wp06-mutation.log` is exploratory
evidence only. The final monitor-focused configuration excludes the expensive
whole-peripheral directed/property campaigns, which pass separately on both OSes
and in generated RTL. It still mutates the entire new checker source and runs
the dedicated positive/negative checker tests; no hand-written mutation engine
or mock DUT substitutes for Stryker.

The first completed monitor-focused run kills 154/186 (82.80%) with 32 survivors.
Review adds the boundary, reset, changing-offer, credit-replacement and channel-name
checks described below. The final whole-file selection kills **177/186 (95.16%)**,
with nine survivors, zero uncovered/timeouts/errors. Six compile-invalid and nine
tool-ignored selected-file variants are excluded. Survivors are six generic
message-string changes and three removed `checked` expressions: overflow after
2^63 lifetime transfers remains unexecuted. Complete line/branch coverage does
not imply those exception paths were challenged. The source is unchanged between
these two completed mutation runs; stronger tests detect 23 additional variants.
Raw final report: `build/verification/wp06-mutation-reviewed/reports/mutation-report.json`;
configuration: `tests/SharpHDL.UnitTests/stryker-axi-conformance.json`.

## Integrated verification

The initial Windows SDK 10.0.403 Release regression passes 257/257 tests, zero failed/skipped,
16 seconds. This includes all seven new Z80 oracle, eight planner and sixteen AXI
tests. Coverlet measures 65.41% line / 51.90% branch over its instrumented managed
assemblies (not native tools or HDL toggles). Both new AXI monitors, including
the nested held-channel helper, measure 100% line / 100% branch; the planner
measures 100% line / 96.15% branch. Defensive planner branches remain uncovered.
Adapter-specific coverage is the separate IncludeTestAssembly run reported above.
Log/TRX/XML: `build/verification/wp456-windows.log` and `wp456/windows`.
Existing xUnit analyzer warnings for public hardware fixture methods and one
Assert.Single style warning remain; do not describe the repository as warning-free.

The initial Linux SDK 10.0.402 run independently passes the same 257/257 tests with zero
failed/skipped in 17 seconds, using native isolated outputs at
`/tmp/sharphdl-workpackages-tests-lCxa7W`. Its instrumented managed coverage is
also 65.41% line / 51.90% branch. Log/TRX/XML: `build/verification/wp456-linux.log`
and `wp456/linux`. The 100 planner and 60 AXI FsCheck cases use replay seed
`1145655947,296144285`; no failing counterexample was found.

After reviewing mutation survivors, three additional facts check the minimum
stream profile and changing unstalled offers, Lite maximum fields and same-edge
credit replacement, and cancellation of every stalled channel/pending credit.
The stalled-channel negative tests also require actionable channel names. The
final regressions pass **260/260 on Windows (17 seconds) and Linux (18 seconds)**,
zero failed/skipped, including seven new Z80, eight planner and nineteen AXI tests.
Logs: `wp456-windows-final.log`, `wp456-linux-final.log`; TRX/coverage:
`build/verification/wp456-final/{windows,linux}`. Linux artifacts are native at
`/tmp/sharphdl-workpackages-tests-xps81y`. Earlier 257-test results remain historical
checkpoints, not a substitute for this final source run.
Both final coverage files retain 65.41% line / 51.90% branch overall, 100%/100%
for AXI checkers and 100%/96.15% for the planner. These scopes exclude the native
HDL tools, HDL toggles and the separately measured adapter test assembly.

Real StrictDoc 0.30.4 exports `CONTINUATION_4_6.sdoc` and `AXI_CONFORMANCE.sdoc`
successfully with stable IDs. The project-layer/cycle check and `git diff --check`
also pass. Large raw outputs remain ignored; retained summary evidence is stored
under `docs/verification` with source hashes and replay commands.

All 24 tracked managed projects have successful Release builds: the initial
serialized sweep passed 23 then caught the shared-helper error above; after the
fix, both linked consumers passed again. Logs: `wp456-build-all.log` (retains the
initial failure) and `wp456-replay-bridge-build.log` (corrected consumers).
The legacy `tests/Tests.csproj` run also passes: 15,212 main C# assertions, 6,698
numeric, 56,479 multiplier and 388,851 divider assertions in its separately
reported scopes. Its emitted HDL benches were generated, not executed by that
command. Generated outputs are archived under `wp456-legacy-generated`; tracked
snapshots rewritten by this run were restored to their initially clean versions.
The independent HDL execution evidence is the eleven-design runner above.

## Reproduction and scope

From a checked repository directory, use .NET 10 with processor count 2, MSBuild
node reuse/shared compilation disabled and mutation concurrency 1 on constrained
hosts. Managed fast regression:

```text
dotnet test tests/SharpHDL.UnitTests/SharpHDL.UnitTests.csproj -c Release -m:1 -nr:false -p:UseSharedCompilation=false --filter FullyQualifiedName!~Z80Tests --collect:"XPlat Code Coverage"
bash tools/run-workpackages-linux.sh wp456
bash tools/run-scheduled-rtl.sh
bash tools/run-scheduled-fuzz.sh 60
python -c "from strictdoc.cli.main import main; main()" export docs/CONTINUATION_4_6.sdoc docs/AXI_CONFORMANCE.sdoc --no-parallelization --output-dir build/verification/wp456-strictdoc
python tests/check-project-layers.py
```

Run Stryker from `tests/SharpHDL.UnitTests` using the pinned local tool and the
`stryker-z80-program.json`, `stryker-operation-plan.json`,
`stryker-operation-plan-review.json` and `stryker-axi-conformance.json` configs.
Linux scripts keep outputs in fresh native `/tmp` directories. The fast filter
excludes the expensive original `Z80Tests` suite; it includes the seven new
continuous-program oracle tests. Original Z80 hardware is unchanged from the
separately recorded full qualification; those historical results are not fresh
whole-ISA evidence. No physical FPGA readiness or performance improvement is claimed.

## Highest-value next tasks

1. WP-07: centralize safe, cancellable HDL-tool execution and retained manifests;
   compare CliWrap against existing BCL Process usage before adding a dependency.
2. WP-08: implement sound bit-vector equivalence for an explicitly bounded IR
   subset with real Z3/Yosys counterexamples and unsupported/unknown handling.
3. Extend scheduling with automatic alias/hazard extraction and multi-cycle
   operator latencies; preserve ordinal determinism and independent lowerer checks.
4. WP-09: establish controlled simulator baselines before optimizing; reuse
   BenchmarkDotNet and equal-output regressions, measuring Native AOT separately.
5. WP-10: build the existing engineering/API documentation and extend requirement
   traceability beyond these slices; retain partial/unverified requirement status.
