using System;
using System.Collections.Generic;
namespace SharpHDL.Core;

/// <summary>Backend-neutral width-checked hardware operation kinds.
/// This is the beginning of a versioned IR contract, not yet the production lowering path.</summary>
public enum HardwareOperation { Constant, Input, Add, Subtract, And, Or, Xor, Not, Select, Register, MemoryRead, MemoryWrite }
public sealed record HardwareType(int Width, bool Signed = false)
{
    public HardwareType Validate()
    {
        if (Width is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(Width));
        return this;
    }
}
public sealed record HardwareNode(int Id, HardwareOperation Operation, HardwareType Type, IReadOnlyList<int> Operands);
