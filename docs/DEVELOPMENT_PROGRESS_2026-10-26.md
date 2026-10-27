# SharpHDL development continuation — 2026-10-26

## Initial assessment

No existing SharpHDL working checkout was located in the searched Windows/WSL
locations. A handoff at C:/share identified the canonical remote, which was cloned
into this task's SharpHDL directory. Initial branch main tracked origin/main;
working tree was clean at 4e6d34c8a48a5c7f4ecc72dfae825c938f661eb6. No remote
updates or pushes were performed. This checkout was newer than the handoff's
90346c3 implementation milestone; subsequent baseline commits were documentation.

Reviewed README, HANDOFF, architecture/design, requirements, test strategy,
next-milestone notes, project boundaries, scheduled compiler/simulator/RTL code,
existing FIR tests and workflows. No repository AGENTS.md was found; the supplied
personal/task instructions applied. Machine-readable project/workflow inventory
and source hashes are in verification/continuation-2026-10-26.json.

The existing implementation includes structural hardware, supported CIL frontends,
Core IR, independent Core simulation/RTL, single-port RAM, elastic pipelines,
AXI Stream/Lite blocks, FIR examples and TinyRisc. Baseline xUnit passed 134/134.
Scheduled FIR Icarus replay also passed at 2/4/8/16 taps. Arbitrary CIL scheduling,
multiple arithmetic-resource binding, multiclock Core simulation and broad vendor
memory qualification remain incomplete. Existing documentation contains stale
claims; only the .NET SDK/run guidance directly touched here was corrected.

## Five completed engineering iterations (of the requested twenty)

1. **Expression ownership.** Acceptance: all scheduled expression sinks reject
   foreign input/register leaves, including nested/shared graphs; local constants
   remain portable. Implemented iterative DAG validation, direct negative tests,
   exhaustive byte arithmetic and a 500-case FsCheck campaign with shrinking.
2. **Builder lifecycle and failed-declaration recovery.** Acceptance: all retained
   state handles freeze after successful lowering, failed validation is repairable,
   compiler names cannot collide, and rejected declarations do not consume names.
   Implemented guards/transactional validation and directed plus 500-case malformed
   width tests. No resource allocation/binding capability is implied.
3. **Non-FIR memory/control vertical slice.** Acceptance: table lookup/conditional
   update through the existing scheduled builder, independent array/protocol
   oracle, reset/stall/conservation checks, generated RTL replay and generic
   synthesis resource checks. Implemented parameterized example, shared campaign,
   replay generator and CI job. All three depths passed. SCHED-011 remains partial
   because its wider resource qualification is not complete.
4. **Restore standalone regression build.** Acceptance: documented .NET 10 command
   runs the existing regression rather than compiling nested projects and their
   obj files. Added explicit root source inclusion and friend access for existing
   structural invariant checks. Full standalone runner passed.
5. **Portable provenance regression.** Acceptance: the same test works when Linux
   outputs are isolated on its native filesystem. First Linux run exposed one
   checkout-discovery failure (147 pass/1 fail). Replaced output-directory lookup
   with CallerFilePath source lookup. Final Windows and Linux both pass 148/148.

These are five substantive changes, not twenty completed iterations. Further
capabilities are left as explicit next work rather than counted as scaffolding.

## Verification evidence

Implementation commits: 04a7c7b (build/test portability), 7ca5ed3 (scheduled table
and compiler boundaries). Commits use the local agent identity Codex
<codex@localhost> because this fresh checkout had no configured author identity;
no global identity setting was changed.

- Windows .NET 10.0.403: 148 passed, 0 failed/skipped; Linux .NET 10.0.402:
  148 passed, 0 failed/skipped, native /tmp output tree separate from Windows.
- Legacy executable: 15,212 base assertions; additional control/memory 21,414,
  frame 112,057, AXI interop 262,347, memory target 15,464, feedback 76,868,
  pipeline 17,730, process/channel 12,405, feature 49,234, numeric 6,698,
  multiplier 56,479 and divider 388,851 checks reported passing. These are
  runner-reported counts, not xUnit test counts. Generated legacy RTL was archived
  under ignored build/verification/legacy-generated; tracked generated baselines
  were restored so regenerated formatting/provenance did not pollute this change.
- FsCheck 3.3.3: two 500-case campaigns, replay 1145655947,296144285. Both pass;
  no counterexample was produced. This is bounded library-generated fuzz/property
  testing, not a long-running coverage-guided fuzz qualification.
- Coverlet 10.1.0: 2615/4611 lines (56.71%), 1869/4646 branches (40.22%).
  ScheduledMachineBuilder: 96.82% line, 83.87% branch; ScheduledTable: 100% both.
  The saved runsettings include linked examples and exclude tests/oracles. These
  aggregate rates have a different scope from earlier collector runs and must not
  be compared as equivalent scopes. Legacy-runner/HDL coverage is unmeasured.
- Stryker.NET 5.0.0, final ScheduledMachine.cs scope: 162 killed, 50 survived,
  9 timed out, 37 uncovered, 34 compile errors, 470 ignored by scope/block filters.
  Tool-reported score 66.28%; timeout is not a confirmed assertion kill. Surviving
  mutants and uncovered validation/priority paths remain engineering work. This
  mutation campaign excludes the new table implementation and oracle.
- Icarus 12.0: FIR 2/4/8/16 replays pass; table address widths 1/4/8 pass,
  each 3201 edges with 421/418/414 completed responses. Seeds 261027/261030/261034.
- Yosys 0.33: hierarchy/proc/opt/check passes; table memory_collect plus explicit
  count assertions retains one $mem_v2 and one $add at address width 4. FIR width
  4 check reports one multiply. No vendor mapping/timing claim.
- Verilator 5.020: table width-4 RTL lint passes; no Verilator replay was run.
- StrictDoc 0.30.4: requirements parse/export passes. Layer dependency check and
  YAML parse pass. Hosted CI was not dispatched; the complete hosted workflow is
  unverified for these commits. Existing compiler/xUnit analyzer warnings remain.

Raw logs, TRX, coverage XML, mutation HTML/JSON and generated replays remain local
under ignored build/verification and generated/{scheduled-table,hls-scheduled-fir}.
Compact reproducible metrics and tested source hashes are version controlled.
No compiler/simulator performance optimization was attempted, so no performance
improvement is claimed. The legacy runner's incidental native throughput outputs
are not a controlled SharpHDL simulator benchmark.

## Reproduction

Check that the checkout exists before changing into it. From its root:

```powershell
if (!(Test-Path -LiteralPath tests/SharpHDL.UnitTests -PathType Container)) { throw 'Wrong checkout' }
dotnet test tests/SharpHDL.UnitTests/SharpHDL.UnitTests.csproj -c Release --settings tests/SharpHDL.UnitTests/coverage.runsettings --collect:'XPlat Code Coverage' --results-directory build/verification/replay
dotnet run --project tests/Tests.csproj -c Release
dotnet run --project tools/SharpHDL.AxiFirRtlDemo -c Release -- --table generated/scheduled-table
```

The standalone runner regenerates tracked generated/ artifacts; run in an isolated
checkout/output working directory if those outputs must be preserved. Linux HDL
commands and table reset/latency contract are in MODULAR_ARCHITECTURE.md. To repeat
mutation testing, install dotnet-stryker 5.0.0 into a checked local tool directory,
check tests/SharpHDL.UnitTests exists, run from there with the saved
stryker-config.json and an ignored output directory. StrictDoc command:
`strictdoc export docs/SCHEDULED_VALIDATION.sdoc --output-dir build/verification/strictdoc`.
Tool selection followed primary Coverlet, FsCheck, Stryker and StrictDoc docs:
https://packages.nuget.org/packages/coverlet.collector/ ;
https://fscheck.github.io/FsCheck/RunningTests.html ;
https://stryker-mutator.io/docs/stryker-net/configuration/ ;
https://strictdoc.readthedocs.io/en/stable/stable/docs/strictdoc_01_user_guide.html .
These are test/documentation dependencies, not hardware runtime dependencies.

## Five highest-value next tasks

1. Add typed arithmetic-resource declarations/binding and reusable arbitration,
   including capacity >1 and illegal dependency/binding diagnostics.
2. Close surviving semantic/priority mutants with independent tests; add a bounded
   coverage-guided fuzz campaign and mutate the new table/oracle boundary too.
3. Connect the supported stateful CIL control-flow representation to the scheduled
   builder, preserving explicit memory latency and source diagnostics.
4. Run hosted Windows/Linux CI and broaden Core/legacy/Verilator parity, including
   directed reset-at-each-phase tests and multi-kernel scheduling cases.
5. Extend memory-port/banking and synthesis qualification with target-specific
   resource evidence, then benchmark representative designs before optimizing.

Remaining risk includes legacy documentation drift, analyzer warnings, partial
Core migration, conservative memory arbitration, unbound resource reservations,
no automatic schedule extraction for arbitrary loops, no multiport/multiclock
Core qualification and no formal proof, board execution or physical timing result.
