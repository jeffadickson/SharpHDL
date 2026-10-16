using System;
using System.Collections.Generic;
using System.Linq;
using SharpHDL.Examples;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>Cycle-level verification of single-port synchronous-RAM FIR scheduling.</summary>
public sealed class AxiFirRamTests
{
    /// <summary>
    /// Compare accepted AXI samples with a software four-tap oracle.
    /// This includes read latency, invalid initial history, wraparound,
    /// stalled output, and held input beats.
    /// </summary>
    [Fact]
    public void Single_port_ram_fir_preserves_history_and_transactions()
    {
        var module=AxiFirRam.Build();
        var sim=module.Simulate();
        var ports=module.Ports.ToDictionary(p=>p.Name,p=>p);
        void Set(string name,int value)=>sim.Set(ports[name],value);
        int Read(string name)=>(int)sim.Read(ports[name]);
        var history=new ushort[3];
        var expected=new Queue<(ushort Data,bool Last)>();
        var rng=new Random(101015);
        ushort[] coeff={3,5,7,11};
        sim.Tick(true);
        for(int i=0;i<4;i++)Set("coefficient"+i,coeff[i]);
        bool pending=false;
        ushort sample=0;
        bool last=false;
        int accepted=0,completed=0;
        for(int cycle=0;cycle<1300;cycle++)
        {
            if(!pending && cycle<1150 && rng.Next(4)!=0)
            {
                pending=true;
                sample=(ushort)rng.Next(65536);
                last=rng.Next(17)==0;
            }
            bool ready=cycle>=1150 || rng.Next(3)!=0;
            Set("s_axis_tvalid",pending?1:0);
            Set("s_axis_tdata",sample);
            Set("s_axis_tlast",last?1:0);
            Set("s_axis_tkeep",3);
            Set("s_axis_tuser",0);
            Set("m_axis_tready",ready?1:0);
            bool outputValid=Read("m_axis_tvalid")!=0;
            bool inputReady=Read("s_axis_tready")!=0;
            if(outputValid && ready)
            {
                Assert.NotEmpty(expected);
                var result=expected.Dequeue();
                Assert.Equal(result.Data,(ushort)Read("m_axis_tdata"));
                Assert.Equal(result.Last,Read("m_axis_tlast")!=0);
                Assert.Equal(3,Read("m_axis_tkeep"));
                completed++;
            }
            if(pending && inputReady)
            {
                ulong sum=(ulong)sample*coeff[0]+(ulong)history[0]*coeff[1]
                    +(ulong)history[1]*coeff[2]+(ulong)history[2]*coeff[3];
                expected.Enqueue(((ushort)sum,last));
                history[2]=history[1];
                history[1]=history[0];
                history[0]=sample;
                accepted++;
            }
            sim.Tick();
            if(pending && inputReady)pending=false;
        }
        Assert.False(pending);
        Assert.Empty(expected);
        Assert.Equal(accepted,completed);
        Assert.True(completed>30);
        Assert.Contains("fir_history",module.Verilog());
    }
}
