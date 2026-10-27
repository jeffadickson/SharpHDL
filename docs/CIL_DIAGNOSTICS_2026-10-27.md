# WP-02: source-aware compiler rejections

The shared CIL rejection factory now attaches `CilDiagnostic` to the existing
`NotSupportedException`. Call `CilDiagnostic.FromException(error)` for stable code
`SHDL-CIL-001`, original `MethodInfo`, IL byte offset, reason, matched source span
and symbol availability. Existing exception catches remain compatible. This
enhances rejection sites using the shared factory; it does not classify every
possible decoder/runtime exception into a new diagnostic taxonomy.

The implementation reuses `PortablePdbSourceMap` and the BCL metadata reader.
Missing or hidden source remains null. Malformed/unreadable optional symbols
preserve the primary rejection, retain their failure as the exception's inner
exception and typed `SymbolFailure`, and identify the failure in display text.
It does not execute the rejected method or guess a source location. Dynamic and
global emitted methods work without on-disk images or declaring types.

Review reproduced a real negative-branch-target defect: symbol lookup previously
threw `ArgumentOutOfRangeException`, hiding the intended rejection. A regression
test failed before the bounds guard and passed afterward. The scheduled frontend
will additionally locate malformed branch diagnostics at the originating branch
in WP-03; the shared guard remains relevant to other frontend callers.

## Verification

Before WP-03 integration, Windows .NET SDK 10.0.403 passed **214/214 non-Z80
regression tests**, zero failures/skips, including seven new diagnostic tests.
Command from a checked repository directory:

```powershell
dotnet test tests/SharpHDL.UnitTests -c Release -m:1 -nr:false -p:UseSharedCompilation=false --filter 'FullyQualifiedName!~Z80Tests' --collect:'XPlat Code Coverage' --results-directory build/verification/wp02/reviewed --logger 'trx;LogFileName=tests.trx'
```

Coverlet 10.1.0 reports **100% line / 100% branch** for `CilDiagnostic` in that
scope. This does not measure HDL or prove all metadata/IO failure modes. Exact
artifact: `build/verification/wp02/reviewed/25e82b5e-2f7d-4793-86a1-02dfbd1b1759/coverage.cobertura.xml`.
The test log is `build/verification/wp02-reviewed.log`; the reproduced failure is
`build/verification/wp02-negative-before.log`.

Boundary review added an eighth test for source lookup at offset zero and extended
the malformed-target case to the legacy frontend's negative reported offset.
The integrated Windows regression then passed **226/226**, zero failed/skipped,
including the WP-03 graph cases (`build/verification/wp03-windows.log`). Separate
class coverage and final mutation counts are retained in the completion evidence.

Mutation evidence and subsequent Windows/Linux integration results are recorded
in the work-package completion report. The first mutation attempt exhausted the
machine's paging-file capacity before testing; idle build servers were stopped
and subsequent jobs serialized with bounded processor counts. An intermediate
attempt also observed incomplete WP-03 source before its dependency was added;
neither failed setup is counted as an executed mutation campaign.

Requirement: **CIL-DIAG-001**, in `SCHEDULED_VALIDATION.sdoc`. Public API XMLDoc is
included. No new production package is required for this work package. Portable
PDB availability and document checksums retain the existing source-map limitations;
the diagnostic does not attest that the source file currently on disk matches.
