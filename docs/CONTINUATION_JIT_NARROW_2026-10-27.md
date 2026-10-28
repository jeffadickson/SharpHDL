# Managed execution and narrow CIL continuation

Baseline: clean main c5f8b4aec38649be0e4a78b3b193f9914b1678e9. Native AOT is
explicitly deferred by the user. CI remains disabled. Commit each coherent slice
locally after verification; push after final checks.

## Managed defaults

The controlled benchmark profile defaults to four JIT cases. Native AOT stays
available only with --qualified-baseline --include-native-aot; this continuation
does not run or further qualify it. The script defaults to JIT and accepts the
same explicit optional switch. Historical eight-case measurements remain evidence
of their original scope, not a new requirement on ordinary builds or development.

Reuse BenchmarkDotNet's existing job/toolchain support and its argument parser.
A small BCL-only admission layer handles SharpHDL's two switches, preserving all
remaining arguments and rejecting ambiguous controls. Adding System.CommandLine
would duplicate the downstream parser for this narrow seam. No new production
dependency is needed. Primary guidance:
https://benchmarkdotnet.org/articles/configs/toolchains.html (host/worker separation
and explicit runtime selection).

Verification: eight benchmark-project tests pass (four existing workload cases,
four launcher cases), zero failures/skips. BaselineRunOptions coverage is 100%
line and 100% branch. Stryker 5.0.0 selects 33 variants: 29 killed, four survivors,
zero uncovered/timeouts/errors (87.88%); two compile-invalid and 26 filtered
variants excluded. Three survivors alter generic diagnostic text; removing the
explicit null guard still throws ArgumentNullException from LINQ. Raw reports
remain build/verification/aot-deferred-mutation; no adjusted score is claimed.

An actual default invocation executes exactly four JIT measurements and no AOT
job: /tmp/sharphdl-baseline-yPpMFk,
build/verification/jit-default-baseline.log. This run qualifies job selection,
not a new performance improvement; shared-host timing remains noisy. Native AOT
was not invoked. Historical native measurements are preserved separately.

## Unchecked narrow CIL conversions

Acceptance: conv.i1/u1/i2/u2 truncate to 8/16 bits, then explicitly sign/zero extend
back to the 32-bit CIL stack. Preserve real instruction states and captured
arguments. Reject checked, wide, floating/native conversions and narrow
locals/ports; no silent fallback. The mixed C# example is in
examples/ScheduledNarrowCil.cs; `bash tools/run-scheduled-rtl.sh narrow` regenerates
and independently checks four raw-IL designs and that example. The default full
HDL suite also includes these designs.

Library choice: reuse the existing decoder, BCL Reflection.Emit raw fixtures and
actual CLR delegates, plus the existing typed Core SignExtend operation. A second
IL parser (such as Mono.Cecil) is unnecessary for this loaded-method slice and
would add a separate decoding contract. CLR conversion semantics are checked
against [Microsoft's opcode documentation](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.emit.opcodes.conv_i1?view=net-6.0).
FsCheck supplies 100 reproducible full-width inputs (replay 1145655947,296144285),
SharpFuzz/AFL++ supplies coverage-guided inputs, and existing native Z3 checks the
conversion expressions against independent mask/subtract logic over all 32-bit
valuations. No new production dependency or custom test-generation engine.

The local proofs explicitly project only a conversion cone with no state boundary;
they do not prove the whole scheduled controller. Four correct cones return actual
UNSAT and four XOR-corrupted references return SAT with independently replayed
counterexamples. Eight new compiler tests include CLR/arithmetic/legacy/Core
comparisons, high discarded bits, sign thresholds, input capture while busy,
repeat calls, malformed stacks and rejection of adjacent unsupported opcodes.

Native AOT remains deferred; native Z3, Icarus, Verilator and Yosys are independent
verification tools and are still used. Existing historical eight-case AOT numbers
are not reclassified as current JIT results.

Final compiler verification: 296 fast xUnit cases pass on Windows and Linux;
60 native Z3 cases pass on each. Eight benchmark/profile cases pass on Windows.
No failures/skips in these gates. Full historical Z80Tests remain excluded, with
seven program-oracle cases retained. Changed managed projects build with warnings
as errors; no new Native AOT build is invoked.

Five new designs pass actual Icarus 12.0 and Verilator 5.020 replay plus Yosys 0.33
generic checks, retaining 25 tool manifests at /tmp/sharphdl-rtl-6EPeQw. Four raw
opcodes each cover 81 edges; composed C# covers 305 edges (629 total). No FPGA
mapping/timing claim. The default suite now includes seventeen designs, but only
the five added designs were rerun in HDL tools during this slice.

Coverage: ScheduledClrNarrow 88.88% line / 90% branch; ScheduledValue.SignExtend
100% line / 100% branch. Defensive impossible-width/default-opcode paths remain
uncovered. Focused helper mutation: 11 killed, three uncovered, zero survivors/
timeouts/errors (78.57%). Stryker cannot instrument the existing full frontend
method safely: 608 BuildScheduledSequential variants are compile-invalid, with
additional excluded legacy-method failures. Do not interpret those exclusions
as detected defects or full-front-end mutation coverage. Source span filters in
the checked config identify the current requested gates; Safe Mode exclusions
remain an explicit tooling limitation. SignExtend API mutation kills all ten
selected variants (100%), zero survivors/uncovered/timeouts/errors. Broad project
compile-invalid/filtered counts are retained in raw JSON, not folded into scores.

SharpFuzz 2.2.0 / AFL++ 4.09c, 60 seconds, seed 261026: 1,104 executions, 123 corpus
entries, zero saved crashes/hangs, /tmp/sharphdl-fuzz-CmNZII. Three new byte seeds
exercise signed-eight, signed-sixteen and unsigned-sixteen boundaries. Raw IL
fixtures, cached immutable designs and an independent arithmetic oracle supply
integration glue; the established engine owns coverage-guided generation.

[Retained conversion proofs](verification/narrow-conversions/) contain eight SMT2
files and eight actual native outcomes/models (four UNSAT, four replayed SAT).
Raw proof bytes remain in build/verification/narrow-proof-records; published copies
normalize only trailing whitespace. Set SHARPHDL_PROOF_EVIDENCE to a new existing
empty directory to retain another run; existing filenames fail rather than
being overwritten. The Linux verifier driver provisions its own new directory.

Remaining work: narrow locals/ports, checked conversions, wide/native/floating
values, sequential equivalence, CIL-to-region extraction and pipelined operators.
Current local expression proofs do not close those requirements. Native AOT stays
deferred; CI remains disabled.

Final real documentation build passes with warnings fatal: 45 requirements,
29 mapped and 16 explicitly unmapped; seven API assemblies and ten engineering
pages. Existing partial, historical and inferred/draft statuses remain open.
Machine-readable evidence: verification/jit-narrow-2026-10-27.json. No GitHub
workflow was enabled or dispatched; these are local execution results.
