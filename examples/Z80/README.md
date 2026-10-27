# Z80 hardware test design

`Z80TestCore.Build()` constructs actual registers, ALU logic and a microsequencer
using SharpHDL's scheduled API. The same typed Core IR runs in the simulator and
emits Verilog. No host Z80 emulator executes the hardware implementation.

This is an instruction-level Z80 design for testing SharpHDL, **not a
pin-compatible Z80**. It supports the documented unprefixed register/memory loads,
8-bit ALU, INC/DEC, 16-bit INC/DEC and ADD HL, accumulator rotates, DAA/CPL/SCF/CCF,
exchanges, relative/absolute/conditional jumps, DJNZ, stack PUSH/POP, CALL/RET/RST,
NOP and HALT, CB rotate/shift/BIT/RES/SET including (HL), and DD/FD indexed forms.
IX/IY pairs, index bytes, signed displacements, indexed CB register copies,
EX (SP),HL/IX/IY and common NMOS SLL are supported. Repeated index prefixes use
the final prefix; memory forms retain the Z80's ordinary H/L transfer exceptions.

Currently unsupported instructions trap: ED instructions, IN/OUT and DI/EI.
Interrupts/NMI, bus ownership, refresh/I/R, WZ/Q, physical pin signals and Z80
T-state timing are absent. ED/I/O/interrupt extensions are the next active items.
BIT (HL), SCF and CCF only promise documented flags; Q/WZ-dependent flag bits 3/5
are excluded from those upstream comparisons. Other supported flag results include
bits 3/5 in the checked scope. No claim of ZEXALL, CP/M or complete ISA conformance.

## Interface

All inputs/outputs have the `s_` prefix in generated Verilog. `clk` and `rst` are
the usual rising-edge clock and synchronous reset. Reset clears architectural
registers and sets SP=FFFF; it cannot undo a previously accepted external write.

| Ports | Meaning |
|---|---|
| step, load_state | While idle, load overrides step; step starts one instruction. Hold step high to run. |
| init_af/bc/de/hl, init_pc/sp, init_alt0..3 | Debug initialization accepted only on load_state while idle; alternate order BC, DE, HL, AF. |
| init_ix/iy, ix/iy_out | Index-register initialization and architectural observation. |
| bus_valid, bus_write, bus_address, bus_wdata | One outstanding byte request; stable until accepted. bus_write=0 means read. |
| bus_ready, bus_rdata | Acceptance and read byte, sampled only on a valid/ready edge. |
| instruction_done, idle, fault, halted_out | Retirement pulse, idle state, sticky illegal-instruction stop, HALT latch. |
| af/bc/de/hl_out, pc/sp_out, alt_out0..3, opcode_out | Observable architectural state and last base/prefix opcode. |

HALT retires once and then suppresses fetches until reset or an idle debug load.
Fault recovers only on reset. A faulting fetch has advanced PC; use opcode_out to
identify the rejected base/prefix byte. Interrupt wake-up is unsupported.

## Run locally

From an existing checkout directory (PowerShell):

```powershell
if (!(Test-Path -LiteralPath 'examples/Z80' -PathType Container)) { throw 'Run from repository root' }
dotnet test tests/SharpHDL.UnitTests/SharpHDL.UnitTests.csproj -c Release --filter FullyQualifiedName~Z80Tests --collect:'XPlat Code Coverage'
if (!(Test-Path -LiteralPath 'build' -PathType Container)) { throw 'Missing build parent' }
dotnet run --project tools/SharpHDL.Z80Demo -c Release -- build/z80
```

The demo emits Z80TestCore.v, a file-driven SystemVerilog testbench and replay
vectors after checking 840 independent instruction cases and a 30-instruction
program. The program sums 1..10, calls a RAM-writing subroutine, returns and halts.
The reusable campaign supplies stalls during fetch, operands, stack and writes.
`generated/Z80TestCore.v` is the checked-in generated module from the verified run.

On Linux, `bash tools/run-z80-rtl.sh` runs Icarus, Verilator and generic Yosys checks
with isolated /tmp artifacts. Missing tools cause an explicit error. Passing
simulation/synthesis checks does not establish physical timing or FPGA readiness.
GitHub CI remains disabled; this command never enables or dispatches it.

## Oracle and library tradeoffs

Instruction semantics follow the [Zilog CPU manual](https://www.zilog.com/docs/z80/um0080.pdf).
840 records (first eight from each of 105 files) are pinned from the MIT-licensed
[SingleStepTests/z80](https://github.com/SingleStepTests/z80/tree/ebe1875d48f374bcfd4b505d8eb8ee751568b5f7)
corpus. `vectors-provenance.json` records upstream revision and complete input-file
SHA256 hashes; `SingleStepTests.LICENSE` retains attribution. `vectors.json` is an
unaltered record subset. Refresh reproducibly using the checked-in C# file app:
`dotnet run tools/ImportZ80Vectors.cs`. Normal builds/tests require no network.
The importer uses BCL HttpClient/System.Text.Json/SHA256 and bounded parallelism.

The corpus models additional CPU state and physical cycles. This core compares
AF/BC/DE/HL, alternate pairs, PC/SP and RAM, with the explicit flag exclusions
above. Cycle traces, refresh, WZ/Q and interrupt state are not compared. Upstream
is a software-model oracle, not captured silicon evidence; finite samples can
miss defects. FsCheck adds fixed-seed arithmetic cases and an independently written
integer/overflow reference. No additional production dependency is required.

[Z80dotNet](https://github.com/Konamiman/Z80dotNet) is a candidate for a future managed
continuous-program oracle, but its emulator abstractions do not synthesize through
the current frontend. Adding it would require an adapter and compatibility checks.
[floooh/chips](https://github.com/floooh/chips) offers a native cycle-oriented oracle
under zlib terms, at the cost of a C toolchain/interop and timing adaptation.
[redcode/Z80](https://github.com/redcode/Z80) has broad software conformance coverage,
but adds native integration and LGPL licensing considerations. For this slice,
pinned vectors avoid runtime native dependencies while retaining independent expected
results. None of those emulators has been integrated or qualified here.

Requirements and implementation/test links are in `REQUIREMENTS.sdoc`. Measured
results and remaining verification gaps are recorded in docs/verification, not
inferred from the presence of these commands.

Focused mutation configuration: tests/SharpHDL.UnitTests/stryker-z80.json. Run
Stryker.NET from examples/, supplying that config and the unit-test project.
The filter selects character offsets 5564..6202 (AluFlags); update it if source
changes. It qualifies six conditional flag mutations, not the whole processor.
The Z80-local .gitattributes preserves LF so offsets are stable across platforms.
