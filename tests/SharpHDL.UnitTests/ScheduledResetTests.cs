using SharpHDL.Examples;
using SharpHDL.HLS;
using SharpHDL.Simulation;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>Directed reset epochs and independently scheduled machine isolation.</summary>
public sealed class ScheduledResetTests
{
    /// <summary>Reset at every phase suppresses that edge and retains only committed writes.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void Reset_at_each_table_phase_retains_exact_commits(int phase)
    {
        var sim=Machine(ScheduledTable.Build(1));
        Request(sim,true,5);sim.Tick();sim.Tick();sim.Tick();
        sim.SetInput("response_ready",1);sim.Tick();sim.SetInput("response_ready",0);
        Request(sim,true,7);for(int i=0;i<phase;i++)sim.Tick();
        sim.Reset();sim.SetInput("request_valid",0);
        Assert.Equal(0,(int)sim.ReadOutput("response_valid"));
        Request(sim,false,0);sim.Tick();sim.SetInput("request_valid",0);sim.Tick();sim.Tick();
        Assert.Equal(phase==3?12:5,(int)sim.ReadOutput("response_data"));
        for(int i=0;i<5;i++){sim.Tick();Assert.Equal(1,(int)sim.ReadOutput("response_valid"));Assert.Equal(phase==3?12:5,(int)sim.ReadOutput("response_data"));}
    }

    /// <summary>Interleaving independent kernels cannot advance or reset the other kernel.</summary>
    [Fact]
    public void Independently_scheduled_kernels_preserve_separate_state()
    {
        var d=ScheduledTable.Build(1);var a=Machine(d);var b=Machine(d);
        Request(a,true,17);Request(b,true,42);
        for(int i=0;i<3;i++){a.Tick();Assert.Equal(0,b.Cycle);}
        Assert.Equal(17,(int)a.ReadOutput("response_data"));
        for(int i=0;i<3;i++)b.Tick();
        a.Reset();Assert.Equal(42,(int)b.ReadOutput("response_data"));Assert.Equal(1,(int)b.ReadOutput("response_valid"));
    }

    private static void Request(HardwareGraphMachine sim,bool update,int delta)
    {sim.SetInput("request_valid",1);sim.SetInput("request_address",1);sim.SetInput("request_delta",delta);sim.SetInput("request_update",update?1:0);}
    private static HardwareGraphMachine Machine(ScheduledHardwareDesign d)
        =>new(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);
}
