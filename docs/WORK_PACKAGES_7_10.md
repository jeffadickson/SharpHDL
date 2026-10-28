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

Other package results will be added only after implementation and execution.
