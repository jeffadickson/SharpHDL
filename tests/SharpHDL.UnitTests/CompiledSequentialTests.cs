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
    public void Synchronous_ram_read_before_write_matches_reference()
    {
        var m = new Module("CompiledRam");
        var address = m.Input("address", 4);
        var data = m.Input("data", 8);
        var we = m.Input("write", 1);
        var re = m.Input("read", 1);
        var ram = SynchronousRam.Create(m, "storage", 8, 16, address, we, data, re);
        var reference = m.Simulate();
        var compiled = new CompiledSequentialSimulator(m);
        reference.Tick(true);
        compiled.Tick(true);
        var random = new Random(111);
        for (var edge = 0; edge < 800; edge++)
        {
            int addr = random.Next(16), value = random.Next(256);
            int write = random.Next(2), read = random.Next(2);
            reference.Set(address, addr); compiled.Set(address, addr);
            reference.Set(data, value); compiled.Set(data, value);
            reference.Set(we, write); compiled.Set(we, write);
            reference.Set(re, read); compiled.Set(re, read);
            reference.Tick(); compiled.Tick();
            Assert.Equal(reference.Read(ram.ReadData), compiled.Read(ram.ReadData));
            if (edge % 97 == 0)
            {
                reference.Tick(true); compiled.Tick(true);
                Assert.Equal(reference.Read(ram.ReadData), compiled.Read(ram.ReadData));
            }
        }
    }
}
