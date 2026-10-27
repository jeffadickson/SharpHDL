# SharpHDL handoff — continuation verified (2026-10-27)

## Current continuation: work packages 1–3

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
Keep future packages 4–10 as backlog rather than claiming they were implemented.

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
