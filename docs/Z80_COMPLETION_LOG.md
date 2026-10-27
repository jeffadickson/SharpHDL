# Z80 completion development log

Baseline: clean main at 6b91749aaf0a74554b6114dfc5201595f8ba0d36.
User requests indexed/ED/I/O/interrupt completion, local commits after each item,
final verification and push, then remaining work. Keep Actions disabled.

## Item 1 — indexed addressing

Implemented DD/FD pairs/bytes, signed displacement, indexed CB copies and BIT,
H/L memory-transfer exceptions, EX(SP),HL/IX/IY and NMOS SLL. Repeated prefix
handling retains the latest index selection. Added 976 independently expected
vectors from 122 pinned upstream files. Existing 840 base vectors also pass.
`dotnet test tests/SharpHDL.UnitTests/SharpHDL.UnitTests.csproj -c Release --filter FullyQualifiedName~Z80Tests`
passes 14/14 tests (1m45s test duration), including existing random arithmetic,
stack wrap, reset and program tests. Log: build/verification/z80-indexed.log.

Added ReadOutputs for one immutable simulator snapshot and reused it in the
vector harness/replay to avoid repeated evaluations. This is not a measured
performance claim. Full coverage, mutation and RTL refresh follow after the
remaining instruction additions; checked-in RTL currently represents the prior
base/CB implementation and must be regenerated before final delivery.

## Item 2 — ED and port I/O

Implemented documented ED families, aliases/undefined ED NOPs, 16-bit ADC/SBC,
I/R and IM state, RETN/RETI, RRD/RLD, block transfer/compare and ordered 16-bit
port I/O. DI/EI state and delay countdown prepare interrupt service. Added 672
published state/RAM/port vectors from all 80 ED corpus files plus four base files.
The full ED/port vector test passes (45s); separate repeat/undefined checks pass
4/4 (11s). Logs: build/verification/z80-ed-final.log and z80-ed-repeat-final.log.

Independent vectors exposed continuing block transfer and block-I/O flag details;
fixed restart-PC bits and repeat-path parity/half-carry folding. All comparisons
retain full flags for these ED cases. Base/indexed fixture records were compacted
without changing data. Indexed/base/refresh regression and full RTL qualification
will run against the final combined core. External interrupt inputs are not yet added.
