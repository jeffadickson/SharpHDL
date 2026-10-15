using System;
using System.Collections.Generic;
using System.Numerics;

namespace SharpHDL.Core;

/// <summary>Backend-neutral width-checked hardware operation kinds.</summary>
public enum HardwareOperation
{
    Constant, Input, Add, Subtract, Multiply, And, Or, Xor, Not,
    Select, Register, MemoryRead, MemoryWrite,
    ShiftLeft, ShiftRight, ArithmeticShiftRight, Slice, Resize,
    SignExtend, Concat, Equal, LessThan, SignedLessThan,
    Lookup, DspMultiplyUnsigned
}

/// <summary>Bit width and signed interpretation of a hardware value.</summary>
public sealed record HardwareType(int Width, bool Signed = false)
{
    public HardwareType Validate()
    {
        if (Width is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(Width));
        return this;
    }
}

/// <summary>
/// Immutable IR node. Offset is used for constant shifts/slices;
/// Table is an optional immutable ROM payload; MemoryName references a
/// separately declared RAM snapshot, never an implicit zero-filled memory.
/// </summary>
public sealed record HardwareNode(
    int Id, HardwareOperation Operation, HardwareType Type,
    IReadOnlyList<int> Operands, BigInteger? ConstantValue = null,
    string? Name = null, int Offset = 0,
    IReadOnlyList<BigInteger>? Table = null, string? MemoryName = null);
