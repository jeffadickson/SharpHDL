# Typed stack and bound RAM alias continuation

Baseline: clean main at 4fcde8f06c72f0954ed064eb3fcdef8d93f73ad4, tracking
origin/main. CI remains disabled and Native AOT deferred.

Item 1 acceptance: distinguish Int32 stack values, bound RAM references and native
array lengths; reject invalid operand roles and equal-height type merges; validate
all local/argument indexes, including unreachable code. Normalize bounded lengths
only through conv.i4/u4 before integer use. Keep source IL diagnostics.

This closes a real correctness gap: a numeric placeholder previously let malformed
CIL supply an integer as the array operand, or use a RAM reference as a number.
Raw Reflection.Emit fixtures challenge those cases without C#'s type checker.
Native array arithmetic is conservatively unsupported: accepting it as Int32
would, for example, apply the wrong shift-count mask on 64-bit CLR hosts.

Library choice: reuse BCL Reflection.Emit/opcode metadata, the existing decoded
instruction model, HLS InstructionControlFlow and BCL Queue/Dictionary worklists.
The abstract types and alias provenance are hardware-domain semantics. A new
Mono.Cecil/AsmResolver dependency would duplicate the existing metadata/decoder
and source-symbol integration without supplying those semantics. Existing graph
analysis stays in its established component; no replacement implementation or new
production dependency is required. FsCheck, Coverlet, Stryker and SharpFuzz remain
the established verification tools.

Primary references: [ECMA-335](https://ecma-international.org/publications-and-standards/standards/ecma-335/),
[runtime opcode metadata](https://github.com/dotnet/runtime/blob/main/src/coreclr/inc/opcode.def),
and [Microsoft opcode reference](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.emit.opcodes?view=net-10.0).
These define operand categories; they do not establish execution evidence.

Execution results and bounded alias acceptance will be recorded after local gates.

Item 1 local gate: 333 fast Windows tests pass (zero skips/failures, historical full Z80Tests excluded), followed by all 15 focused typed-stack cases including the added loop and 100 seeded invalid-variable samples. Both commands build with warnings as errors. Focused Coverlet class rates: {"type":"SharpHDL.CilCompiler","line":"0.9545","branch":"0.9"}. Two initial namespace-import build failures were corrected before these passing runs. Linux, mutation, fuzz and full documentation gates remain pending at this commit.
