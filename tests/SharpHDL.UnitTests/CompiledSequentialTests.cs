using System;
using System.Numerics;
using SharpHDL;
using Xunit;

namespace SharpHDL.UnitTests;

public sealed class CompiledSequentialTests
{
    [Fact]
    public void Swap_and_enable_match_reference_for_every_edge()
    {
        var m = new Module("CompiledSwap");
        var enable = m.Input("enable", 1);
        var a = m.Register("a", 17, 5);
        var b = m.Register("b", 17, 9);
        m.NextWhen(a, enable, b + Expr.Const(17, 1));
        m.NextWhen(b, enable, a);
        var reference = m.Simulate();
        var compiled = new CompiledSequentialSimulator(m);
        Assert.Throws<InvalidOperationException>(() => compiled.Read(a));
        reference.Tick(true);
        compiled.Tick(true);
        Assert.Equal(reference.Read(a), compiled.Read(a));
        var random = new Random(37011);
        for (var i = 0; i < 1000; i++)
        {
            int v = random.Next(2);
            reference.Set(enable, v);
            compiled.Set(enable, v);
            reference.Tick();
            compiled.Tick();
            Assert.Equal(reference.Read(a), compiled.Read(a));
            Assert.Equal(reference.Read(b), compiled.Read(b));
        }
    }

    [Fact]
    public void Overflow_and_reset_match_reference()
    {
        var m = new Module("CompiledOverflow");
        var data = m.Input("data", 8);
        var counter = m.Register("counter", 8, 0);
        m.Next(counter, counter + data);
        var reference = m.Simulate();
        var compiled = new CompiledSequentialSimulator(m);
        reference.Tick(true);
        compiled.Tick(true);
        for (var i = 0; i < 512; i++)
        {
            int value = (i * 73) % 256;
            reference.Set(data, value);
            compiled.Set(data, value);
            reference.Tick();
            compiled.Tick();
            Assert.Equal(reference.Read(counter), compiled.Read(counter));
        }
        reference.Tick(true);
        compiled.Tick(true);
        Assert.Equal(reference.Read(counter), compiled.Read(counter));
    }

    [Fact]
    public void Ram_requires_reference_backend_until_compiled_memory_semantics_exist()
    {
        var m = new Module("NoSilentRamFallback");
        var address = m.Input("address", 3);
        var data = m.Input("data", 8);
        SynchronousRam.Create(m, "ram", 8, 8, address,
            Expr.Const(1, 1), data, Expr.Const(1, 1));
        Assert.Throws<NotSupportedException>(() => new CompiledSequentialSimulator(m));
    }
}
