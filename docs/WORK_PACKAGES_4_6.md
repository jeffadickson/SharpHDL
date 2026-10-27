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
confined to the non-packable unit-test project. Its old release cadence and
behavioral gaps are costs, not evidence of silicon conformance. Upstream permission
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

## Following packages

WP-05 will reuse QuikGraph-backed component analysis for dependency validation
and implement domain-specific deterministic capacity allocation, with real RAM
bank/shared-arithmetic emission and independent rejection of conflicting schedules.
No optimal scheduling or dynamic runtime arbitration claim.

WP-06 will reuse FsCheck's current xUnit integration and shrinking for protocol
campaigns. Hedgehog was also evaluated: integrated generator shrinking is useful,
but adding a second framework is not necessary for these existing C# xUnit
properties. Preserve replay seeds and negative controls; no mocks of DUT behavior.
See [FsCheck](https://fscheck.github.io/FsCheck/) and
[Hedgehog](https://github.com/hedgehogqa/fsharp-hedgehog).
