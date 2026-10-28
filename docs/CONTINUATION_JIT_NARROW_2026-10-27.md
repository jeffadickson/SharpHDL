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
