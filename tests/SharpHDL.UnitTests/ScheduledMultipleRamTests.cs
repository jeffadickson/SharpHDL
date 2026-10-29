using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using SharpHDL.Simulation;
using SharpHDL.Verification;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>Distinct static RAM origins dispatch by tags and retain separate bounds/contents.</summary>
public sealed class ScheduledMultipleRamTests
{
    private static MethodInfo Kernel()=>TypedCilFixture.CreateWithParameters([typeof(uint[]),typeof(uint[]),typeof(uint)],il=>
    {
        var right=il.DefineLabel();var join=il.DefineLabel();il.Emit(OpCodes.Ldarg_2);il.Emit(OpCodes.Brtrue,right);
        il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Br,join);il.MarkLabel(right);il.Emit(OpCodes.Ldarg_1);
        il.MarkLabel(join);il.Emit(OpCodes.Stloc_0);
        il.Emit(OpCodes.Ldloc_0);il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Ldarg_2);il.Emit(OpCodes.Stelem_I4);
        il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Ldelem_U4);
        il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Ldelem_U4);il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc_0);il.Emit(OpCodes.Ldlen);il.Emit(OpCodes.Conv_U4);il.Emit(OpCodes.Ldc_I4,100);il.Emit(OpCodes.Mul);il.Emit(OpCodes.Add);il.Emit(OpCodes.Ret);
    },typeof(uint[]));

    /// <summary>Origin unions at a join select one memory while later accesses observe both physical arrays.</summary>
    [Fact]
    public void Selected_alias_observes_separate_contents_and_lengths()
    {
        var method=Kernel();var options=new SequentialOptions{MaxCycles=200,MemoryDepths=new Dictionary<int,int>{{0,2},{1,3}},
            InitialMemories=new Dictionary<int,BigInteger[]>{{0,[7,9]},{1,[21,22,23]}}};
        var d=CilCompiler.BuildScheduledSequential(method,"TwoRams",options).Design;Assert.Equal(2,d.Memories.Count);
        var sim=new HardwareGraphMachine(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);
        uint[] left=[7,9],right=[21,22,23];
        foreach(uint choice in new uint[]{0,1,0,1})
        {
            uint expected=(uint)method.Invoke(null,[left,right,choice])!;
            sim.SetInput("arg2",choice);sim.SetInput("start",1);sim.Tick();sim.SetInput("start",0);sim.SetInput("arg2",1-choice);
            for(int i=0;i<202&&sim.ReadOutput("done")==0;i++)sim.Tick();
            Assert.Equal(1,(int)sim.ReadOutput("done"));Assert.Equal(0,(int)sim.ReadOutput("fault"));Assert.Equal(expected,(uint)sim.ReadOutput("result"));
        }
    }

    /// <summary>Each memory enforces its own depth, rather than the largest configured depth.</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public void Bounds_are_checked_for_the_actual_origin(bool right)
    {
        var method=TypedCilFixture.CreateWithParameters([typeof(uint[]),typeof(uint[])],il=>
        {il.Emit(right?OpCodes.Ldarg_1:OpCodes.Ldarg_0);il.Emit(OpCodes.Ldc_I4_2);il.Emit(OpCodes.Ldelem_U4);il.Emit(OpCodes.Ret);});
        var d=CilCompiler.BuildScheduledSequential(method,"OriginBounds",new SequentialOptions{MemoryDepths=new Dictionary<int,int>{{0,2},{1,3}},InitialMemories=new Dictionary<int,BigInteger[]>{{1,[11,12,13]}}}).Design;
        var sim=new HardwareGraphMachine(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);
        sim.SetInput("start",1);sim.Tick();sim.SetInput("start",0);for(int i=0;i<32&&sim.ReadOutput("done")==0;i++)sim.Tick();
        Assert.Equal(1,(int)sim.ReadOutput("done"));Assert.Equal(right?0:1,(int)sim.ReadOutput("fault"));Assert.Equal(right?13:0,(int)sim.ReadOutput("result"));
    }

    /// <summary>Ambiguous legacy initialization, non-RAM keys, bad depths and excessive origin counts reject.</summary>
    [Fact]
    public void Invalid_binding_configuration_rejects()
    {
        var method=Kernel();
        Assert.Throws<NotSupportedException>(()=>CilCompiler.BuildScheduledSequential(method,"Legacy",new SequentialOptions{InitialMemory=[1]}));
        Assert.Throws<ArgumentException>(()=>CilCompiler.BuildScheduledSequential(method,"WrongKey",new SequentialOptions{MemoryDepths=new Dictionary<int,int>{{2,4}}}));
        foreach(int depth in new[]{0,65537})Assert.Throws<ArgumentOutOfRangeException>(()=>CilCompiler.BuildScheduledSequential(method,"Depth",new SequentialOptions{MemoryDepths=new Dictionary<int,int>{{1,depth}}}));
        var many=TypedCilFixture.CreateWithParameters(Enumerable.Repeat(typeof(uint[]),9).ToArray(),il=>{il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Ret);});
        Assert.Throws<NotSupportedException>(()=>CilCompiler.BuildScheduledSequential(many,"TooMany"));
        var scalar=TypedCilFixture.Create(il=>{il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Ret);});
        Assert.Contains("scheduled backend",Assert.Throws<NotSupportedException>(()=>CilCompiler.BuildSequential(scalar,"LegacyOptions",new SequentialOptions{MemoryDepths=new Dictionary<int,int>{{0,2}}})).Message);
    }

    /// <summary>The highest admitted origin survives local tag storage without truncating tag eight to zero.</summary>
    [Fact]
    public void Eighth_origin_is_representable_in_a_local()
    {
        var method=TypedCilFixture.CreateWithParameters(Enumerable.Repeat(typeof(uint[]),8).ToArray(),il=>
        {il.Emit(OpCodes.Ldarg_S,(byte)7);il.Emit(OpCodes.Stloc_0);il.Emit(OpCodes.Ldloc_0);il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Ldelem_U4);il.Emit(OpCodes.Ret);},typeof(uint[]));
        var d=CilCompiler.BuildScheduledSequential(method,"EightRams",new SequentialOptions{MemoryDepth=2,InitialMemories=new Dictionary<int,BigInteger[]>{{7,[87,1]}}}).Design;
        Assert.Equal(8,d.Memories.Count);var sim=new HardwareGraphMachine(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);
        sim.SetInput("start",1);sim.Tick();sim.SetInput("start",0);for(int i=0;i<64&&sim.ReadOutput("done")==0;i++)sim.Tick();
        Assert.Equal(1,(int)sim.ReadOutput("done"));Assert.Equal(0,(int)sim.ReadOutput("fault"));Assert.Equal(87,(int)sim.ReadOutput("result"));
    }
}
