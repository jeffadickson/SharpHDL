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
