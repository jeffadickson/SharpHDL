using System;
using System.Linq;
using System.Numerics;
using SharpHDL;
using Xunit;

namespace SharpHDL.UnitTests;

public sealed class CompiledDagTests
{
    [Fact]
    public void Shared_fanout_is_compiled_as_a_linear_schedule()
    {
        var module = new Module("SharedFanout");
        var input = module.Input("input", 32);
        Expr result = input;
        // Expanding this DAG into a tree would duplicate hundreds of millions of leaves.
        for (int i = 0; i < 28; i++) result = result + result;
        var compiled = new CompiledCombinational(module, result);
        Assert.Equal(29, compiled.ScheduledNodeCount);
        Assert.Equal(new BigInteger(0x50000000u), compiled.Evaluate(5));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(17)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(127)]
    [InlineData(257)]
    public void Typed_DAG_operations_match_the_bit_vector_interpreter(int width)
    {
        var module = new Module("TypedDag" + width);
        var a = module.Input("a", width);
        var b = module.Input("b", width);
        var choose = module.Input("choose", 1);
        var mask = Expr.Mask(width);
        var joined = Expr.Concat(a, b);
        var expressions = new[] {
            a + b, a - b, a * b, a & b, a | b, a ^ b, ~a,
            a.Eq(b), a.Lt(b), a.SignedLt(b), a.SignExtend(width + 3),
            a.ArithmeticShift(0), a.ArithmeticShift(width), a.ArithmeticShift(width + 1),
            a >> (width + 2), a << (width + 2), a >> 1, a << 1,
            a.Resize(Math.Max(1, width - 1)), joined, joined.Slice(width, 1),
            Expr.Mux(choose, a, b), Expr.Lookup(width, a, new[] { BigInteger.Zero, mask })
        };
        var engines = expressions.Select(e => new CompiledCombinational(module, e)).ToArray();
        var reference = module.Simulate();
        var random = new Random(901 + width);
        var bytes = new byte[(width + 7) / 8];
        BigInteger RandomBits()
        {
            random.NextBytes(bytes);
            return new BigInteger(bytes, isUnsigned: true) & mask;
        }
        for (int i = 0; i < 24; i++)
        {
            var x = i == 0 ? mask : i == 1 ? BigInteger.Zero : RandomBits();
            var y = i == 0 ? BigInteger.One : i == 1 ? mask : RandomBits();
            reference.Set(a, x);
            reference.Set(b, y);
            reference.Set(choose, i & 1);
            for (int j = 0; j < expressions.Length; j++)
                Assert.Equal(reference.Read(expressions[j]), engines[j].Evaluate(x, y, i & 1));
        }
    }

    [Fact]
    public void Multiple_RAM_ports_sample_old_state_with_independent_write_enables()
    {
        var module = new Module("MultipleRams");
        var address = module.Input("address", 3);
        var data = module.Input("data", 16);
        var firstEnable = module.Input("firstWrite", 1);
        var secondEnable = module.Input("secondWrite", 1);
        var first = SynchronousRam.Create(module, "first", 16, 3, address,
            firstEnable, data, Expr.Const(1, 1));
        var second = SynchronousRam.Create(module, "second", 16, 5, address,
            secondEnable, first.ReadData + data, Expr.Const(1, 1));
        var reference = module.Simulate();
        var compiled = new CompiledSequentialSimulator(module);
        reference.Tick(true);
        compiled.Tick(true);
        void Set(Signal port, int value) { reference.Set(port, value); compiled.Set(port, value); }
        for (int i = 0; i < 160; i++)
        {
            Set(address, i % 8); // Include out-of-range reads/writes for both memories.
            Set(data, i * 37);
            Set(firstEnable, i & 1);
            Set(secondEnable, (i >> 1) & 1);
            bool reset = i % 29 == 0;
            reference.Tick(reset);
            compiled.Tick(reset);
            Assert.Equal(reference.Read(first.ReadData), compiled.Read(first.ReadData));
            Assert.Equal(reference.Read(second.ReadData), compiled.Read(second.ReadData));
        }
    }
}
