using System;
using System.Numerics;
using SharpHDL;
using Xunit;

namespace SharpHDL.UnitTests;

public sealed class IterativeMultiplierTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void Multicycle_multiplier_preserves_result_and_stalls(int width)
    {
        var m=new Module("Iterative"+width);
        var p=IterativeMultiplier.Attach(m,"mul",width);
        var sim=m.Simulate();
        sim.Tick(true);
        sim.Set(p.InputValid,0);
        sim.Set(p.OutputReady,0);
        var mask=Expr.Mask(width);
        var values=new [] {(BigInteger.Zero,BigInteger.One),
            (mask,mask),(new BigInteger(3),new BigInteger(7)),
            (new BigInteger(0x5A5A5A5A),new BigInteger(0x0F0F0F0F))};
        foreach(var (a,b) in values)
        {
            sim.Set(p.A,a&mask);sim.Set(p.B,b&mask);
            sim.Set(p.InputValid,1);
            Assert.Equal(BigInteger.One,sim.Read(p.InputReady));
            sim.Tick(); // accept
            sim.Set(p.InputValid,0);
            Assert.Equal(BigInteger.Zero,sim.Read(p.InputReady));
            for(int i=0;i<width;i++)sim.Tick();
            Assert.Equal(BigInteger.One,sim.Read(p.OutputValid));
            var expected=(a*b)&mask;
            Assert.Equal(expected,sim.Read(p.Result));
            for(int i=0;i<5;i++)
            {
                sim.Tick();
                Assert.Equal(BigInteger.One,sim.Read(p.OutputValid));
                Assert.Equal(expected,sim.Read(p.Result));
            }
            sim.Set(p.OutputReady,1);
            sim.Tick();
            sim.Set(p.OutputReady,0);
            Assert.Equal(BigInteger.Zero,sim.Read(p.OutputValid));
            Assert.Equal(BigInteger.One,sim.Read(p.InputReady));
        }
        Assert.Contains("always @(posedge clk)",m.Verilog());
        Assert.DoesNotContain(" * ",m.Verilog());
    }
}
