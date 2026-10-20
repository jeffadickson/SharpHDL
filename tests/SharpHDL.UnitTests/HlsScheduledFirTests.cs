using System;
using System.Collections.Generic;
using System.Linq;
using SharpHDL.Core;
using SharpHDL.Examples;
using SharpHDL.HLS;
using SharpHDL.Simulation;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>End-to-end checks for scheduled-machine FIR Core IR.</summary>
public sealed class HlsScheduledFirTests
{
    /// <summary>Compare Core IR execution against independent convolution under randomized stalls.</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    public void Core_scheduled_controller_matches_reference(int taps)
    {
        var design=HlsScheduledFir.Build(taps);
        var sim=Machine(design);
        var coefficients=Enumerable.Range(0,taps).Select(i=>(ushort)(i*7+3)).ToArray();
        for(int i=0;i<taps;i++)sim.SetInput("coefficient"+i,coefficients[i]);
        var history=new ushort[taps-1];
        var expected=new Queue<(ushort Data,bool Last)>();
        var random=new Random(1500+taps);
        bool pending=false;ushort sample=0;bool last=false;int accepted=0,completed=0;
        int cycles=1000+160*taps;
        for(int cycle=0;cycle<cycles;cycle++)
        {
            if(!pending&&cycle<cycles-200&&random.Next(4)!=0)
            {pending=true;sample=(ushort)random.Next(65536);last=random.Next(11)==0;}
            bool ready=cycle>=cycles-200||random.Next(3)!=0;
            SetBeat(sim,pending,sample,last,ready);
            bool take=pending&&sim.ReadOutput("s_axis_tready")!=0;
            bool give=ready&&sim.ReadOutput("m_axis_tvalid")!=0;
            if(give)
            {
                var next=expected.Dequeue();
                Assert.Equal(next.Data,(ushort)sim.ReadOutput("m_axis_tdata"));
                Assert.Equal(next.Last,sim.ReadOutput("m_axis_tlast")!=0);
                Assert.Equal(3,(int)sim.ReadOutput("m_axis_tkeep"));completed++;
            }
            if(take)
            {
                ulong sum=(ulong)sample*coefficients[0];
                for(int i=1;i<taps;i++)sum+=(ulong)history[i-1]*coefficients[i];
                expected.Enqueue(((ushort)sum,last));
                for(int i=history.Length-1;i>0;i--)history[i]=history[i-1];
                history[0]=sample;accepted++;
            }
            sim.Tick();if(take)pending=false;
        }
        Assert.False(pending);Assert.Empty(expected);Assert.Equal(accepted,completed);
        Assert.True(completed>taps*2);
        Assert.Single(design.Graph.Nodes,n=>n.Operation==HardwareOperation.Multiply);
        Assert.Single(design.Memories);
    }

    /// <summary>The generic Core backend and existing structural four-tap FIR agree transaction by transaction.</summary>
    [Fact]
    public void Four_tap_core_backend_matches_structural_implementation()
    {
        ushort[] coefficients={3,10,17,24};
        ushort[] samples={5,11,0,65535,1234,42,77,9};
        var core=RunCore(samples,coefficients);
        var structural=RunStructural(samples,coefficients);
        Assert.Equal(structural,core);
    }

    /// <summary>Single-port arbitration rejects two accesses assigned to one state.</summary>
    [Fact]
    public void Scheduled_machine_rejects_resource_oversubscription()
    {
        var builder=new ScheduledMachineBuilder("Conflict");
        var address=builder.Input("address",1);var data=builder.Input("data",8);
        var target=builder.Register("target",8);var memory=builder.Memory("ram",8,2);
        builder.State("only").Read(memory,address,target,builder.Constant(1,1))
            .Write(memory,address,data,builder.Constant(1,1));
        builder.Output("result",target.Value);
        Assert.Throws<InvalidOperationException>(()=>builder.Lower());
    }

    /// <summary>Reject tap counts outside the current FIR mapping range.</summary>
    [Fact]
    public void Reject_unsupported_tap_counts()
    {
        foreach(int taps in new[]{0,1,17,257})
            Assert.ThrowsAny<ArgumentOutOfRangeException>(()=>HlsScheduledFir.Build(taps));
    }

    private static HardwareGraphMachine Machine(ScheduledHardwareDesign design)
        =>new(design.Graph,design.Inputs,design.Outputs,design.NextState,
            design.ResetValues,design.Memories);

    private static void SetBeat(HardwareGraphMachine sim,bool valid,ushort data,bool last,bool ready)
    {
        sim.SetInput("s_axis_tvalid",valid?1:0);sim.SetInput("s_axis_tdata",data);
        sim.SetInput("s_axis_tlast",last?1:0);sim.SetInput("s_axis_tkeep",3);
        sim.SetInput("s_axis_tuser",0);sim.SetInput("m_axis_tready",ready?1:0);
    }

    private static ushort[] RunCore(ushort[] samples,ushort[] coefficients)
    {
        var sim=Machine(HlsScheduledFir.Build(4));
        for(int i=0;i<4;i++)sim.SetInput("coefficient"+i,coefficients[i]);
        var output=new List<ushort>();int source=0;
        for(int cycle=0;cycle<500&&output.Count<samples.Length;cycle++)
        {
            bool valid=source<samples.Length;ushort sample=valid?samples[source]:(ushort)0;
            SetBeat(sim,valid,sample,false,true);
            if(valid&&sim.ReadOutput("s_axis_tready")!=0)source++;
            if(sim.ReadOutput("m_axis_tvalid")!=0)output.Add((ushort)sim.ReadOutput("m_axis_tdata"));
            sim.Tick();
        }
        return output.ToArray();
    }

    private static ushort[] RunStructural(ushort[] samples,ushort[] coefficients)
    {
        var module=AxiFirStructural.Build();var sim=module.Simulate();
        var ports=module.Ports.ToDictionary(p=>p.Name,p=>p);
        void Set(string name,int value)=>sim.Set(ports[name],value);
        for(int i=0;i<4;i++)Set("coefficient"+i,coefficients[i]);
        Set("s_axis_tkeep",3);Set("m_axis_tready",1);sim.Tick(true);
        var output=new List<ushort>();int source=0;
        for(int cycle=0;cycle<100&&output.Count<samples.Length;cycle++)
        {
            bool valid=source<samples.Length;Set("s_axis_tvalid",valid?1:0);
            Set("s_axis_tdata",valid?samples[source]:0);Set("s_axis_tlast",0);Set("s_axis_tuser",0);
            if(valid&&sim.Read(ports["s_axis_tready"])!=0)source++;
            if(sim.Read(ports["m_axis_tvalid"])!=0)output.Add((ushort)sim.Read(ports["m_axis_tdata"]));
            sim.Tick();
        }
        return output.ToArray();
    }
}
