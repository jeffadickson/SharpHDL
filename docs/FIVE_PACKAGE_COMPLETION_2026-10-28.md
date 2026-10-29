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
