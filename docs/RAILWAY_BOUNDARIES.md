# Railway build and command boundaries

This plan implements audit steps 1–3 against the application boundaries.
Preserve the documented tool and solver statuses and reuse the current BCL
and subprocess runner. The audit compared System.CommandLine for input parsing
and FluentResults for composition. The small existing argument surfaces and
domain-specific outcomes favor retaining the existing dependencies.

1. Exercise the benchmark pipeline with a failing stub and the fuzz scan with
   clean, missing and populated evidence. Preserve failures through shell wrappers.
2. Exercise ToolCli with malformed input and a bare name missing from PATH.
   Exercise a real subprocess with an injected failing output sink and wait
   for an explicit ready signal in the caller cancellation test.
3. Exercise DocBuilder and EquivalenceCli with invalid input and missing
   dependencies. Reuse a typed command result and cancel through the same token
   at each stage. Retain failed run evidence and publish completion atomically.

Verify each change locally and trace tests to the three StrictDoc requirements.

DocBuilder reports the operation `sharphdl.documentation.build` and EquivalenceCli
reports `sharphdl.equivalence.qualify`. Each emits a terminal JSON object with its
status, stage, evidence directories and any underlying tool or solver outcome.
Exit codes are 0 for success, 2 for invalid input, 127 for a missing dependency,
130 for cancellation and 1 for other failures. Equivalence case progress can
precede the terminal JSON line.

Use `campaign-complete.json` as the completion record. Failed or cancelled runs
retain `campaign-failure.json` when their evidence destination is writable.
The stage directory can contain useful partial evidence. Interrupted publication
can leave a `.pending` file. Reusing an existing run directory is rejected.

The Linux regression scripts check the benchmark and fuzz scan exits and exercise
the compiled campaign entry points. Run them from the repository root. For the
campaign tests, use an existing absolute managed artifacts directory containing
Release builds of DocBuilder and EquivalenceCli and the pinned native Z3 setup.

```bash
test -d tests && bash tests/check-railway-shell.sh
test -d tests && test -d "$managed" && bash tests/check-railway-campaigns.sh "$managed"
```

The verification record contains the run locations, measured coverage and scope.
[Run evidence](verification/railway-run.json)
