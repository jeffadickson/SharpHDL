using System;
using System.Collections.Generic;
using System.Linq;
using SharpHDL.Examples;
using SharpHDL.Core;
using SharpHDL.HLS;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>End-to-end checks for schedule-driven, parameterized RAM FIR RTL.</summary>
public sealed class HlsScheduledFirTests
{
    /// <summary>
    /// Compare every accepted output against independent convolution,
    /// including initial zeros, circular history, TLAST, and AXI stalls.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    public void Generated_controller_matches_reference_for_multiple_tap_counts(int taps)
    {
        var module=HlsScheduledFir.Build(taps);
        var sim=module.Simulate();
        var ports=module.Ports.ToDictionary(p=>p.Name,p=>p);
        void Set(string name,int value)=>sim.Set(ports[name],value);
        int Read(string name)=>(int)sim.Read(ports[name]);
        var coefficients=Enumerable.Range(0,taps)
            .Select(i=>(ushort)(i*7+3)).ToArray();
        for(int i=0;i<taps;i++)Set("coefficient"+i,coefficients[i]);
        var history=new ushort[taps-1];
        var expected=new Queue<(ushort Data,bool Last)>();
        var random=new Random(1500+taps);
        bool pending=false;
        ushort sample=0;
        bool last=false;
        int accepted=0,completed=0;
        sim.Tick(true);
        int cycles=1000+160*taps;
        for(int cycle=0;cycle<cycles;cycle++)
        {
            if(!pending && cycle<cycles-200 && random.Next(4)!=0)
            {
                pending=true;
                sample=(ushort)random.Next(65536);
                last=random.Next(11)==0;
            }
            bool ready=cycle>=cycles-200 || random.Next(3)!=0;
            Set("s_axis_tvalid",pending?1:0);
            Set("s_axis_tdata",sample);
            Set("s_axis_tlast",last?1:0);
            Set("s_axis_tkeep",3);
            Set("s_axis_tuser",0);
            Set("m_axis_tready",ready?1:0);
            bool take=pending && Read("s_axis_tready")!=0;
            bool give=ready && Read("m_axis_tvalid")!=0;
            if(give)
            {
                Assert.NotEmpty(expected);
                var next=expected.Dequeue();
                Assert.Equal(next.Data,(ushort)Read("m_axis_tdata"));
                Assert.Equal(next.Last,Read("m_axis_tlast")!=0);
                Assert.Equal(3,Read("m_axis_tkeep"));
                completed++;
            }
            if(take)
            {
                ulong sum=(ulong)sample*coefficients[0];
                for(int i=1;i<taps;i++)
                    sum+=(ulong)history[i-1]*coefficients[i];
                expected.Enqueue(((ushort)sum,last));
                for(int i=history.Length-1;i>0;i--)history[i]=history[i-1];
                history[0]=sample;
                accepted++;
            }
            sim.Tick();
            if(take)pending=false;
        }
        Assert.False(pending);
        Assert.Empty(expected);
        Assert.Equal(accepted,completed);
        Assert.True(completed>taps*2);
        Assert.Equal(2*taps,HlsSinglePortFirPlanner.Plan(taps).InitiationInterval);
        // A single multiply node is shared across the acceptance and
        // historical accumulation phases, rather than instantiating N DSPs.
        Assert.Single(module.ExportCoreGraph().Graph.Nodes.Where(n=>
            n.Operation==HardwareOperation.Multiply));
        Assert.Contains("fir_history",module.Verilog());
    }

    /// <summary>Reject shapes unsupported by the specialized hardware emitter.</summary>
    [Fact]
    public void Reject_unsupported_tap_counts()
    {
        foreach(int taps in new[]{0,1,17,257})
            Assert.Throws<ArgumentOutOfRangeException>(()=>HlsScheduledFir.Build(taps));
    }
}
