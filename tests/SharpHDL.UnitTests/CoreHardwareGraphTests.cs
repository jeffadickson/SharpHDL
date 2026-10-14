using System;
using System.Linq;
using SharpHDL.Core;
using Xunit;

namespace SharpHDL.UnitTests;

public sealed class CoreHardwareGraphTests
{
    static HardwareType W(int bits) => new(bits);
    static HardwareNode N(int id, HardwareOperation op, int width, params int[] operands) =>
        new(id, op, W(width), operands);

    [Fact]
    public void Valid_graph_is_topological_and_immutable()
    {
        var graph = new HardwareGraph(new[] {
            N(0, HardwareOperation.Input, 8),
            N(1, HardwareOperation.Constant, 8),
            N(2, HardwareOperation.Add, 8, 0, 1),
            N(3, HardwareOperation.Register, 8, 2)
        });
        Assert.Equal(1, graph.SchemaVersion);
        Assert.Equal(4, graph.Nodes.Count);
        Assert.Equal(new[] { 0, 1 }, graph.Nodes[2].Operands);
    }

    [Fact]
    public void Rejects_unknown_future_self_reference_or_duplicate()
    {
        Assert.Throws<ArgumentException>(() => new HardwareGraph(new[] { N(0, HardwareOperation.Add, 8, 0, 0) }));
        Assert.Throws<ArgumentException>(() => new HardwareGraph(new[] { N(0, HardwareOperation.Add, 8, 3, 3) }));
        Assert.Throws<ArgumentException>(() => new HardwareGraph(new[] { N(0, HardwareOperation.Input, 8), N(0, HardwareOperation.Input, 8) }));
    }

    [Fact]
    public void Enforces_widths_and_rejects_unsupported_ops()
    {
        Assert.Throws<ArgumentException>(() => new HardwareGraph(new[] {
            N(0, HardwareOperation.Input, 8), N(1, HardwareOperation.Input, 16),
            N(2, HardwareOperation.Add, 8, 0, 1)
        }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HardwareGraph(new[] { N(0, HardwareOperation.Input, 0) }));
        Assert.Throws<NotSupportedException>(() => new HardwareGraph(new[] { N(0, HardwareOperation.MemoryRead, 8) }));
    }
}
