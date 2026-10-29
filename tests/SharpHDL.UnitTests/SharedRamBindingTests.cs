using System.Numerics;
using System.Reflection.Emit;
using SharpHDL.Simulation;
using SharpHDL.Verification;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>Explicit resource identity, configuration consistency and canonical chains.</summary>
public sealed class SharedRamBindingTests
{
    [Theory][InlineData(false)][InlineData(true)]
    public void Shared_or_distinct_binding_agrees_with_actual_CLR_objects(bool shared)
    {
        var method=typeof(SharpHDL.Examples.ScheduledSharedRam).GetMethod("Update")!;
        var d=CilCompiler.BuildScheduledSequential(method,"Shared",new SequentialOptions{MemoryDepth=3,
            RamAliases=shared?new Dictionary<int,int>{{1,0}}:null!}).Design;
        Assert.Equal(shared?1:2,d.Memories.Count);
        var sim=new HardwareGraphMachine(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);
        uint[] left=new uint[3],right=shared?left:new uint[3];
        foreach(uint word in new[]{1u,uint.MaxValue,37u})
        {
            uint expected=SharpHDL.Examples.ScheduledSharedRam.Update(left,right,1,word);
            sim.SetInput("arg2",1);sim.SetInput("arg3",word);sim.SetInput("start",1);sim.Tick();sim.SetInput("start",0);
            for(int i=0;i<96&&sim.ReadOutput("done")==0;i++)sim.Tick();
            Assert.Equal(1,(int)sim.ReadOutput("done"));Assert.Equal(0,(int)sim.ReadOutput("fault"));Assert.Equal(expected,(uint)sim.ReadOutput("result"));
        }
    }

    [Fact]
    public void Cycles_bad_keys_and_conflicting_alias_metadata_reject()
    {
        var m=typeof(SharpHDL.Examples.ScheduledSharedRam).GetMethod("Update")!;
        foreach(var bindings in new[]{new Dictionary<int,int>{{0,0}},new Dictionary<int,int>{{0,1},{1,0}}})
            Assert.Contains("cycle",Assert.Throws<NotSupportedException>(()=>CilCompiler.BuildScheduledSequential(m,"Cycle",new SequentialOptions{RamAliases=bindings})).Message);
        Assert.Contains("parameter indexes",Assert.Throws<ArgumentException>(()=>CilCompiler.BuildScheduledSequential(m,"Scalar",new SequentialOptions{RamAliases=new Dictionary<int,int>{{1,2}}})).Message);
        Assert.Contains("depths",Assert.Throws<ArgumentException>(()=>CilCompiler.BuildScheduledSequential(m,"DepthConflict",new SequentialOptions
        {RamAliases=new Dictionary<int,int>{{1,0}},MemoryDepths=new Dictionary<int,int>{{0,2},{1,3}}})).Message);
        Assert.Contains("initialization",Assert.Throws<ArgumentException>(()=>CilCompiler.BuildScheduledSequential(m,"InitConflict",new SequentialOptions
        {MemoryDepth=2,RamAliases=new Dictionary<int,int>{{1,0}},InitialMemories=new Dictionary<int,BigInteger[]>{{0,[1,2]},{1,[3,4]}}})).Message);
    }

    [Fact]
    public void Chains_inherit_canonical_depth_and_contents()
    {
        var m=TypedCilFixture.CreateWithParameters([typeof(uint[]),typeof(uint[]),typeof(uint[])],il=>
        {il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Ldelem_U4);il.Emit(OpCodes.Ret);});
        var d=CilCompiler.BuildScheduledSequential(m,"Chain",new SequentialOptions{RamAliases=new Dictionary<int,int>{{0,1},{1,2}},
            MemoryDepths=new Dictionary<int,int>{{2,2}},InitialMemories=new Dictionary<int,BigInteger[]>{{2,[41,42]}}}).Design;
        var p=Assert.Single(d.Memories).Value;Assert.Equal(new BigInteger[]{41,42},p.Initial);
    }
}
