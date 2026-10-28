# SharpHDL handoff — continuation verified (2026-10-27)

## Current continuation: work packages 4–6

Baseline: clean main at 5179ddf. WP-04 is committed as 04689d3 and WP-05 as
8fc8062. WP-04 adds a test-only Z80dotNet 1.1.0 continuous-program oracle with
qualified flag/interrupt limitations. WP-05 adds a bounded deterministic static
dependency/resource planner and a real two-bank/shared-multiplier example.
WP-06 adds strict bounded AXI-Lite/Stream monitors and shared independent queue/
byte-register campaigns reused by tests, HDL replay and SharpFuzz. See the
[current execution report](docs/WORK_PACKAGES_4_6.md) for fresh results and limits.

Final managed regressions pass 260/260 on Windows and Linux. Eleven designs pass
Icarus/Verilator and five pass generic Yosys checks; the new AXI checkers have
100% measured managed line/branch coverage. The original expensive Z80Tests
suite is excluded from this fresh gate; its earlier qualification below remains
historical. The shared replay helper accepts Core components so the Z80 demo
keeps its Core-only dependency boundary. Current source hashes and exact scopes
are retained in `docs/verification/work-packages-4-6-2026-10-27.json`.

Packages 7–10 remain backlog. In particular, the planner requires declared alias
dependencies and one-edge operations; protocol checks do not prove liveness;
the CPU reference does not qualify arbitrary IM0 injection, WZ/Q or pin timing.
Use `tools/run-workpackages-linux.sh wp456` to isolate current Linux test evidence.
Keep CI disabled; this continuation authorizes a normal final push only after
the package checks. Preserve unknown changes and keep Linux outputs native.

## Previous continuation: work packages 1–3

Baseline was clean main at 6b91749. Z80 indexed, ED/I/O and interrupt additions
are committed as 5ec0c1c, 42d96a0 and 7a4c975; qualification is 6b6863c.
The existing instruction-level core now includes base/CB/DD/FD/ED, IM0/1/2,
edge-pending NMI, EI/HALT behavior, RETN/RETI and 16-bit port transactions.
Windows/Linux full suites pass 233/233, with Icarus/Verilator and mapped iCE40
replay passing 56,514 edges. Six stalled-request assertions pass two-state
temporal induction with a failing negative control. See
[scope and exact evidence](docs/Z80_COMPLETION_2026-10-27.md).
T-state/pin timing, internal WZ/Q flag effects, exhaustive ISA/ZEXALL/CP-M and
physical FPGA timing remain unqualified; do not infer them from the results.

The [ten work packages](docs/WORK_PACKAGES_2026-10-27.md) include library rationales;
1–3 are completed within their stated scopes. WP-02 (`1aee8b3`) adds source-aware structured compiler
rejections using the existing BCL PDB reader. WP-03 adds deterministic instruction
graphs via QuikGraph 2.5.0 (MS-PL), isolated behind HLS result types (`5937579`). All
24 managed projects build. The integrated
Windows/Linux non-Z80 suites pass 226/226. Eight scheduled designs pass both HDL
simulators. Diagnostic mutations kill 22/26 and graph mutations 41/42 testable
mutants; survivors and bounded fuzz scope remain explicit in the
[completion report](docs/WORK_PACKAGES_COMPLETION_2026-10-27.md).
Never combine differing coverage scopes as if equivalent.
At that checkpoint packages 4–10 remained backlog; the newer continuation above
supersedes the status of packages 4–6 only.

Use .NET 10 (`global.json`), checked directories and separate native Linux outputs.
To avoid local paging-file exhaustion, serialize builds, use processor count 2,
disable MSBuild node reuse/shared compilation, and run mutation concurrency 1.
Run StrictDoc export with `--no-parallelization` on this Windows installation.
GitHub Actions remains disabled. This task authorizes a normal final push;
never enable/dispatch workflows or force-push.

## Historical initial Z80 test design

The following records the earlier 87dcd71 scope, superseded above.

Implementation 87dcd71 adds examples/Z80: a synthesizable scheduled base/CB subset,
generated Verilog, 840 pinned independent vectors and a multi-instruction program.
Fresh full suites pass 223/223 on Windows and Linux. Both HDL simulators pass
11,548 replay edges; generic Yosys checks pass. Six focused ALU mutations are killed.
Coverage is 61.57% line / 49.01% branch across the recorded managed scope; the Z80
elaborator is fully covered but that does not establish ISA/toggle coverage.
[Report and limitations](docs/Z80_TEST_DESIGN_2026-10-27.md) and
[reproduction/interface](examples/Z80/README.md) describe that initial evidence;
the interface documentation now reflects the completed additions.
DD/FD/ED, interrupts, I/O, DI/EI, EX(SP),HL and SLL were absent at 87dcd71 and
have since been added. Physical timing remains outside scope.
No complete Z80/CP-M/FPGA compatibility claim. The earlier continuation below is
historical evidence, preserved for source-map/compiler work.

## Previous compiler continuation

Latest continuation began at f116697. Scheduled CIL supports 32-bit shl/shr/shr.un
with CLR-compatible count masking and explicit arithmetic/logical right shifts.
Matched sidecar/embedded Portable PDBs map actual scheduler encodings to source
spans; hidden/unmapped states stay null. No new production NuGet dependency.

Fresh results: 207/207 xUnit tests on Windows and isolated Linux; eight designs
pass Icarus and Verilator; both shift designs pass generic Yosys checks. Managed
coverage is 61.53% line / 48.86% branch. Focused mutation is 88.89% with five
survivors, six uncovered and two timeouts retained. Fixed-seed raw-CIL fuzzing:
4486 executions in 60 seconds, no saved crashes/hangs. Legacy regression,
StrictDoc and layering checks pass. No new physical or performance claim.

[Current report](docs/CONTINUATION_SHIFTS_AND_SYMBOLS.md) provides scope, limitations,
commits, commands and evidence links. [Previous report](docs/FOLLOWUP_2026-10-27.md)
contains historical arithmetic, memory and target-mapping evidence; do not treat
those historical numbers as fresh results. [Library decisions](docs/LIBRARY_FIT_2026-10-27.md)
remain applicable, including deferred candidates.

Prioritize source-aware diagnostics, checked/narrow CIL, remaining symbol-reader
qualification, iterative/cross-kernel scheduling and expanded memory/target checks.
Calls, checked arithmetic, division, packed ports, exception handlers, true multiport
RAM and multiclock Core remain unsupported or unqualified. State/source maps do
not describe every optimized wire. No formal or physical FPGA readiness claim.

GitHub Actions is explicitly disabled by the user: do not enable or dispatch it.
The current task authorizes local commits and a push after final tests. Preserve
unknown changes; never force-push. Keep native Linux outputs separate from Windows.

## Packages 7–10 continuation (2026-10-27)

Start from the current checkout and read docs/WORK_PACKAGES_7_10.md, the baseline
report docs/SIMULATOR_BASELINES_2026-10-27.md and the real StrictDoc specifications.
Do not restart old iteration quotas. Actions remains disabled; no automatic CI.
This task authorizes one normal push after the final verified documentation commit.

Implemented: retained cancellable HDL execution (BCL), bounded native Z3 QF_BV
checks with independent replay, typed-effect hazard regions and actual iterative
multipliers. WP-09 establishes eight JIT/AOT measurements and an isolated workload
library; no simulator algorithm optimization is claimed. WP-10 is an opt-in
managed Sphinx/compiler-XML/StrictDoc driver; output must be outside the repository.

Fresh gates: 35 managed projects compile on Windows with warning-sensitive
analyzers; 288 fast tests and 52 native Z3 tests pass on each OS; four documentation
tests pass per OS; four workload tests pass on Windows; the patched local Docker/
Icarus forty-cycle fixture passes on Linux. Twelve scheduled designs pass Icarus/
Verilator, six generic Yosys checks pass, and 44 native Z3/Yosys equivalence cases
retain counterexamples. Root regression assertions and focused coverage/mutation/
fuzz scopes are in the report. Full historical Z80 vector tests were not rerun.

Reproduce with checked directories using tools/run-workpackages-linux.sh,
tools/run-verification-linux.sh (matching pinned Z3 native assets),
tools/run-equivalence-linux.sh, tools/run-scheduled-rtl.sh and, after local jobs
finish, tools/run-simulator-baselines-linux.sh. Documentation commands and private
Python dependencies are in docs/ENGINEERING_BUILD.md. No FPGA timing, full-ISA,
sequential-proof, full API-comment or general Native AOT launcher claim is implied.

Current policy: Native AOT is deferred. Use ordinary .NET 10/JIT for development.
The controlled benchmark script now defaults to four JIT cases; native comparison
requires an explicit --include-native-aot opt-in. Historical results remain valid
for their recorded scope. Continue compiler correctness and HDL verification first.

Current continuation adds unchecked conv.i1/u1/i2/u2 on the scheduled 32-bit CIL
stack and an explicit HLS SignExtend value. Narrow locals/ports and checked/wide/
floating/native conversions still reject. Read docs/CONTINUATION_JIT_NARROW_2026-10-27.md
and docs/JIT_AND_NARROW_CIL.sdoc for acceptance, actual results and limitations.
Fresh scope: 296 fast and 60 native Z3 tests per OS, five added HDL designs,
retained conversion witnesses, focused mutation and a finite fuzz campaign.
Native AOT stays deferred; preserve CI-off policy and existing history.

Active continuation: narrow storage/ports and checked conversions. See docs/CONTINUATION_STORAGE_CHECKED_2026-10-27.md and docs/NARROW_STORAGE_CHECKED.sdoc. Native AOT deferred; CI disabled.
