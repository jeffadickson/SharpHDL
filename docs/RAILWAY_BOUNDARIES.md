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
