# Work packages 1–3: implementation and verification

Continuation baseline: clean `main` at
`6b91749aaf0a74554b6114dfc5201595f8ba0d36`, existing origin
`https://github.com/jeffadickson/SharpHDL.git`. Existing history was preserved.
The [ten-package plan](WORK_PACKAGES_2026-10-27.md) describes every proposed
capability, library rationale and tradeoff. Packages 4–10 remain future work.

## Implemented scope

- **WP-01:** indexed, ED, I/O and interrupt behavior in the existing Z80 Core test
  design, independently qualified within the documented architectural scope.
  Commits `5ec0c1c`, `42d96a0`, `7a4c975`, `6b6863c`.
  [Detailed evidence and limitations](Z80_COMPLETION_2026-10-27.md).
- **WP-02:** immutable structured CIL rejections with method/IL/reason/source,
  retained optional-symbol failures and compatible `NotSupportedException`.
  BCL metadata reuse; eight directed tests include hidden/missing/malformed
  symbols, global methods, offset zero and malformed negative branch targets.
  Commit `1aee8b3`. [API and reproduced defect](CIL_DIAGNOSTICS_2026-10-27.md).
- **WP-03:** bounded immutable instruction graphs, canonical reachability and
  strongly connected/reachable cyclic components, integrated before scheduled
  stack validation. QuikGraph 2.5.0 supplies BFS/SCC; SharpHDL owns validation,
  ordering and hardware semantics. Compiler state declaration order is preserved.
  Eleven graph test cases include a 200-case fixed-seed FsCheck property.
  Commit `5937579`.
  Existing example suite now exports control-flow JSON beside source maps.
  [Architecture, library tradeoffs](INSTRUCTION_CONTROL_FLOW.md) and
  [third-party metadata](THIRD_PARTY.md).

StrictDoc requirements: CIL-DIAG-001 and CIL-CFG-001/002/003. Public APIs include
XML documentation. No replacement implementation or new graph dependency in Core.
FsCheck seed: `1145655947,296144285`; no counterexample was found in 200 generated
graphs up to eight vertices compared with independent Floyd-Warshall closure.

## Executed results and scope

WP-01's full suites passed **233/233 on Windows and Linux** before diagnostic/graph
changes. Z80 hardware/source hashes remain in its evidence manifest. Its RTL,
mapped replay, protocol induction, fuzz and mutation results remain qualified
only for that source and scope; they are not falsely relabeled as later full runs.

For WP-02/03, Windows SDK 10.0.403 passes **226/226 non-Z80 tests**, zero failures
or skips. The initial run took 32 seconds; the final post-build recheck took 39
seconds. Final log: `build/verification/wp03-windows-final.log`;
TRX: `build/verification/wp03/windows-final/tests.trx`. This scope intentionally omits
the separately completed expensive Z80 campaigns and is not a new full-suite run.

Coverlet 10.1.0 in this non-Z80 run reports **2,887/5,217 lines**, **2,504/5,193
branches**. `CilDiagnostic` and `InstructionControlFlow` each have **100% line
and branch** execution. Aggregate coverage includes unexecuted example paths and
must not be compared as equivalent to WP-01's full-suite percentages. Coverage
does not measure HDL toggles, the standalone fuzz runner or third-party library
internals, and does not establish semantic correctness by itself.

StrictDoc 0.30.4 exported both requirement documents successfully using
`--no-parallelization`; layering validation passed. Existing legacy nullable and
xUnit fixture analyzer warnings remain; no claim of a warning-free repository.
Windows parallel StrictDoc export was stopped after stalling at parsing; its
successful serial run is `build/verification/workpackages-strictdoc-final.log`;
the final requirement recheck passed in `build/verification/workpackages-strictdoc-final2.log`.

Stryker.NET 5.0.0 diagnostic scope: **22 killed, 4 survived, 0 uncovered, 0
timeouts**, **84.62%** of 26 testable mutants. Ten selected-file compile errors
and three ignored mutations are excluded. Survivors remove only message-format
punctuation; typed-field and both offset-boundary mutations are caught. Whole-project
compile/filter totals are not counted as selected-file successes. Raw report:
`build/verification/wp02/mutation-boundaries/reports/mutation-report.json`.

Stryker.NET graph scope: **41 killed, 1 survived, 0 uncovered, 0 timeouts**,
**97.62%** of 42 testable mutants; 12 selected-file compile errors and one ignored
mutation excluded. The survivor changes the internal graph's parallel-edge flag;
the adapter already supplies distinct edges, so this is equivalent under the
adapter invariant. The score retains it rather than silently excluding it.
An initial 35/42 result prompted stronger actionable-error tests; the final run
catches missing entry validation and empty limit/error reasons. Raw report:
`build/verification/wp03/mutation-reviewed/reports/mutation-report.json`.

Linux SDK 10.0.402 passes **226/226 non-Z80 tests**, zero failures/skips, 34 seconds
test duration. Run `bash tools/run-workpackages-linux.sh`; native artifacts remain
at `/tmp/sharphdl-workpackages-tests-reokVi`, TRX/coverage under
`build/verification/wp03/linux`, log `build/verification/wp03-linux.log`.
This validates the selected QuikGraph adapter algorithms on both target platforms.

Icarus 12.0 and Verilator 5.020 each pass all **eight scheduled designs** through
`tools/run-scheduled-rtl.sh`; Yosys 0.33 generic checks pass both shift designs.
The runtime loop agrees with independent native/legacy/Core behavior, including
partial RAM writes on a bounds fault. Artifact root: `/tmp/sharphdl-rtl-VvFRy9`;
log: `build/verification/wp03-rtl.log`. JSON exports report loop/shift/raw-shift
reachable counts **30/18/12** and cyclic component counts **1/0/0** respectively.

SharpFuzz 2.2.0 with AFL++ 4.09c, seed **261026**, **60 seconds**: **2,798 executions**,
**96 corpus entries**, **zero saved crashes/hangs**, **2.14% host bitmap occupancy**.
The extended established harness checks valid/malformed graphs against closure,
plus existing raw-CIL shifts and table traffic. This is a bounded campaign, not
all-input coverage; third-party library and HDL toggle coverage are unmeasured.
Run `bash tools/run-scheduled-fuzz.sh 60`; raw artifacts:
`/tmp/sharphdl-fuzz-V1uLbW`, log `build/verification/wp03-fuzz.log`.

All **24 tracked managed projects** build on Windows/.NET 10: 23 C# projects plus
the F# example. Logs: `build/verification/wp03-all-builds.log` and
`build/verification/wp03-fsharp-build.log`. This includes compiling the integration
test project; its Docker/Testcontainers tests were not run in this continuation.
The final Windows recheck passes **226/226**, zero failed/skipped, and preserves
the exact coverage fractions above. The legacy executable also passes, run from
an isolated directory so generated fixtures cannot overwrite tracked files.
Its log is `build/verification/wp03-legacy.log`; it reports independent groups
including 6,698 numeric, 56,479 multiplier, 388,851 divider and 15,212 main C#
assertions. These group counts are not added to the xUnit total. Additional legacy
HDL benches were generated, not executed by that managed invocation. Artifacts:
`build/verification/wp03/legacy-28ceb1945c5448a4872f3aaf5ec62993`.

## Reproduction and limitations

Check that the repository directory exists before changing into it. On this host
use `DOTNET_PROCESSOR_COUNT=2`, `DOTNET_CLI_USE_MSBUILD_SERVER=0`,
`MSBUILDDISABLENODEREUSE=1`, `UseSharedCompilation=false`; serialize builds and
use `-m:1 -nr:false -p:UseSharedCompilation=false` for direct .NET builds/tests.
Stryker 5.0.0 is installed locally in `build/tools`, not assumed on PATH. From the
checked `tests/SharpHDL.UnitTests` directory use `--config-file stryker-diagnostics.json`
or `stryker-control-flow.json`, `--concurrency 1` and an isolated `--output` path.
GitHub Actions remains disabled; no hosted checks were dispatched.

[Machine-readable evidence](verification/work-packages-2026-10-27.json) pins the
implementation revision and source SHA-256 hashes, exact regression and selected
mutation counts, tool versions, seeds and artifact locations. Temporary Linux
and ignored build artifacts are reproduction aids, not durable archival storage.
The final evidence/handoff commit changes documentation only.

Library algorithms do not implement resource scheduling, stack semantics or
termination proofs. The new graph API qualifies only selected BFS/SCC operations
and documented limits; trimming/Native AOT of that dependency is unmeasured.
Z80 remains instruction-level, with WZ/Q undocumented flag effects and T-state
pin behavior outside scope. No new physical FPGA, exhaustive ISA or compiler
equivalence claim is made, and no performance optimization/speedup is claimed.

The five next priorities are WP-04 continuous-program Z80 reference qualification,
WP-05 bank/resource scheduling, WP-06 AXI property campaigns, WP-07 portable tool
orchestration and WP-08 sound declared-subset equivalence. WP-09 optimization and
WP-10 broader documentation generation follow. Their libraries are candidates
until individually validated and adopted.
