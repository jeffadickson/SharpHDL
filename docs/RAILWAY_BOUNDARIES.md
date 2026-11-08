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

## Editor command contract

`sharphdl.buildDesign` and `sharphdl.runDesign` accept
`{ workspaceUri, projectId }` and an optional VS Code cancellation token.
They return `{ operation, status, stage, diagnostic?, exitCode? }`. Omit the
selector to receive `NeedsInput`. The workspace must be open and local.
The extension reads the project manifest from disk and validates its sources
before creating the output directory. A run stops if its build fails.
A second operation targeting the same output path in the same extension host
returns `Busy`. This guard does not coordinate separate VS Code processes.

The palette and Explorer use `sharphdl.selectAndBuildDesign` and
`sharphdl.selectAndRunDesign`. These adapters can request input and present
diagnostics. The shared operation uses VS Code ProcessExecution and its problem
matcher. This retains terminal logs and Problems integration without introducing
a subprocess dependency or duplicating the task lifecycle.
[VS Code API](https://code.visualstudio.com/api/references/vscode-api)

## Local gate and Dafny check

From the repository root in PowerShell 7 on Windows, run the local gate with
Node and WSL available. `$managed` is the absolute Linux path to the compiled
Release artifacts described above.

```powershell
if (Test-Path -LiteralPath tests -PathType Container) {
    ./tests/Check-RailwayLocal.ps1 -LinuxArtifacts $managed
}
```

With the installed Windows VS Code CLI and .NET SDK, the separate host
check starts a real task in an isolated workspace. It checks that a failing
build reports its exit status and prevents the run task from starting.

```powershell
if ((Test-Path -LiteralPath tests -PathType Container) -and
    (Test-Path -LiteralPath $evidence -PathType Container)) {
    ./tests/Check-RailwayEditorHost.ps1 -EvidenceRoot $evidence
}
```

The opt-in Dafny check proves the zero-exit invariant for the model and compiles
the generated C# to compare against the production console on all current
terminal outcomes and a representative unknown value. It does not prove
publication, process cancellation or the C# implementation. Dafny 4.11.0
and its generated runtime are confined to the check. The existing production
switch remains the runtime implementation.
[Dafny 4.11.0](https://github.com/dafny-lang/dafny/releases/tag/v4.11.0)

```powershell
if ((Test-Path -LiteralPath tests/dafny -PathType Container) -and
    (Test-Path -LiteralPath $evidence -PathType Container)) {
    ./tests/dafny/Check-CampaignExit.ps1 -Dafny $dafny -EvidenceRoot $evidence
}
```

[Editor, local gate and proof evidence](verification/railway-followup.json)
