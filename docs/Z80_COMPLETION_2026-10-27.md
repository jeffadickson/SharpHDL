# Z80 completion and qualification — WP-01

The requested indexed, ED, interrupt and I/O additions are implemented in the
existing C# scheduled core. Commits: 5ec0c1c, 42d96a0, 7a4c975. Baseline was clean
main at 6b91749. [Interface and reproduction](../examples/Z80/README.md) and
[feature progress](Z80_COMPLETION_LOG.md) describe the accepted scope.

The core now supports the documented base/CB, DD/FD and ED families, common NMOS
aliases/SLL, 16-bit port I/O, IM0 opcode injection, IM1, wrapping IM2 vector reads,
pending-edge NMI, IFF/IM/I/R state, EI delay, HALT wake-up, RETN and RETI indication.
Repeated block instructions retire one iteration at a time. Unsupported debug
IM=3 faults. Undefined ED encodings correctly act as two-byte NOPs.

## Executed evidence

- Full xUnit suites: **233/233 on Windows and Linux**, zero failed/skipped.
  Runs took 26m23s and 21m02s during concurrent local verification; these are not
  simulator benchmark or speedup measurements.
- Four pinned fixture groups contain **3,504 records from 710 distinct upstream
  opcode files**, including every base and CB opcode. Some base records overlap.
  Register/RAM/port checks retain the stated undocumented-flag exclusions.
- Seven independent interrupt scenarios cover all modes, IM0 CALL operands,
  NMI priority/edge retention, RETN/RETI, EI/HALT and a stalled write.
- Icarus 12.0 and Verilator 5.020 each pass **56,514 edges**. The same replay
  passes after Yosys 0.33 iCE40 cell mapping: **4,070 LUT4, 453 carry and 316
  flip-flop cells**, 4,839 cells total. No place/route, pin assignment or physical
  timing was performed; nextpnr-ice40 is unavailable.
- Yosys SAT temporal induction proves the six stalled-request assertions in the
  **default two-state model**, including I/O/ack qualifiers. A deliberately inverted
  valid assertion fails on a six-step reset-started trace, retaining a counterexample.
  Earlier undefined-value modeling failed/timed out; that is not a physical X/Z
  proof. The final proof matches Core's declared two-state semantics, assumes no
  input values, and proves protocol stability—not instruction-set equivalence.
- Focused Stryker.NET 5.0.0: six AluFlags conditional mutants killed, no survivors,
  uncovered mutants or timeouts in that selected scope. **100% is a six-mutant
  score**, not whole-core qualification.
- SharpFuzz 2.2.0 / AFL++ 4.09c: seed 261027, 61 seconds, **273 executions**, 12
  corpus entries, zero saved crashes/hangs, 0.33% host bitmap occupancy. Scope:
  base/IX/IY ALU results/flags, address wrap and WAIT stability. Hardware construction
  was moved inside the initialized fuzz callback after an initial pre-input failure.
- Coverlet managed aggregate: **3,237/5,128 lines (63.12%)**, **2,529/5,149 branches
  (49.11%)**. Z80 elaborator coverage is 100% line/branch; elaboration coverage
  does not measure ISA cases or hardware toggles. CLI/fuzz and HDL toggles are unmeasured.
- StrictDoc 0.30.4 export passed. The current importer was Native AOT-published
  and executed offline to validate 3,504 fixture records and hardware-width bounds.

[Machine-readable evidence](verification/z80-completion-2026-10-27.json) records
source hashes, scope, seeds and tool versions. Raw reports remain ignored under
build/verification/z80-completion*. Linux artifacts: /tmp/sharphdl-z80-q5pPX8
(RTL), /tmp/sharphdl-z80-mapped-WF5TLO (mapped),
/tmp/sharphdl-z80-protocol-3RAzhQ (proof/negative control),
/tmp/sharphdl-z80-fuzz-8PF88v (fuzz). Temporary artifacts are not archival storage.

## Limits and library decisions

This is an instruction-level hardware test core, not a T-state/pin-compatible
replacement. Internal WZ/Q and their BIT(HL)/SCF/CCF undocumented flag effects
remain outside the comparison scope. No ZEXALL, CP/M, exhaustive ISA, silicon,
physical timing or FPGA-board readiness claim is made. Sampling a software-model
oracle and proving one protocol property do not establish those stronger claims.

The production hardware retains no new dependency for Z80. Pinned MIT-licensed
SingleStepTests records provide independent expectations. Existing FsCheck,
SharpFuzz, Stryker and Coverlet supply established verification infrastructure;
the importer uses BCL JSON/HTTP/SHA256. The [ten work packages](WORK_PACKAGES_2026-10-27.md)
include a separately qualified continuous-program oracle and further compiler work.
GitHub Actions remains disabled and no hosted workflow was dispatched.
