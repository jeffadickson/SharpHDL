using System;
using System.Collections.Generic;
using System.Numerics;
namespace SharpHDL.Core;

/// <summary>Backend-neutral width-checked hardware operation kinds.
/// This is the beginning of a versioned IR contract, not yet the production lowering path.</summary>
public enum HardwareOperation { Constant, Input, Add, Subtract, Multiply, And, Or, Xor, Not, Select, Register, MemoryRead, MemoryWrite }
public sealed record HardwareType(int Width, bool Signed = false)
{
    public HardwareType Validate()
    {
        if (Width is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(Width));
        return this;
    }
}
/// <summary>One immutable IR node with optional literal and signal metadata.</summary>
public sealed record HardwareNode(int Id, HardwareOperation Operation, HardwareType Type,
    IReadOnlyList<int> Operands, BigInteger? ConstantValue = null, string? Name = null);
