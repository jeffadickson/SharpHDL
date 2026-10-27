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

    /// <summary>Exercise signed branches, switch lowering, bitwise operations and unchecked arithmetic.</summary>
    public static uint Branch(uint x,int signed)
    {
        if(signed<0)return x^0x80000000u;
        switch(x&3)
        {
            case 0:return ~x;
            case 1:return unchecked(0u-x);
            case 2:return unchecked(x*3);
            default:return unchecked(x-7);
        }
    }

    /// <summary>Compare real C# branch/switch behavior over generated scalar inputs.</summary>
    [FsCheck.Xunit.Property(MaxTest=100,Replay="1145655947,296144285")]
    public void Scalar_control_flow_matches_native(uint x,int signed)
    {
        var d=CilCompiler.BuildScheduledSequential(typeof(ScheduledCilTests).GetMethod(nameof(Branch))!,"Branches",
            new SequentialOptions {MaxCycles=200}).Design;
        var sim=new HardwareGraphMachine(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);
        sim.SetInput("arg0",x);sim.SetInput("arg1",unchecked((uint)signed));sim.SetInput("start",1);
        sim.Tick();sim.SetInput("start",0);
        for(int edge=0;edge<201&&sim.ReadOutput("done")==0;edge++)sim.Tick();
        Assert.Equal(1,(int)sim.ReadOutput("done"));Assert.Equal(0,(int)sim.ReadOutput("fault"));
        Assert.Equal(Branch(x,signed),(uint)sim.ReadOutput("result"));
    }

    /// <summary>Initialized words reach the CIL load path and are not aliased with caller arrays.</summary>
    [Fact]
    public void Initial_memory_is_used_by_scheduled_cil()
    {
        System.Numerics.BigInteger[] initial={2,7};
        var d=CilCompiler.BuildScheduledSequential(typeof(ScheduledCilTests).GetMethod(nameof(Update))!,"Initial",
            new SequentialOptions {MemoryDepth=2,InitialMemory=initial,MaxCycles=200}).Design;
        initial[0]=100;
        var sim=new HardwareGraphMachine(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);
        sim.SetInput("arg1",2);sim.SetInput("arg2",3);sim.SetInput("start",1);sim.Tick();sim.SetInput("start",0);
        for(int edge=0;edge<201&&sim.ReadOutput("done")==0;edge++)sim.Tick();
        Assert.Equal(29,(int)sim.ReadOutput("result"));Assert.Equal(0,(int)sim.ReadOutput("fault"));
    }

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
