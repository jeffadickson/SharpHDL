# Work packages 7–10 and scheduling continuation

Baseline: clean main b2341dd, 2026-10-27. Preserve CI-off policy, commit each of the
five packages locally after checks, push only after final qualification.

## WP-07 — retained, cancellable external tools

Acceptance: literal argv, concurrent bounded raw stdout/stderr, closed stdin,
distinct missing/start/nonzero/timeout/cancel/truncation outcomes, executable hash,
working directory/timing/platform provenance, no overwritten evidence, ordinary
descendant termination, and real HDL-runner integration. IO/cleanup errors propagate.

Library comparison: [CliWrap](https://github.com/Tyrrrz/CliWrap) supplies fluent
asynchronous pipes and graceful/hard cancellation. The .NET BCL already supplies
ArgumentList, asynchronous pipe reads, WaitForExitAsync and Kill(entireProcessTree).
The required policy is hard cancellation, raw bounded logs and SharpHDL manifests;
those policies still need domain glue with CliWrap. Select BCL here, adding no
dependency. NativeProcess is the unrelated managed hardware-method runner, not an
OS subprocess abstraction. Existing shell/Python workflows remain historical;
the scheduled RTL tier now routes all 49 HDL invocations through this shared runner.

The runner is not process isolation: detached/reparented daemons cannot be reliably
owned by BCL process-tree cancellation. Do not infer job-object/container guarantees.
Each artifact directory retains stdout.bin, stderr.bin and manifest.json; stream
caps are bytes, a cap violation is failure and manifests record arguments verbatim.
Native AOT CLI qualification is recorded separately from managed execution.

Executed: nine new Windows tests pass (including 20 fixed-seed FsCheck argv cases),
then the integrated current fast suite passes 269/269 on Windows (24 seconds) and
Linux (22 seconds), zero failed/skipped. Logs: `wp07-windows-complete.log`,
`wp07-linux-manifest.log`. The first Linux run found a zero-length PID publication
race in the fixture; atomic rename fixed it, and the rerun passes. The ordinary
parent/child cancellation assertion executes on both systems.

Coverlet 10.1.0 Windows coverage: async runner 92.72% line / 93.18% branch;
lookup/validation helper 100% / 77.77%; pipe pump 100% / 100%. Generated JSON context
coverage is separate. Start-returned-false, cancellation/exit races and some IO/error
paths are not measured. The initial bounded validation/exit-status mutation run
kills 27, survives six, leaves four uncovered (72.97%). Reviewed boundary/existing-
directory tests improve this to 32 killed, three generic message-string survivors,
two uncovered, zero timeouts/errors (86.49% of 37 selected testable/uncovered
variants). One compile-invalid and 97 filtered variants are excluded. This is
explicitly not a whole-runner score. Config: `stryker-tool-runner.json`; raw final
report: `build/verification/wp07-mutation-reviewed/reports/mutation-report.json`.

All eleven designs pass Icarus/Verilator and five pass generic Yosys checks through
49 retained invocation manifests at `/tmp/sharphdl-rtl-UjLYh4/runs`.
Log: `wp07-rtl.log`. Budgets are now also explicit manifest fields and are checked
by managed tests and the final Native AOT smoke. Linux SDK 10.0.402 successfully
publishes the CLI with PublishAot=true and executes `/usr/bin/printf` preserving a
literal Unicode argument; final evidence is `/tmp/sharphdl-tool-native-ACmERS/evidence-final`.
This qualifies functionality on Linux x64, not Windows AOT or a performance claim.

## WP-08 — bounded bit-vector equivalence

Acceptance: real UNSAT/SAT/Unknown outcomes, complete admission checks before
native context creation, replayed counterexamples, retained SMT-LIB/version/input
valuation, bounded Core replay and independent Yosys validation. Profile: 1..256
nodes/graph, 1..32-bit values, at most 128 shared input bits and matching unique
1..64-character ASCII identifiers. No state boundaries, registers, RAM, ROM or DSP
primitive, even in unused nodes. This proves combinational output equality only;
it does not establish sequential equivalence, pin timing or FPGA readiness.

Library choice: adopt the already cached official **Microsoft.Z3 4.12.2** in the
separate Verification project. Typed native AST construction avoids a new SMT
parser and preserves modular/signed bit-vector operators. Official NuGet release
cadence is older than upstream and the package omits Linux native assets; do not
assume netstandard compatibility means native availability. The Linux setup helper
restores the matching official x64/glibc-2.31 release into a private cache and checks
archive SHA256 `a198851a7403d8b25bab920bd4a4792efe1af5c28a5a932d840af0472d3a83eb`
and libz3 SHA256 `b6cc142a234129de3e8aab68439d71751f16885e27aad4af962836b32d8b2980`.
These were computed from the primary HTTPS release, not asserted vendor signatures.
Native version mismatch/unavailability never means Equivalent. No community fork
or arbitrary system libz3 is silently substituted. Verification Native AOT is not
qualified. Sources: [Z3 API](https://z3prover.github.io/api/html/),
[official package](https://www.nuget.org/packages/Microsoft.Z3/4.12.2),
[Linux packaging issue](https://github.com/Z3Prover/z3/issues/6822),
[pinned release](https://github.com/Z3Prover/z3/releases/tag/z3-4.12.2).

The Windows/Linux initial verifier suites pass 49/49; the expanded suites pass
52/52 (Windows 11 seconds, Linux 10 seconds), zero failed/skipped. They include
180 fixed-seed FsCheck name/arithmetic cases, a real native resource-limit Unknown,
unsupported/state/interface/budget negatives, one-bit/32-bit and huge shift
boundaries, identity extension, and replay allocation under 32 KiB for a two-node
million-bit-count graph. Bounding Core shifts fixes an unbounded host-allocation
defect without changing fixed-width results; no speedup is claimed.
Logs: `wp08-windows-reviewed.log`, `wp08-linux-reviewed.log` and final reruns.

Z3 and Yosys independently agree on **44** free-input qualification cases: all 22
operation/boundary variants and 22 corrupted complete two-bit truth tables. Each
corruption has exactly one witness, x=3/y=1, checked from both solver outputs.
The first Yosys run exposed binary WaveJSON decoding in the harness; it was fixed,
not misreported as an HDL defect. Artifacts: `/tmp/sharphdl-equivalence-7lUenZ`,
log `wp08-equivalence-linux-reviewed.log`. Windows explicitly ran Z3-only mode;
Yosys qualification is Linux. Artifacts retain SMT, actual native version, models,
Verilog and bounded Yosys logs/manifests. No proof certificate checker is claimed.

Stryker 5.0.0 encoder/outcome scope: 21 killed, eight survivors, one uncovered,
zero timeouts/errors: 70.00% of 30 selected testable/uncovered variants. Twelve
compile-invalid and 138 filtered variants are excluded. One survivor is generic
diagnostic text. Seven are semantic-equivalent shifts/identity extractions and
zero-bit extensions; a separate native test proves their equivalence over every
width 1..32 and all allowed 32-bit nonnegative offsets using 32 actual UNSAT checks.
The raw mutation score is not inflated by those dispositions. The unreachable
default encoder diagnostic stays uncovered. Separate left-shift replay mutation
scope kills five of six (83.33%), with the width-equality boundary survivor
equivalent after masking; no timeout/error. Its initial analysis failed on a
transitive project reference; explicit test references corrected discovery.
Configs/raw reports: `tests/SharpHDL.VerificationTests/stryker-*.json`,
`build/verification/wp08-mutation` and `wp08-replay-mutation-qualified`.

Extended SharpFuzz/AFL++ campaign: 60 seconds, seed 261026, 2,947 executions,
182 corpus entries, 3.06% host bitmap occupancy, zero saved crashes/hangs at
`/tmp/sharphdl-fuzz-cptl4b`; log `wp08-fuzz.log`. It checks profile rejection and
symbolic commutativity in addition to the prior campaigns. A budget-limited Unknown
remains a nonproof result; this campaign does not claim every input was proved.

Reproduce on Linux with a checked repository directory: source
`tools/setup-z3-linux.sh`, then run `tools/run-verification-linux.sh` and
`tools/run-equivalence-linux.sh`. An existing matching native directory can be
selected through SHARPHDL_Z3_NATIVE_DIR; mismatching files fail checksum checks.
On Windows use `dotnet test tests/SharpHDL.VerificationTests -c Release`; the
matching native package asset is restored by NuGet.

## Automatic regions and iterative arithmetic (completed bounded slice)

Source-order register RAW/WAR/WAW and conservative physical-memory alias analysis
now build deterministic operation regions. Distinct constant addresses may be
disjoint; unknown addresses alias. Ordinal ready-operation selection reserves all
latency phases. An independent assignment validator and the lowerer's real port
and reservation checks reject conflicting schedules. A registered 2..32-bit
shift-add multiplier samples operands once and completes after Width edges with
initiation interval Width. It emits no multiply node. ScheduledIterativeBanks
exercises two banks and a shared eight-edge unit in a twenty-edge transaction.

Library choice: reuse BCL collections and the existing typed HLS representation.
Source-order hazards are a bounded forward DAG, so a graph library or Z3 dependency
would add deployment and abstraction cost without solving an unmet requirement.
This greedy scheduler is deterministic, not an optimality solver. Regions are
straight-line and bounded to 128 operations / 4095 edges; CIL extraction, dynamic
alias proofs, pipelined initiation intervals and loop scheduling remain future work.

Verification: fifteen new region cases include FsCheck generated register programs
(100 cases) and independent arithmetic/reset checks (50 cases), replay seed
1145655947,296144285. The current fast suite passes 286/286 on both Windows and
Linux, zero failures/skips; this count also includes two baseline-observation tests.
Historical expensive Z80Tests are excluded; Z80ProgramOracleTests remain included.
Logs: build/verification/region-windows-accepted.log and region-linux-final.log.
The twelve-design Icarus/Verilator campaign and six generic Yosys checks pass;
retained artifacts /tmp/sharphdl-rtl-vPyatv include 54 process manifests. The new
example checks 714 edges across reset phases and changing sampled inputs.

Focused Stryker hazard/independent-validation scope finishes at 41 killed, three
generic-diagnostic survivors, zero uncovered/timeouts/errors: 93.18% of 44 selected
variants. Initial runs exposed bounds and mixed-latency gaps and one timeout;
the final bounded implementation and tests resolve those behavioral gaps. This
is not whole-planner mutation coverage. Config: stryker-regions.json; final report
build/verification/region-mutation-final. SharpFuzz/AFL++ ran 60 seconds, seed
261026: 2,461 executions, 154 corpus entries, 3.08% bitmap occupancy, no saved
crashes/hangs (/tmp/sharphdl-fuzz-Z6dtyv). Earlier focused coverage measured
ScheduledRegionPlan 100% line / 92.85% branch and ScheduledOperationBlock 91.66%
line / 100% branch; partial builder classes and unexecuted diagnostics remain
outside those class-specific figures. No FPGA timing or full ISA claim follows.
