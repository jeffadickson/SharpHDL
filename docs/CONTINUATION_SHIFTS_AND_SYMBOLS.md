# Continuation: scheduled shifts and matched source symbols

Baseline: f11669763eb4ffa3472af2f50da4598823107109, clean main tracking origin/main.
Two substantive capabilities were completed; verification hardening has separate
commits. Earlier work and evidence remain in [the previous report](FOLLOWUP_2026-10-27.md).

## Implemented behavior and acceptance

Scheduled CIL now lowers shl, shr and shr.un into typed Core hardware with the
CLR-compatible Int32 low-five-bit count policy. Arithmetic right shift sign extends;
logical right shift zero fills. Constant bit-vector shifts handle oversize offsets
without allocating enormous intermediates. Negative constant offsets reject.
This is the supported SharpHDL policy, tested against the native x64 CLR; it is not
a claim that every oversized raw-CIL shift has universally specified ECMA behavior.

Matched Portable PDB information now maps actual scheduler encodings to IL offsets
and source spans, preserving hidden/unmapped states and document checksum metadata.
BCL PEReader checks loaded-image MVID and associated symbol identity, including
embedded PDBs. Missing images/symbols are explicit statuses; malformed symbols and
unexpected IO failures propagate. Existing annotation no longer carries visible
locations through hidden regions. Generated source-map JSON identifies the state
signal and actual encodings. These are control-state maps, not provenance for every
optimized RTL wire. Document hashes are supplied, not automatically verified against
local source contents.

Acceptance tests cover boundary/negative counts, all byte values with fixed shift
offsets, 200 reproducible FsCheck cases, direct emitted CIL without C# count masks,
hidden points, embedded/wrong/malformed symbols, bounded offsets and IO failures.
Mutation testing exposed a real test gap: C# inserts count masks before emitting
shift instructions. Raw-CIL tests, RTL replay and fuzzing now challenge the lowering
independently of that compiler behavior.

## Executed verification

- Windows SDK 10.0.403 and isolated Linux SDK 10.0.402: 207/207 xUnit tests pass,
  zero failures/skips. Full standalone legacy regression also passes.
- Coverlet managed scope: 3028/4921 lines (61.53%), 2509/5135 branches (48.86%).
  CLI, fuzz harness and HDL toggle coverage are unmeasured. Coverage is not proof.
- Eight designs pass both Icarus 12.0 and Verilator 5.020: three table sizes,
  CIL loop, C# shifts, raw-CIL shifts and two banked memories. Shift replays check
  1996 and 1366 edges respectively, including 105 transactions each.
- Yosys 0.33 hierarchy/proc/opt/check/stat passes both shift designs. No new
  target mapping, physical timing, placement, board qualification or formal proof.
- Focused Stryker.NET 5.0.0 score: 88.89%; 86 killed, 5 surviving, 6 uncovered,
  2 timeouts. Six selected mutants failed compilation and 13 were ignored.
  Timeouts count toward the tool score but are not confirmed assertion kills.
  Whole-project out-of-filter mutants are excluded from these scoped counts.
- SharpFuzz 2.2.0 / AFL++ 4.09c: 60 seconds, seed 261026, 4486 executions,
  121 corpus entries, 1254 bitmap edges, no saved crashes or hangs. This bounded
  campaign covers raw shifts, table traffic and IL-name parsing, not all CIL.
- StrictDoc 0.30.4 parse/export and Core layering checks pass. No performance
  optimization or new performance claim. GitHub Actions remains disabled.

[Machine-readable evidence](verification/continue-2026-10-27.json) records revisions,
source hashes, versions and scope; [mutant dispositions](verification/continue-mutant-dispositions.json)
and [source-map checks](verification/continue-source-map-checks.json) retain gaps.
Raw artifacts are local and ignored: build/verification/continue-reviewed-windows.log,
continue-reviewed-linux.log, continue-mutation-final.log, continue-rtl-final.log and
continue-fuzz-final.log. Linux RTL artifacts: /tmp/sharphdl-rtl-OeaZAW; fuzz artifacts:
/tmp/sharphdl-fuzz-LxfYzr. These temporary paths are machine-local, not archival storage.

## Reproduction and library decisions

From an existing repository directory, use dotnet test on
 tests/SharpHDL.UnitTests/SharpHDL.UnitTests.csproj -c Release with
 --collect:"XPlat Code Coverage". The checked-in focused mutation configuration is
 tests/SharpHDL.UnitTests/stryker-cil-diagnostics.json. On Linux with prerequisites,
 bash tools/run-scheduled-rtl.sh runs both simulators and generic synthesis;
 bash tools/run-scheduled-fuzz.sh 60 runs the bounded fixed-seed campaign.
Keep Linux build artifacts separate from Windows output. Tool installation and
external simulator availability remain prerequisites, not successful checks by default.

No production NuGet dependency was added. BCL PEReader/PortablePdbBuilder/metadata
APIs supply maintained identity handling and independent malformed-symbol fixtures;
NSubstitute exercises only the IO seam. BinaryPrimitives decodes fuzz bytes explicitly.
Custom code remains limited to hardware semantics and bounded sequence-point lookup.
[Library tradeoffs](LIBRARY_FIT_2026-10-27.md) describes alternatives and future fits.

## Remaining risks and next tasks

1. Integrate matched source spans into actionable compiler errors and debugger views.
2. Extend checked/narrow CIL semantics only with independent overflow/extension tests.
3. Qualify remaining symbol-reader paths: default missing sidecar, stale on-disk MVID,
   defensive provider invariants and documented surviving mutants.
4. Add iterative arithmetic scheduling and cross-kernel arbitration with latency tests.
5. Expand sequential/memory differential campaigns and target mapping qualification.

Calls, checked arithmetic, division, packed ports, exception handlers, true multiport
RAM and multiclock Core remain unsupported or outside this slice. Existing analyzer
warnings persist. Private invalid-width/opcode paths are unreachable through the
validated frontend and remain partly uncovered; source inspection does not close
those verification gaps.

## Local commits

a473b93 shifts; b5bbcaa symbols; e45ed00 nullable/mutation configuration;
430ca61 fuzz boundaries; cf7916d raw opcode and malformed-symbol tests;
b72423f raw RTL replay; f337221 raw fuzz oracle; 08b8a00 synthesis checks.
The final documentation commit records these results without semantic changes.
