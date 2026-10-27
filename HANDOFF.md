# SharpHDL handoff — continuation verified (2026-10-27)

## Latest addition: Z80 architectural test design

Implementation 87dcd71 adds examples/Z80: a synthesizable scheduled base/CB subset,
generated Verilog, 840 pinned independent vectors and a multi-instruction program.
Fresh full suites pass 223/223 on Windows and Linux. Both HDL simulators pass
11,548 replay edges; generic Yosys checks pass. Six focused ALU mutations are killed.
Coverage is 61.57% line / 49.01% branch across the recorded managed scope; the Z80
elaborator is fully covered but that does not establish ISA/toggle coverage.
[Report and limitations](docs/Z80_TEST_DESIGN_2026-10-27.md) and
[reproduction/interface](examples/Z80/README.md) are the current evidence.
DD/FD/ED, interrupts, I/O, DI/EI, EX(SP),HL, SLL and physical timing remain absent.
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
