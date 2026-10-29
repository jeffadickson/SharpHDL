# Refined RAM continuation

Baseline: clean main at cbd87819880adba35603f13ba7631ac5a4f252ea. Preserve CI off,
Native AOT deferred, existing history and the previous scope-specific evidence.

The first item closes seven retained test gaps with freshly compiled fixtures:
reference pop, signed element loads, signed native-length normalization and four
actionable diagnostic reasons. Tests compare valid operations to actual CLR
execution; rejected fixtures are not executed. Focused mutation replay targets
the original survivors before the validator implementation changes.

Library policy: reuse the existing BCL decoder/Reflection.Emit fixtures, HLS
control-flow analysis and QuikGraph integration, FsCheck, Coverlet, Stryker and
SharpFuzz. Null refinement and physical RAM binding policy require compiler-domain
logic; adding a metadata reader or a parallel graph implementation would not
supply those semantics. No production dependency change is planned.

Primary references: [CLI opcode definitions](https://github.com/dotnet/runtime/blob/main/src/coreclr/inc/opcode.def)
and [native array length](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.emit.opcodes.ldlen?view=net-10.0).
These define semantics; test execution and retained evidence qualify the bounded implementation.

Remaining work is selected from actual gaps: branch-sensitive null refinement,
bounded multiple physical RAM origins, and reliable external-tool qualification
diagnostics. General heap pointers, arbitrary CLR verification and whole sequential
proof remain separate requirements.

Item 1 local gate: all seven focused gap tests pass on Windows with warnings as errors and Coverlet collected under build/verification/alias-gaps. No production validator change is included in this item. The scoped Stryker replay is still running at this commit; no kill result is claimed yet.
