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
    [Fact]
    public void Reused_operand_buffer_reflects_input_changes_and_atomic_register_commit()
    {
        var m = new Module("ReusableOperands");
        var input = m.Input("input", 8);
        var a = m.Register("a", 8, 2);
        var b = m.Register("b", 8, 5);
        var probe = m.Output("probe", 8);
        m.Next(a, b + input);
        m.Next(b, a);
        m.Drive(probe, a + input);
        var reference = m.Simulate();
        var compiled = new CompiledSequentialSimulator(m);
        reference.Tick(true);
        compiled.Tick(true);
        for (var i = 0; i < 100; i++)
        {
            var value = (i * 17) & 255;
            reference.Set(input, value);
            compiled.Set(input, value);
            // Combinational reads must immediately reflect changed inputs.
            Assert.Equal(reference.Read(probe), compiled.Read((Expr)probe));
            reference.Tick();
            compiled.Tick();
            Assert.Equal(reference.Read(a), compiled.Read(a));
            Assert.Equal(reference.Read(b), compiled.Read(b));
            Assert.Equal(reference.Read(probe), compiled.Read((Expr)probe));
        }
    }

    [Theory]
    [InlineData(8)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(65)]
    public void Word_arithmetic_matches_big_integer_reference_across_overflow(int bits)
    {
        var m = new Module("ArithmeticFastPath" + bits);
        var input = m.Input("input", bits);
        var add = m.Register("add", bits, Expr.Mask(bits));
        var mul = m.Register("mul", bits, 7);
        m.Next(add, add + input);
        m.Next(mul, mul * input);
        var reference = m.Simulate();
        var compiled = new CompiledSequentialSimulator(m);
        reference.Tick(true); compiled.Tick(true);
        var random = new Random(304 + bits);
        for (int i = 0; i < 400; i++)
        {
            BigInteger v = (new BigInteger(random.NextInt64()) &
                            Expr.Mask(Math.Min(bits, 63))) & Expr.Mask(bits);
            if (i % 7 == 0) v = Expr.Mask(bits);
            reference.Set(input, v);
            compiled.Set(input, v);
            reference.Tick(); compiled.Tick();
            Assert.Equal(reference.Read(add), compiled.Read(add));
            Assert.Equal(reference.Read(mul), compiled.Read(mul));
        }
    }

}
