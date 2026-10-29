# Typed stack and bound RAM alias continuation

Baseline: clean main at 4fcde8f06c72f0954ed064eb3fcdef8d93f73ad4, tracking
origin/main. CI remains disabled and Native AOT deferred.

Item 1 acceptance: distinguish Int32 stack values, bound RAM references and native
array lengths; reject invalid operand roles and equal-height type merges; validate
all local/argument indexes, including unreachable code. Normalize bounded lengths
only through conv.i4/u4 before integer use. Keep source IL diagnostics.

This closes a real correctness gap: a numeric placeholder previously let malformed
CIL supply an integer as the array operand, or use a RAM reference as a number.
Raw Reflection.Emit fixtures challenge those cases without C#'s type checker.
Native array arithmetic is conservatively unsupported: accepting it as Int32
would, for example, apply the wrong shift-count mask on 64-bit CLR hosts.

Library choice: reuse BCL Reflection.Emit/opcode metadata, the existing decoded
instruction model, HLS InstructionControlFlow and BCL Queue/Dictionary worklists.
The abstract types and alias provenance are hardware-domain semantics. A new
Mono.Cecil/AsmResolver dependency would duplicate the existing metadata/decoder
and source-symbol integration without supplying those semantics. Existing graph
analysis stays in its established component; no replacement implementation or new
production dependency is required. FsCheck, Coverlet, Stryker and SharpFuzz remain
the established verification tools.

Primary references: [ECMA-335](https://ecma-international.org/publications-and-standards/standards/ecma-335/),
[runtime opcode metadata](https://github.com/dotnet/runtime/blob/main/src/coreclr/inc/opcode.def),
and [Microsoft opcode reference](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.emit.opcodes?view=net-10.0).
These define operand categories; they do not establish execution evidence.

Execution results and bounded alias acceptance will be recorded after local gates.

## Bound RAM alias implementation

Local uint[] references are one-bit null/bound tags; the evaluation stack retains
its fixed 32-bit representation. The array argument denotes the single non-null
physical RAM and cannot be reassigned. Integer bits can never fabricate reference
provenance. There is no pointer address encoding and no second memory allocation.

The abstract reference lattice contains null, bound RAM, and their union. Each
instruction entry holds immutable stack/local snapshots. Transfer functions clone
locals before writing; joins union reference states and reject incompatible scalar
categories. A widening join reprocesses successors, including loop backedges.
Combined arguments/locals are limited to 1024, alongside existing instruction and
stack bounds. Valid reference truth/equality, and cgt.un against definite null,
lower through identity tags. Native length arithmetic stays unsupported until
explicit conv.i4/u4 normalization.

Dereferences require definitely bound provenance. A nullable join followed by a
null-test guard may conservatively reject because this slice does not refine
local provenance along branch conditions. Rebinding the RAM argument, multiple
array origins, new arrays, general pointers and reference ordering remain outside
the profile. This is bounded hardware validation, not a general CLI verifier.

The working C# ScheduledArrayAliases.Update example writes through copied aliases
and reads through the original reference. Unit tests compare raw-IL aliases and
nullable branches with CLR execution, including repeated starts and backedge
widening. FsCheck uses replay 1145655947,296144285: 100 invalid-variable samples
and 60 alias-copy samples. Generated inputs are bounded to terminate independently.

## Library tradeoffs

| Choice | Rationale | Limitation |
| --- | --- | --- |
| Existing HLS InstructionControlFlow / QuikGraph | Preserve canonical successors and cycle analysis, without rebuilding the graph infrastructure. | Control-flow edges do not establish path feasibility or alias nullness. |
| BCL Queue, Dictionary and immutable entry snapshots | A finite, monotone worklist expresses the domain-specific provenance rules directly. | Bound resource growth explicitly; this implementation is sequential and ordinal. |
| Existing CIL decoder and Reflection.Emit fixtures | Reuse original method/IL provenance and construct negative cases C# cannot emit. | Dynamic fixtures are JIT test tools; Native AOT remains deferred. |
| FsCheck and SharpFuzz/AFL++ | Seeded generation/shrinking and maintained coverage-guided input campaigns. | Finite campaigns supplement operand-role oracles and CLR/HDL replay. |
| Coverlet and Stryker.NET | Reuse established coverage and mutation tooling. | Reflected fixture assemblies stay uninstrumented; large frontend instrumentation exclusions remain explicit. |

No production dependency is added. A metadata reader alone cannot supply the
hardware-specific immutable-RAM binding and nullness policy. Reusing the existing
libraries preserves the current .NET 10/PDB integration and avoids a parallel
assembly object model.

## Reproduction and remaining work

From an existing checkout on Windows, with the existing pinned tool environments:

```powershell
if (!(Test-Path -LiteralPath 'tests/SharpHDL.UnitTests' -PathType Container)) { throw 'Run from the existing checkout' }
dotnet test tests/SharpHDL.UnitTests -c Release -m:1 -nr:false -p:UseSharedCompilation=false --filter FullyQualifiedName!~Z80Tests --settings tests/SharpHDL.UnitTests/coverage.runsettings --collect:"XPlat Code Coverage"
wsl --exec bash tools/run-workpackages-linux.sh typed-alias
dotnet test tests/SharpHDL.VerificationTests -c Release -m:1 -nr:false -p:UseSharedCompilation=false
wsl --exec bash tools/run-verification-linux.sh
wsl --exec bash tools/run-scheduled-rtl.sh aliases
wsl --exec bash tools/run-scheduled-fuzz.sh 60
```

Run Stryker from the checked tests/SharpHDL.UnitTests directory with
stryker-typed-stack.json and a new output directory. On constrained/shared hosts,
use bounded processor counts, disabled build-server reuse and a .NET heap limit.
The real engineering/API documentation build is described in ENGINEERING_BUILD.md.

Highest-value next work: branch-sensitive null refinement; additional array-origin
and interface decisions; broader malformed metadata/typed-operand qualification;
sequential equivalence; and removal of legacy mutation-instrumentation exclusions.
No FPGA mapping/timing or exhaustive alias proof is claimed.

Item 1 local gate: 333 fast Windows tests pass (zero skips/failures, historical full Z80Tests excluded), followed by all 15 focused typed-stack cases including the added loop and 100 seeded invalid-variable samples. Both commands build with warnings as errors. Focused Coverlet class rates: {"type":"SharpHDL.CilCompiler","line":"0.9545","branch":"0.9"}. Two initial namespace-import build failures were corrected before these passing runs. Linux, mutation, fuzz and full documentation gates remain pending at this commit.

Intermediate execution: 347 fast xUnit tests on each OS and 62 native verifier regression tests on Windows (the corrected Linux gate is recorded below), zero failures/skips (full historical Z80Tests excluded). The native checks are existing equivalence regressions, not an alias-analysis proof. Typed validator main partial class: 98.48% line / 89.86% branch coverage; record/closure classes 100% line/branch. Coverage of reflected fixture/example bodies is intentionally excluded. Icarus, Verilator and generic Yosys checks pass both added designs: 454 alias-update replay edges and 73 nullable-comparison edges, ten manifests at /tmp/sharphdl-rtl-yRDqm1. The seven RAM transactions cover shared writes, capture, earlier writes surviving bounds faults and recovery; six nullable transactions cover reset-per-start behavior. No FPGA timing claim.

SharpFuzz 2.2.0/AFL++ 4.09c: 60 seconds, seed 261026, 574 executions, 72 corpus entries, zero saved crashes/hangs, /tmp/sharphdl-fuzz-9qXbmV. Three new raw seeds cover invalid variable indexes, numeric array fabrication and null aliases. Full source hashes, coverage scopes and actual gates are retained in verification/typed-alias-2026-10-28.json.

## Final verification and review

After deterministic boundary and exact-diagnostic additions, 373 fast tests pass on each OS, zero failed/skipped; 62 native verifier regression cases pass per OS, and four documentation tests pass on Windows. Final typed-validator main-class coverage is 100% line / 97.97% branch. Record/closure class rates are 100%/100%; this is not whole-compiler coverage or proof. Earlier 347/365-case and 98.48%-line results above are historical intermediate gates.

Stryker.NET 5.0.0 executed three distinct scopes. Initial full validator profile: 179 killed, three timed out, 49 survived and nine uncovered, raw score 75.83%. Boundary replay with coverage selection disabled: 65 killed / 21 survived, no timeouts/errors, 75.58% of that selected scope. Exact-diagnostic replay: 12 killed / zero survived/timeouts/errors, 100% of that selected scope. Do not average or present these as a new full-profile score. Per-ID observations and raw-report SHA256 values are in verification/typed-alias-mutation.json; raw reports remain local. After replays, 223 original IDs were killed, three timed out and 14 survived. Seven survivors are reviewed equivalent (redundant short-opcode selectors and an overwritten initializer); seven remain explicit test gaps (four diagnostic-string assertions, plus positive reference-pop, signed element-load and native conv.i4 variants). No mutant-equivalence solver proof is claimed. Eight validator mutants were compile-invalid and excluded; the full-project instrumenter excludes 2,029 compile-invalid variants, including large legacy methods. The full configuration now includes strengthened boundary tests for a future full rerun.

Review found that a later stack/lowerer rejection could mask deletion of an earlier typed check. Tests now require the actual bad store's IL offset, distinguish all array-store operand roles, exercise all long/short variable forms, and reject an excessive 33-slot peak even after later pops restore a valid return height. Existing lowerer defenses remain independent. Source review checked snapshot cloning, monotone joins/backedge reprocessing, numeric/reference separation, bounded native lengths, one-bit alias tags and repeated-start clearing. No production defect was left hidden by an equivalent-mutant classification.

The real StrictDoc/Sphinx/compiler-XML workflow passes 50 requirements (34 mapped / 16 unmapped), seven API assemblies and twelve engineering pages. Partial and historical requirement status is retained. CI stays disabled; Native AOT stays deferred; no performance speedup or full Z80/FPGA qualification is claimed.

Exit-code audit correction: the first Linux native-verifier launch exited 1 before running tests because an explicit temporary Z3 directory from the previous session no longer existed. Earlier wording that attributed 62 current native tests to Linux was premature; only the Windows run had passed then. The corrected Linux command uses the script's verified default cache rather than a stale path. Final execution status is recorded separately below and in the evidence JSON.


Corrected final Linux native gate: all 62 tests pass, zero failures/skips, exit code zero, verified cached Z3 4.12.2. Artifacts: /tmp/sharphdl-verification-tests-84823F. The failed initial launch is not counted as an executed test run. Final full documentation rebuild also passed all 50 requirements / 34 mappings / seven API assemblies / twelve engineering pages before this audit paragraph was refreshed.
