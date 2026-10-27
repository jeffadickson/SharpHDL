using System.Reflection;
using SharpHDL.HLS;
using SharpHDL.Simulation;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>Actual C# runtime loops lowered through shared CIL analysis to scheduled Core.</summary>
public sealed class ScheduledCilTests
{
    /// <summary>Test kernel with runtime loop, RAM loads/stores and loop-carried arithmetic.</summary>
    public static uint Update(uint[] words,uint count,uint factor)
    {
        uint sum=0;
        for(uint i=0;i<count;i++)
        {
            uint value=words[i]*factor+1;
            words[i]=value;
            sum+=value;
        }
        return sum;
    }

    /// <summary>Host-only division is intentionally outside the scheduled subset.</summary>
    public static uint Unsupported(uint x)=>x/3;

    /// <summary>Compare native C#, legacy RTL semantics and scheduled Core edge by edge.</summary>
    [Fact]
    public void Stateful_loop_matches_host_and_legacy_across_repeated_calls()
    {
        var method=typeof(ScheduledCilTests).GetMethod(nameof(Update))!;
        var options=new SequentialOptions {MemoryDepth=8,MaxCycles=1000};
        var compilation=CilCompiler.BuildScheduledSequential(method,"ScheduledUpdate",options);
        Assert.NotEmpty(compilation.StateOffsets);Assert.Same(method,compilation.Method);
        Assert.Single(compilation.Design.Graph.Nodes,n=>n.Operation==Core.HardwareOperation.Multiply);
        var d=compilation.Design;
        var sim=new HardwareGraphMachine(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);
        var legacy=CilCompiler.BuildSequential(method,"LegacyUpdate",options);
        var old=legacy.Simulate();var ports=legacy.Ports.ToDictionary(p=>p.Name);
        old.Tick(true);var expected=new uint[8];
        foreach(uint count in new uint[]{0,1,8,4,8})
        {
            uint want=Update(expected,count,3);
            foreach(var input in new[]{("start",1u),("arg1",count),("arg2",3u)})
            {sim.SetInput(input.Item1,input.Item2);old.Set(ports[input.Item1],input.Item2);}
            bool done=false;
            for(int edge=0;edge<1002;edge++)
            {
                sim.Tick();old.Tick();
                foreach(string output in new[]{"busy","done","fault","timeout","result","accept"})
                    Assert.Equal(old.Read(ports[output]),sim.ReadOutput(output));
                if(edge==0){sim.SetInput("start",0);old.Set(ports["start"],0);}
                if(sim.ReadOutput("done")==1){done=true;break;}
            }
            Assert.True(done);Assert.Equal(0,(int)sim.ReadOutput("fault"));Assert.Equal(want,(uint)sim.ReadOutput("result"));
        }
    }

    /// <summary>Out-of-bounds and nonterminating work fault rather than silently succeeding.</summary>
    [Theory]
    [InlineData(9,1000,false)]
    [InlineData(8,2,true)]
    public void Bounds_and_watchdog_faults_are_explicit(uint count,int maxCycles,bool timeout)
    {
        var d=CilCompiler.BuildScheduledSequential(typeof(ScheduledCilTests).GetMethod(nameof(Update))!,"Fault",
            new SequentialOptions {MemoryDepth=8,MaxCycles=maxCycles}).Design;
        var sim=new HardwareGraphMachine(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);
        sim.SetInput("start",1);sim.SetInput("arg1",count);sim.SetInput("arg2",1);sim.Tick();sim.SetInput("start",0);
        for(int edge=0;edge<1002&&sim.ReadOutput("done")==0;edge++)sim.Tick();
        Assert.Equal(1,(int)sim.ReadOutput("done"));Assert.Equal(1,(int)sim.ReadOutput("fault"));
        Assert.Equal(timeout?1:0,(int)sim.ReadOutput("timeout"));
        sim.Reset();Assert.Equal(0,(int)sim.ReadOutput("busy"));Assert.Equal(0,(int)sim.ReadOutput("fault"));
    }

    /// <summary>Unsupported constructs report method and original IL location without fallback.</summary>
    [Fact]
    public void Unsupported_opcodes_report_original_method_and_il()
    {
        var error=Assert.Throws<NotSupportedException>(()=>CilCompiler.BuildScheduledSequential(
            typeof(ScheduledCilTests).GetMethod(nameof(Unsupported))!,"Unsupported"));
        Assert.Contains(nameof(Unsupported),error.Message);Assert.Contains("IL_",error.Message);Assert.Contains("div",error.Message);
    }
}
