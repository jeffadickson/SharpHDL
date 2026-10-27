# Z80 test-design implementation and evidence

Started with clean main at bf173ea522ae4e986dff7281e6cd9964fef5300f.
Implementation commit: 87dcd71bd4554c0719f1c21288069db1302d5fb5.
No existing work was discarded. GitHub Actions remains disabled.

## Delivered

[The example](../examples/Z80/README.md) contains a C# scheduled hardware core,
generated Verilog, independent pinned instruction vectors/license/provenance,
StrictDoc requirements and a reproducible RTL demo. The 2,957-node typed Core graph
contains real hardware registers and logic; no software emulator supplies CPU state.
An external ready/valid byte bus supports deterministic stalls. Debug initialization,
instruction retirement, continuous stepping, HALT and explicit unsupported-opcode
faults make the design useful as a compiler/simulator regression workload.

The supported base/CB subset includes register/memory loads, ALU/flags, decimal
adjust, 16-bit arithmetic, alternate registers, jumps/loops, calls/returns, stack
operations and bit operations. This is **not a complete Z80 implementation**:
DD/FD/ED, interrupts, I/O, DI/EI, EX (SP),HL, undocumented SLL and physical timing
remain unsupported. Missing refresh/I/R, IX/IY and WZ/Q state prevents claiming
full ISA or machine compatibility. Unsupported instructions trap rather than acting
as NOPs. BIT(HL), SCF and CCF only promise documented flags.

## Executed checks

| Check | Actual result |
|---|---|
| Windows .NET 10.0.403 full xUnit suite | 223 passed, zero failed/skipped |
| Isolated Linux .NET 10.0.402 full xUnit suite | 223 passed, zero failed/skipped |
| Published independent oracle | 840 register/RAM vectors from 105 pinned files; stalled bus fields checked |
| FsCheck ALU oracle | 64 generated input cases × 8 operations; replay 1145655947,296144285 |
| Composed machine-code program | 30 instructions / 267 edges: sum 1..10, CALL, two RAM stores, RET, HALT |
| Negative/boundary checks | Illegal prefixes/control/SLL, sticky faults, reset during stalled write, HALT, PC/SP wrap |
| Icarus 12.0 and Verilator 5.020 | Both pass all 11,548 generated replay edges |
| Yosys 0.33 | hierarchy/proc/opt/check/stat pass; 1,130 generic cells |
| Focused Stryker.NET 5.0.0 | Six AluFlags conditional mutations killed; zero survivors/timeouts/uncovered; 100% scoped score |
| StrictDoc 0.30.4 | Requirement parse/export passed |
| C# importer | Native AOT publish and executable run pass; same vector working-file hash reproduced |

Coverlet 10.1.0 reports managed aggregate 3,030/4,921 lines (61.57%) and
2,519/5,139 branches (49.01%). The Z80 elaborator reports 235/235 lines and
68/68 branch conditions. **Elaboration coverage does not measure exercised ISA
semantics or hardware toggles.** CLI/importer and HDL toggle coverage are unmeasured.
Different historical coverage scopes must not be treated as a performance/quality
trend. The focused mutation result qualifies six flag-condition changes, not the
entire processor. The initial mutation filter used line numbers where Stryker
expects character offsets; that run selected no mutants and produced no score.
The checked-in configuration corrects this and documents its source span.

Raw logs and TRX/Cobertura/Stryker reports remain in ignored build/verification/z80*
paths. Linux artifacts are retained at /tmp/sharphdl-z80-YrgO7W (RTL) and
/tmp/sharphdl-z80-tests-6oWsUs (managed). These temporary directories are not an
archive. [Versioned evidence](verification/z80-2026-10-27.json) records source
hashes, tool versions, scopes and selected mutants. Windows JSON whitespace and
Git's LF normalization can yield different file hashes without changing data.

## Reproduction and dependency rationale

Use the checked commands in [the example README](../examples/Z80/README.md).
tools/run-z80-managed.sh keeps native Linux outputs separate; tools/run-z80-rtl.sh
runs both independent HDL simulators and generic synthesis. Generated module
examples/Z80/generated/Z80TestCore.v comes from that verified run.

The oracle is an unaltered record subset from MIT-licensed
[SingleStepTests/z80](https://github.com/SingleStepTests/z80/tree/ebe1875d48f374bcfd4b505d8eb8ee751568b5f7).
Normal verification is offline. Existing FsCheck/Stryker and BCL JSON/HTTP/SHA256
avoid new production dependencies. [Library tradeoffs](LIBRARY_FIT_2026-10-27.md#z80-test-design-decisions)
assign Z80dotNet to future continuous-program differential qualification and explain
native floooh/chips and redcode/Z80 alternatives. No such emulator was integrated.
Instruction reference: [Zilog Z80 user manual](https://www.zilog.com/docs/z80/um0080.pdf).

## Remaining verification and development work

This finite oracle sample is based on a software model; it is not silicon capture,
ZEXALL qualification or formal proof. No new coverage-guided Z80 fuzz campaign or
performance benchmark ran. Existing unrelated analyzer warnings remain. Yosys checks
establish generic structural synthesis, not target placement/timing or board readiness.

The highest-value follow-ups are:

1. Add DD/FD indexed addressing and EX (SP),HL with displacement/wrap tests.
2. Implement ED arithmetic, transfers, block operations and port transactions.
3. Add I/R, interrupt modes, EI delay, NMI and HALT wake-up with explicit bus semantics.
4. Integrate and qualify a continuous-program oracle, then expand opcode/vector coverage.
5. Broaden mutation/coverage-guided campaigns and qualify target synthesis/timing.

The current scope deliberately remains visible so future work can extend this
test design without mistaking sampled behavior for complete Z80 conformance.
