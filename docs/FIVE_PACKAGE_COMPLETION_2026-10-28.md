# Five-package completion

Baseline: clean main d990139. CI remains disabled, Native AOT deferred. Each package
gets its own tested local commit; the final push follows verification. Requirements
RAM-QUAL-001, RAM-REL-001, RAM-SHARE-001, RAM-STEP-001 and RAM-HDL-001 define the bounded
acceptance contracts. Shared bindings are explicit; solver proof is limited to its
admitted transition profile. FPGA timing requires real target-tool evidence.

Library decisions: retain BCL Reflection.Emit/BigInteger for fixtures and bounded
alias sets, existing QuikGraph CFG integration, Coverlet/xUnit/FsCheck, Stryker.NET,
SharpFuzz and native Microsoft.Z3 4.12.2. A new metadata/graph wrapper would not
provide hardware alias semantics. Z3 bit vectors and finite memory expansion reuse
the pinned integration and allow replay; no second solver dependency is needed.
Primary references checked: [Stryker timeout configuration](https://stryker-mutator.io/docs/stryker-net/configuration/)
and [Z3 .NET context APIs](https://z3prover.github.io/api/html/class_microsoft_1_1_z3_1_1_context.html).
Timeout is not treated as an adjudicated kill. Finite HDL replay is not formal proof.

Package 1 local gate: five new qualification cases pass (20 combined qualification/refinement/relational tests). Initial fixture mistakes (memory metadata property and depth/initializer mismatch) were corrected before this gate. Mutation replay follows with a larger timeout budget; no new score is claimed yet.

Package 2 local gate: four new relational cases plus eleven existing guard cases pass within the combined 20-test gate. Relations use bounded BigInteger sets, intersect at joins, detach writes and refine current equal locals only. Positive copy guards execute against CLR/Core on repeated starts; overwritten/one-path relations reject.

Package 3 local gate: 18 combined shared-binding, distinct-memory, relation and qualification cases pass with warnings as errors/Coverlet. Explicit RamAliases resolves canonical chains; cycles/scalar targets and conflicting depths/initial contents reject. Shared and distinct CLR objects agree with Core outputs, reference equality and cross-parameter writes. Canonical metadata is inherited unless an alias explicitly supplies matching values.

Package 4 local gate: seven native Z3 tests pass. Identity and structurally different NOT/XOR write transitions prove equivalent; changed writes and write-first outputs yield original-Core-replayed SAT witnesses; resource budget one returns real Unknown. Exhaustive independent array reference: 512 tiny transition cases including disabled and out-of-range writes. Retained SMT streams separate checks with reset. Admission is one/two RAMs, widths/depths 1..8, at most 64 RAM-state bits; expanded graph/input budgets also apply. Matching initialization is required, but every admitted pre-edge cell is universally symbolic. Registers and whole-machine/multi-edge equivalence remain unsupported.

Qualification follow-up: first larger-budget replay tested 62 helper mutants:
51 killed, three timed out, eight survived. All three timeouts are address-width
loop mutations: signed/unsigned right shift cannot reach depth 65536 from 1, and
removing width++ leaves 2<65536 forever. The timeout behavior is expected from
these nonterminating mutated loops; it is not counted as an ordinary killed test.
Two survivors were the private count guard already enforced by public admission;
that duplicate guard was removed. Six predicate survivors prompted five additional
positive/negative cases, including a safe asymmetric join that reaches the join
rather than rejecting earlier. All 31 focused cases pass. Final fact/relation
mutation replay is separate; scores from different source scopes are not combined.

Package 5 local gate: six collision/shared binding tests pass. Independent RTL
profile passes five designs in Icarus and Verilator: collision Auto 99 edges,
collision XilinxBlock 99, shared RAM 71, distinct RAM 75, guarded multi-RAM 183
(527 edges per simulator). Five generic Yosys checks pass. Independent procedural
array RTL proves equal over the three-step reset sequence in Yosys SAT; an explicit
write-first defect yields a retained WaveJSON counterexample. This bounded check
uses initialized cells and reset, not the arbitrary-prestate Z3 contract.
Yosys synth_xilinx -family xc7 passes for Auto and XilinxBlock; the latter maps to
two RAMB18E1 cells. Placement, routing and device timing remain unverified:
nextpnr-ice40/nextpnr-xilinx/Vivado/Quartus are absent. No target timing readiness
claim is made. Raw tool manifests and source vectors are under the retained
/tmp/sharphdl-rtl-hugkNM campaign. The profile tests reset-time write suppression,
RAM retention, non-power-of-two bounds, read-before-write and physical identity.

Further verification strengthens the formal cone checks: a last-cell-only write
defect, an unread second RAM, unaddressable cells, excess state/missing binding,
maximum admitted 64-bit RAM state and two outputs with only one difference. The
solver wrapper compares all post-cells, including cells not reachable by a narrow
address port. Proof concerns Core snapshots; physical port scheduling/inference
qualification remains a separate lowerer/HDL responsibility.

The shared-origin SharpFuzz harness compares actual shared/distinct CLR arrays to
Core for arbitrary uint words and valid/invalid addresses. AFL++ 4.09c, seed 261026,
60 seconds, 518 executions: zero saved crashes or hangs. Existing FsCheck regression
properties run with the repository's retained replay seed; no second property
framework was needed for this bounded slice. The tiny independent memory sweep
covers all 512 state/address/data/enable combinations. No speedup is claimed.

Fresh regression before the last boundary additions: 416 fast and 72 native tests
pass on each platform; documentation parser tests pass 4/4 per OS. Reusing a proof
directory caused ten expected CreateNew IO failures in an intermediate Windows
native run; the fresh-directory rerun passes 72/72. Existing evidence is retained.
Final regression with all strengthened cases follows in separate artifact roots.
The engineering/API build passes real StrictDoc/Sphinx: 58 requirements, 42 mapped,
16 unmapped, seven API assemblies, fourteen engineering pages. Mapping is not
closure; legacy undocumented APIs and offline Mermaid rendering remain limitations.

## Final results and acceptance

All five packages meet their declared bounded acceptance contracts. Final gates:
420 fast tests and 74 native verification tests pass on Windows and Linux; four
documentation tests pass per OS, zero skipped. Full historical Z80 vector campaigns
were not rerun. CI remains disabled and Native AOT deferred. No performance claim.
The per-item local commits are d2fe9e4, e875def, c2436ec, ea4ca49 and 9a05e25;
0208ae4 and 1a27c1a strengthen the qualification. Final evidence commits follow.

Mutation evidence is deliberately separate:

* RAM/reference replay: 51 killed, three width-loop timeouts, eight survivors.
  The duplicate count guard was removed; predicate follow-up closes its six gaps.
* Reference facts: all 18 mutants killed in the combined facts/relation execution.
* Relation helper: 18 killed, one timeout, three survivors; two shift-direction
  survivors are killed in the final two-mutant replay after testing reciprocal
  relations through self-copy. The remaining i<=length iteration cannot access
  the array: membership bits are strictly below length, so its body is false at
  i==length. This is a source-invariant equivalence review, not a solver proof.
  The i-- mutation takes a pathological negative-index loop until integer wrap;
  its timeout is expected and retained separately, not called an ordinary kill.
* Memory transition critical scope: 14 killed/three survived, then all five focused
  replay mutants killed after maximum-state, multiple-output and unequal write
  address-width cases. No combined/full-file score is manufactured.

File-wide instrumenter CompileError/Ignored counts remain in the JSON summaries;
these are not executed tests. Main frontend CS0165 Safe Mode exclusions persist.
The final replays are not replacements for a future broad compiler mutation run.

New source coverage (Windows scopes; Linux counts are separately retained):

| Source | Lines | Branches |
| --- | ---: | ---: |
| Refined stack validator | 105/105 | 226/240 |
| Equality relation helper | 7/7 | 8/8 |
| Shared binding planner | 19/19 | 25/26 |
| Memory transition checker, native suite | 72/75 | 86/112 |

Counts include compiler-generated method branch counters and unique source lines.
Raw Cobertura digests and collection-wide rates are in five-coverage-*.json; the
fast/native scopes differ and must not be averaged. Test CIL fixtures deliberately
remain uninstrumented, and shell/HDL tooling is outside managed coverage. Unexecuted
exception/replay-failure and unsupported-profile arms remain limitations.

The formal profile uses 1..8-bit Core RAM words. It does not yet prove the 32-bit
scheduled CIL RAM examples: those have independent CLR/Core/HDL replay and synthesis
qualification. Pre-edge Core reads and one synchronous write interface are modeled;
physical port scheduling and inference are separate responsibilities. FPGA evidence
is xc7 synthesis/resource mapping only, without device placement, routing or timing.

## Reproduction and continuation

From an existing checkout, with the pinned .NET/native/documentation tools:

```text
dotnet test tests/SharpHDL.UnitTests -c Release -m:1 -nr:false -p:UseSharedCompilation=false -warnaserror --filter FullyQualifiedName!~Z80Tests --settings tests/SharpHDL.UnitTests/coverage.runsettings --collect:"XPlat Code Coverage"
bash tools/run-workpackages-linux.sh five-complete
bash tools/run-verification-linux.sh
bash tools/run-scheduled-rtl.sh memory-qualification
bash tools/qualify-memory-hardware-linux.sh <existing-absolute-retained-RTL-campaign>
bash tools/run-scheduled-fuzz.sh 60
```

Use a fresh SHARPHDL_PROOF_EVIDENCE directory for every native run; existing proof
files reject overwrite. Memory SMT/witness files and the independent Yosys WaveJSON
counterexample are version-controlled under docs/verification/five-memory-proofs
and five-yosys-counterexample.json. Raw campaigns remain local. Stryker configurations
under the two test projects describe whole-helper and focused replay scopes; spans
are tied to the retained source hashes. Reproduce summary JSON with the checked-in
JIT C# tools SummarizeMutationEvidence.cs and SummarizeRefinedRamCoverage.cs.

Highest-value next work: widen transition proof to 32-bit CIL RAM/control state;
qualify a real FPGA part/clock/pin constraint set through place/route; remove broad
frontend mutation instrumentation exclusions; refresh full Z80 qualification; and
complete the 16 unmapped requirements plus legacy API/offline documentation gaps.

Example binding (the source is linked by the replay demo, unit suite and fuzz tool):

```csharp
var compiled = CilCompiler.BuildScheduledSequential(
    typeof(ScheduledSharedRam).GetMethod(nameof(ScheduledSharedRam.Update))!,
    "Shared", new SequentialOptions {
        MemoryDepth = 3,
        RamAliases = new Dictionary<int, int> { [1] = 0 }
    });
```

`ramstateM_C` in a solver witness means cell C in RAM M, with RAM names sorted
ordinally. Original input names remain in the same valuation. Final documentation
rebuild preserves all five new manually reviewed bounded statuses. CLI rejection
of an extra RTL-profile argument also passed and advertises memory-qualification.
