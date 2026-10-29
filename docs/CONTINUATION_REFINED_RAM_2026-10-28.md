# Refined RAM continuation

Baseline: clean main at cbd87819880adba35603f13ba7631ac5a4f252ea. Preserve CI off,
Native AOT deferred, existing history and the previous scope-specific evidence.

The first item closes seven retained test gaps with freshly compiled fixtures:
reference pop, signed element loads, signed native-length normalization and four
actionable diagnostic reasons. Tests compare valid operations to actual CLR
execution; rejected fixtures are not executed. Focused mutation replay targets
the original survivors before the validator implementation changes.

Library policy: reuse the existing BCL decoder/Reflection.Emit fixtures, HLS
control-flow analysis and QuikGraph integration, FsCheck, Coverlet, Stryker and
SharpFuzz. Null refinement and physical RAM binding policy require compiler-domain
logic; adding a metadata reader or a parallel graph implementation would not
supply those semantics. No production dependency change is planned.

Primary references: [CLI opcode definitions](https://github.com/dotnet/runtime/blob/main/src/coreclr/inc/opcode.def)
and [native array length](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.emit.opcodes.ldlen?view=net-10.0).
These define semantics; test execution and retained evidence qualify the bounded implementation.

Remaining work is selected from actual gaps: branch-sensitive null refinement,
bounded multiple physical RAM origins, and reliable external-tool qualification
diagnostics. General heap pointers, arbitrary CLR verification and whole sequential
proof remain separate requirements.

Item 1 local gate: all seven focused gap tests pass on Windows with warnings as errors and Coverlet collected under build/verification/alias-gaps. No production validator change is included in this item. The scoped Stryker replay is still running at this commit; no kill result is claimed yet.

Gap replay finished: all seven selected retained mutations killed, zero survivors/timeouts/runtime errors, Stryker.NET 5.0.0. Branch-refinement local gate: 18 focused CLR/Core tests pass with Coverlet collection after fixing predicate joins that initially lost widened candidate sets. This local commit covers direct/equality/Boolean/negated guards, stale Boolean/stack predicates, and identical successors. Broad Windows/Linux, HDL and fuzz gates remain pending for the combined implementation.

Multiple-origin local gate: 396 fast tests pass on Windows (zero skips), including
all five multi-RAM cases and the synthetic invalid-control source-map contract.
Independent guarded-rams profile: Icarus and Verilator each pass 183 replay edges;
Yosys generic synthesis passes. AFL++ 4.09c / SharpFuzz seed 261026 ran 60 seconds,
1,474 executions, zero saved crashes/hangs. Native Windows Z3 tests: 62 passed;
documentation parser tests: four passed. Linux and helper mutation gates are still
pending at this snapshot; no full Z80 vector rerun or FPGA timing is claimed.

Implementation policy: MemoryDepths/InitialMemories use original parameter indexes,
falling back to MemoryDepth for unspecified depths. InitialMemory remains supported
for exactly one RAM; ambiguous multi-RAM initialization and per-RAM options on the
legacy backend reject. Tags 1..8 identify physical resources; zero is null. Selected
bounds are checked before each access, and writes preceding a later fault persist.
The example includes capture, repeated starts, nullable joins and unequal RAM depths.
Cross-local relational facts, dynamic inter-parameter object aliasing, arbitrary
heap references and whole sequential equivalence remain unsupported/unverified.

Library tradeoffs (primary sources checked 2026-10-28):

* Keep BCL Reflection.Emit and the existing decoder: actual CLR execution supplies
  an independent instruction oracle without adding another metadata representation.
  [Ldlen](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.emit.opcodes.ldlen?view=net-10.0)
  returns native unsigned length, so the hardware validator retains its distinct
  category until explicit normalization. BCL APIs do not supply hardware alias policy.
* Keep existing QuikGraph/control-flow integration; the bounded fixed-point facts
  are compiler semantics. A second graph library would duplicate graph storage
  without implementing local-write invalidation or physical memory dispatch.
* Keep [Stryker.NET](https://stryker-mutator.io/docs/stryker-net/configuration/) for
  deliberate test challenges, with coverage analysis off in the focused helper run.
  Instrumentation exclusions and survivor dispositions are reported separately;
  a focused score cannot qualify the entire compiler.
* Keep [SharpFuzz/AFL++](https://github.com/metalnem/sharpfuzz) for reusable
  coverage-guided input generation. The custom harness only decodes bounded inputs
  and compares CLR/Core behavior. This finite Linux campaign is not a formal proof.
* Retain Coverlet, xUnit and the pinned native Z3/Yosys integration. No new NuGet
  production dependency is needed. Native asset validation uses BCL/shell checks
  and the existing verified cache; an extra process wrapper would not fix a stale path.

Final regression gates: 396 fast tests pass on each Windows/Linux platform (zero
failures/skips), and 62 native verification tests pass per platform. An initial
Linux run saw the new legacy-option assertion against a production assembly built
before that guard was added; the isolated final rebuild passes all 396 tests.
Windows documentation parser tests pass 4/4. Engineering/API build uses real
StrictDoc and warning-failing Sphinx: 53 requirements, 37 mapped, 16 unmapped,
seven API assemblies and thirteen engineering pages. Mapped does not mean closed.

Other open work addressed: setup-z3-linux.sh rejects stale explicit native asset
directories with status 2 and an actionable diagnostic; relative paths also reject
with status 2. tools/check-z3-paths-linux.sh reproduces both negative checks without
creating the missing target. Default-cache SHA validation and all 62 Linux native
verification tests pass. The helper does not repair invalid caller paths silently.

Reproduction (from an existing repository directory):

```text
dotnet test tests/SharpHDL.UnitTests -c Release -m:1 -nr:false -p:UseSharedCompilation=false -warnaserror --filter FullyQualifiedName!~Z80Tests --settings tests/SharpHDL.UnitTests/coverage.runsettings --collect:"XPlat Code Coverage"
bash tools/run-workpackages-linux.sh refined-ram-final
bash tools/run-verification-linux.sh
bash tools/check-z3-paths-linux.sh
bash tools/run-scheduled-rtl.sh guarded-rams
bash tools/run-scheduled-fuzz.sh 60
```

Run Stryker from tests/SharpHDL.UnitTests using stryker-refined-ram.json and
concurrency 1. The original seven-span gap configuration is baseline-specific:
use source revision 59425de to replay it; the validator moved in the next item.
Coverage inputs and scoped summaries are retained in docs/verification/refined-ram-coverage-*.json.
Reproduce summaries with the checked-in C# file-based app (JIT; AOT disabled):
`dotnet run tools/SummarizeRefinedRamCoverage.cs -- <existing-Cobertura-XML>`.
Raw local artifacts remain under build/verification and private /tmp directories.
No simulator performance optimization or new performance claim is included.

Follow-up priorities:

1. Resolve meaningful new helper mutation survivors with focused negative tests.
2. Extend guarded facts across copied locals with sound relational invalidation.
3. Specify explicit shared-resource alias groups before supporting parameter aliasing.
4. Extend bounded bit-vector equivalence to selected memory transitions with honest unknown results.
5. Qualify multi-memory reset/collision behavior across further HDL profiles and FPGA tools.

Measured coverage on the final 396-test regression (Windows and Linux agree for
these source scopes; compiler-generated method branches included):

| File | Covered lines | Covered branches |
| --- | ---: | ---: |
| ScheduledCilRefinedValidation | 102/102 | 215/228 (94.30%) |
| ScheduledCilReferenceFacts | 6/6 | 12/18 (66.67%) |
| ScheduledCilRamBindings | 17/17 | 35/38 (92.11%) |
| ScheduledCilStackValidation categories | 1/1 | 1/2 (50%) |
| ScheduledSequentialCil | 164/169 | 305/332 (91.87%) |

Collected production-assembly coverage: 69.92% line, Windows 56.98% branch / Linux
56.96% branch. This excludes test fixtures deliberately (instrumenting CIL fixtures
changes the tested program), shell scripts and unexecuted projects. Coverage alone
is not correctness. Earlier reports have different scopes and are not comparisons.
The initial broad source-map test exposed the new synthetic fault-state count;
the test now requires its explicit null source and exclusion from original IL offsets.

Seven-gap raw mutation summary is retained with SHA-256. Seven executable selected
mutants were killed; eight instrumentation compile exclusions and 251 ignored
entries in that source file were not executed. The archived configuration is
now docs/verification/stryker-alias-gaps-59425de.json; use the recorded revision.

Documentation parser regressions also pass 4/4 on Linux. CI permissions were
queried and remain disabled; no workflow was enabled or dispatched. Local item
commits: 59425de (seven gaps), 31e9a3b (null refinement), ca3d326 (multiple RAMs),
69d8970 (native asset diagnostics). The final evidence commit follows these items.
CIL-GAPS-001 is verified_bounded; CIL-NULL-001 and CIL-RAMS-001 remain partial
because the stated relational/general pointer and sequential-proof limitations
are not closed by this finite qualification. The existing requirements stay open
where prior evidence did not establish closure.

Helper Stryker campaign completed: 62 executable mutants, 37 killed, nine timeout,
16 survived; tool-reported score 74.19% counts timeout as detection. One selected
helper compile exclusion and two ignored entries are separate. Existing large
frontend Safe Mode exclusions are outside this scope. Timeouts have not been
individually adjudicated. This campaign is not a whole-validator mutation result.
The retained JSON includes every survivor location/replacement and a review:
redundant private admission/depth checks, actionable diagnostic text, empty/invalid
initialization dictionaries, endpoint depths, older stack provenance precision and
asymmetric optional-fact joins. These are new qualification limitations, not a
claim that the seven original executable mutation gaps remain open. No new test
or proof closure is claimed from a source-only survivor review.
