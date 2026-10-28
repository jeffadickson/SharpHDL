# Narrow storage and checked conversions

Starting revision: 2659a0be7c543cd7759ae6b66c8128dedb9ca3ca, clean main tracking
origin/main. CI remains disabled. Native AOT remains deferred.

## Item 1: narrow storage and ports

The scheduled frontend uses declared 8/16-bit local and captured-argument storage
for byte, sbyte, short, ushort and char. Stores discard upper bits; loads sign or
zero extend to the 32-bit CLI evaluation stack. Return ports use declared widths.
Int32/UInt32 and canonical Boolean behavior remain compatible. Wide, native,
floating, by-reference and exception-handler profiles remain unsupported.
Physical HDL ports carry fixed-width bit patterns; signed load behavior is
expressed by explicit extension operations inside the design.

Raw Reflection.Emit fixtures test implicit storage independently of explicit
conversion opcodes. CLR execution and modular arithmetic provide references.
Tests include argument capture, return widths, reset, boundaries, FsCheck replay
1145655947,296144285 (100 generated inputs), and unsupported storage diagnostics.
The storage RTL profile covers ten signed/unsigned/character local/argument
designs through Icarus, Verilator and generic Yosys checks. This is finite replay,
not a sequential proof or FPGA qualification.

## Library choices

Reuse BCL Reflection.Emit for precise executable IL fixtures, the existing typed
HLS/Core representation for storage and extension, FsCheck for reproducible
generated values, Coverlet for coverage, Stryker.NET for mutation and the existing
manifest-retaining HDL runner. Adding a conversion or numeric library would not
define CLI storage semantics and would add dependency/version cost without
removing domain-specific lowering. No new production dependency is needed.

Primary semantic references: Microsoft's [stloc documentation](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.emit.opcodes.stloc_s?view=net-10.0)
defines truncation on small local writes; [checked unsigned-source conversion](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.emit.opcodes.conv_ovf_i1_un)
defines overflow and source interpretation. Runtime differential checks accompany
these references; documentation alone is not execution evidence.

Execution results are recorded below after the local gates finish.

## Item 2: checked conversions

All twelve conv.ovf.i1/u1/i2/u2/i4/u4 forms, including .un, use explicit ranges.
Ordinary opcodes interpret signed Int32 stack values; .un interprets UInt32.
Overflow completes with done=1, busy=0, fault=1, conversion_overflow=1, timeout=0
and fault_il equal to the original conversion offset. The result stays cleared;
later instructions do not execute. Earlier RAM writes remain committed. Reset or
accepted start clears diagnostics. Watchdog expiry takes priority over execution.
Unchecked kernels expose zero diagnostic outputs without diagnostic registers.
These are added output ports; named-port consumers can leave them unconnected,
but positional HDL instantiations must update their port list.

The working CheckedStore C# example demonstrates partial writes and recovery.
The checked HDL dispatch fixture selects every admitted opcode and compares
successes and CLR OverflowException cases, including original IL offsets.
Native Z3 4.12.2 verifies only local range predicates for all 32-bit patterns;
deliberately inverted references require independently replayed counterexamples.
Reuse the pinned Z3 integration instead of adding a new solver binding: this
keeps runtime/native-version policy and unknown/unsupported handling centralized.
FsCheck and SharpFuzz/AFL++ retain their existing roles; no new dependency added.

| Facility | Choice and rationale | Tradeoff |
| --- | --- | --- |
| CIL fixture generation | BCL Reflection.Emit gives exact store/conversion opcodes and a real CLR oracle. | Dynamic fixture execution uses JIT; it is not Native AOT qualification. C# compilation alone can optimize away the opcode under test. |
| Generated properties | Existing FsCheck 3.3.3 provides seeded generation and shrinking. | Hedgehog is a reasonable alternative, but adding a second framework brings migration/dependency cost without improving these scalar properties. |
| Formal checks | Reuse pinned Microsoft.Z3 4.12.2 and the existing equivalence/replay integration. | Native asset/version qualification remains necessary; the current proof boundary is local combinational expressions. |
| Coverage-guided fuzzing | Existing SharpFuzz 2.2.0 / AFL++ 4.09c owns mutation and corpus management. | Linux/native instrumentation setup is required; finite campaigns do not establish exhaustive correctness. |
| Coverage and mutation | Existing Coverlet 10.1.0 and Stryker.NET 5.0.0 measure executed production code and challenge assertions. | Reflection fixtures must remain uninstrumented, and the large legacy frontend has instrumenter exclusions. |
| External HDL tools | Reuse the existing BCL Process runner with retained manifests. | CliWrap would not remove any CLI lowering semantics; its process API tradeoffs remain documented in WP-07. |

Selection references: [FsCheck properties/replay](https://fscheck.github.io/FsCheck/Properties.html),
[FsCheck generators and shrinking](https://fscheck.github.io/FsCheck/TestData.html),
and [Stryker's static-initialization limits](https://stryker-mutator.io/docs/mutation-testing-elements/static-mutants/).
These primary sources support the retained tool choices; the execution results
below establish compatibility of the pinned versions on this checkout.

## Verified results and reproduction

Windows and Linux each pass 319 fast xUnit cases and 62 native verifier cases,
zero failures/skips. Historical full Z80 vector qualification was excluded;
this request changes the compiler, not the Z80 implementation. Four documentation
tests pass on Windows. After strengthening direct narrow-return fixtures, all
31 storage/unchecked/checked focused cases pass again on both operating systems.

Coverlet reports 100% line and branch coverage for ScheduledCilScalarTypes.cs
(including its descriptor's load/store methods) and ScheduledCilCheckedConversions.cs.
This scope excludes reflected test/example bodies and does not establish whole
compiler correctness. Production assembly coverage and raw reports remain under
build/verification. Source hashes are retained in verification/storage-checked-2026-10-27.json.

Icarus 12, Verilator 5.020 and generic Yosys 0.33 checks pass for ten storage
designs (560 total replay edges), one checked dispatch (192 transactions, 1,349
edges), and one C# RAM/recovery design (eight transactions, 107 edges). Retained
artifacts: /tmp/sharphdl-rtl-75bMrU (50 manifests), /tmp/sharphdl-rtl-sOs2ju
(five), /tmp/sharphdl-rtl-GwmgOm (five). Generic synthesis checks do not establish
FPGA mapping/timing closure. Earlier aborted script execution is not counted.

Native checked-predicate records contain twelve actual UNSAT equivalences and
twelve SAT corrupt-reference cases with independently replayed counterexamples.
See [retained SMT and results](verification/checked-conversions/). Publication
normalizes trailing whitespace only; raw original files remain in
build/verification/checked-proof-records. These are local predicates, not whole
sequential proofs.

Reproduce from an existing repository directory with the pinned .NET 10 SDK:

```powershell
if (!(Test-Path -LiteralPath 'tests/SharpHDL.UnitTests' -PathType Container)) { throw 'Run from the existing SharpHDL checkout' }
dotnet test tests/SharpHDL.UnitTests -c Release -m:1 -nr:false -p:UseSharedCompilation=false --filter FullyQualifiedName!~Z80Tests --settings tests/SharpHDL.UnitTests/coverage.runsettings --collect:"XPlat Code Coverage"
dotnet test tests/SharpHDL.VerificationTests -c Release -m:1 -nr:false -p:UseSharedCompilation=false
wsl --exec bash tools/run-workpackages-linux.sh storage-checked
wsl --exec bash tools/run-verification-linux.sh
wsl --exec bash tools/run-scheduled-rtl.sh storage
wsl --exec bash tools/run-scheduled-rtl.sh checked
wsl --exec bash tools/run-scheduled-fuzz.sh 60
```

Run the configured Stryker campaign from tests/SharpHDL.UnitTests using
../../build/tools/dotnet-stryker.exe --config-file stryker-scalar-checked.json
--concurrency 1 --output ../../build/verification/scalar-checked-mutation-new.
Check the output parent exists and select a new output directory. Use bounded
processor/memory settings on shared machines. No performance speedup is claimed.

The real StrictDoc/Sphinx/compiler-XML build passes: 48 requirements, 32 mapped,
16 explicitly unmapped, seven API assemblies and eleven engineering pages.
Partial/historical/draft statuses remain intact. Native AOT remains deferred;
GitHub Actions stays disabled and no remote workflow was dispatched.

Highest-value next work: typed stack/alias validation for array references;
broader sequential equivalence; checked arithmetic with explicit fault policy;
64-bit/native/floating profile decisions; and removal of legacy frontend mutation
instrumentation barriers. None is claimed implemented by this slice.

Final source review checked extension/truncation at each storage boundary,
ordinary versus .un interpretation, valid/failing conversion guards, watchdog
priority, sticky diagnostics and reset/start clearing, return widths, RAM partial
writes, and source-offset provenance. Review corrections include stale architecture
and handoff restrictions, reflected-fixture coverage instrumentation, and a
mutation finding: process-static compiled graphs could mask later lowering
mutations. Checked tests now compile their design during the assertion run and
validate diagnostic messages/parameter names. The first 84/100 mutation result
is historical intermediate evidence, not the final qualification result.

Item 1 local-commit gate: 302 fast xUnit tests pass on Windows, zero skipped (historical Z80Tests excluded). Coverlet measures the scalar descriptor/load/store helper at 100% line and branch coverage; frontend file 96.35% line / 84.25% branch. Test-linked reflection fixtures are intentionally uninstrumented because collector calls alter their CIL. The first instrumented-example run failed one case; the corrected run passes all 302. HDL, bounded mutation and finite fuzz campaigns are still running and are not claimed passed at this commit.

Final SharpFuzz/AFL++ campaign: 60 seconds, seed 261026, 2,045 executions, 175 corpus entries, zero saved crashes/hangs. It includes narrow storage and all twelve checked operations with an independent range oracle; six new boundary seeds are version-controlled. Artifacts: /tmp/sharphdl-fuzz-V2U05u. This finite campaign supplements, rather than replaces, the CLR/HDL and native local checks.

After correcting the compiled-design cache, all 319 fast tests pass again on each OS (Windows 44 seconds; Linux 30 seconds, not performance benchmarks). Fresh Coverlet production-helper coverage remains 100% line / 100% branch. The main CilCompiler partial class in ScheduledSequentialCil.cs measures 96.68% line / 84.91% branch; this is a separate scope from the helper metrics.

Final Stryker.NET 5.0.0 helper campaign: 95 killed, five survived, zero timeouts/runtime errors, zero uncovered eligible mutants; raw score 95% (not adjusted). Checked helper: 54 killed / two survived / four compile-invalid / one ignored. Scalar helper: 41 killed / three survived / one ignored. Review finds the five survivors equivalent under the bounded width contracts: two remove redundant width-32 identity/true shortcuts, and three toggle signedness of 32-bit storage where extension to 32 bits is the identity. This is a source-review disposition, not a separate solver proof. See verification/storage-checked-mutation.json for exact IDs/replacements and rationale. The campaign excludes 2,042 project-wide compile-invalid variants, 8,297 outside-filter variants and two block-filter variants. Large frontend instrumentation remains unqualified. The raw 3.7 MB report stays local with its SHA256 in the summary. The strengthened tests kill the eleven additional non-equivalent survivors from the intermediate campaign.
