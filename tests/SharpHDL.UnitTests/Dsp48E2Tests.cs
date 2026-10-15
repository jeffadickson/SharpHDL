using System;
using System.Numerics;
using SharpHDL;
using Xunit;

namespace SharpHDL.UnitTests;

public sealed class Dsp48E2Tests
{
    [Fact]
    public void Direct_dsp_primitive_has_full_precision_and_matches_both_simulators()
    {
        var m=new Module("DirectDsp");
        var a=m.Input("a",16);
        var b=m.Input("b",16);
        var p=m.Output("product",32);
        m.Drive(p,Dsp48E2.MultiplyUnsigned(a,b).Named("explicit_dsp"));
        var rtl=m.Verilog();
        Assert.Contains("sharphdl_dsp48e2_mul_unsigned",rtl);
        Assert.Contains(".A_WIDTH(16)",rtl);
        Assert.Contains(".B_WIDTH(16)",rtl);
        var reference=m.Simulate();
        var compiled=new CompiledSequentialSimulator(m);
        reference.Tick(true);compiled.Tick(true);
        var rng=new Random(42);
        for(int i=0;i<256;i++)
        {
            int av=i==0?65535:rng.Next(65536);
            int bv=i==0?65535:rng.Next(65536);
            reference.Set(a,av);reference.Set(b,bv);
            compiled.Set(a,av);compiled.Set(b,bv);
            var expected=new BigInteger(av)*bv;
            Assert.Equal(expected,reference.Read(p));
            Assert.Equal(expected,compiled.Read((Expr)p));
        }
    }

    [Fact]
    public void Rejects_operand_widths_that_cannot_fit_one_unsigned_DSP48E2()
    {
        var m=new Module("BadDsp");
        var a=m.Input("a",27);
        var b=m.Input("b",18);
        Assert.Throws<NotSupportedException>(()=>Dsp48E2.MultiplyUnsigned(a,b));
    }
}
