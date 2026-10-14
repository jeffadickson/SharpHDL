using System;
using System.Numerics;
using SharpHDL;
using Xunit;

namespace SharpHDL.UnitTests;

public sealed class CompiledCombinationalTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(3, 5, 40)]
    [InlineData(255, 1, 0)]
    [InlineData(12, 7, 133)]
    public void Compiled_expression_matches_reference_graph(int aValue, int bValue, int expected)
    {
        var m = new Module("CompiledArithmetic");
        var a = m.Input("a", 8);
        var b = m.Input("b", 8);
        var y = m.Output("y", 8);
        m.Drive(y, (a + b) * b);
        var compiled = new CompiledCombinational(m, y);
        var sim = m.Simulate();
        sim.Set(a, aValue);
        sim.Set(b, bValue);
        var actual = compiled.Evaluate(aValue, bValue);
        Assert.Equal(new BigInteger(expected), actual);
        Assert.Equal(sim.Read(y), actual);
    }

    [Fact]
    public void Rejects_register_state_and_invalid_input()
    {
        var m = new Module("Stateful");
        var x = m.Input("x", 8);
        var q = m.Register("q", 8, 0);
        m.Next(q, q + x);
        Assert.Throws<NotSupportedException>(() => new CompiledCombinational(m, q));
        var c = new CompiledCombinational(m, x);
        Assert.Throws<ArgumentOutOfRangeException>(() => c.Evaluate(256));
    }
}
