# Narrow storage and checked conversions

Starting revision: 2659a0be7c543cd7759ae6b66c8128dedb9ca3ca, clean main tracking
origin/main. CI remains disabled. Native AOT remains deferred.

## Item 1: narrow storage and ports

The scheduled frontend uses declared 8/16-bit local and captured-argument storage
for byte, sbyte, short, ushort and char. Stores discard upper bits; loads sign or
zero extend to the 32-bit CLI evaluation stack. Return ports use declared widths.
Int32/UInt32 and canonical Boolean behavior remain compatible. Wide, native,
floating, by-reference and exception-handler profiles remain unsupported.

Raw Reflection.Emit fixtures test implicit storage independently of explicit
conversion opcodes. CLR execution and modular arithmetic provide references.
Tests include argument capture, return widths, reset, boundaries, FsCheck replay
1145655947,296144285 (100 generated inputs), and unsupported storage diagnostics.
The storage RTL profile covers ten signed/unsigned/character local/argument
designs through Icarus, Verilator and generic Yosys checks. This is finite replay,
not a sequential proof or FPGA qualification.

## Library choices

Reuse BCL Reflection.Emit for precise executable IL fixtures, the existing typed
HLS/Core representation for storage and extension, FsCheck for reproducible
generated values, Coverlet for coverage, Stryker.NET for mutation and the existing
manifest-retaining HDL runner. Adding a conversion or numeric library would not
define CLI storage semantics and would add dependency/version cost without
removing domain-specific lowering. No new production dependency is needed.

Primary semantic references: Microsoft's [stloc documentation](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.emit.opcodes.stloc_s?view=net-10.0)
defines truncation on small local writes; [checked unsigned-source conversion](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.emit.opcodes.conv_ovf_i1_un)
defines overflow and source interpretation. Runtime differential checks accompany
these references; documentation alone is not execution evidence.

Execution results are recorded below after the local gates finish.

Item 1 local-commit gate: 302 fast xUnit tests pass on Windows, zero skipped (historical Z80Tests excluded). Coverlet measures the scalar descriptor/load/store helper at 100% line and branch coverage; frontend file 96.35% line / 84.25% branch. Test-linked reflection fixtures are intentionally uninstrumented because collector calls alter their CIL. The first instrumented-example run failed one case; the corrected run passes all 302. HDL, bounded mutation and finite fuzz campaigns are still running and are not claimed passed at this commit.
