using System.Reflection;
using System.Reflection.Emit;
using SharpHDL.Simulation;
using SharpHDL.Verification;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>Guarded loads and stale predicates challenge edge-specific null facts.</summary>
public sealed class ScheduledNullRefinementTests
{
    private static MethodInfo Guarded(string mode)=>TypedCilFixture.Create(il=>
    {
        var join=il.DefineLabel();var load=il.DefineLabel();var missing=il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Brfalse,join);il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Stloc_0);il.MarkLabel(join);
        il.Emit(OpCodes.Ldloc_0);
        if(mode is "ceq" or "beq" or "bne" or "cgt" or "bool" or "negated")il.Emit(OpCodes.Ldnull);
        if(mode=="ceq")il.Emit(OpCodes.Ceq);
        if(mode is "cgt" or "bool" or "negated")il.Emit(OpCodes.Cgt_Un);
        if(mode=="bool"){il.Emit(OpCodes.Stloc_1);il.Emit(OpCodes.Ldloc_1);}
        if(mode=="negated"){il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Ceq);}
        if(mode=="brtrue"||mode=="bne")
        {il.Emit(mode=="bne"?OpCodes.Bne_Un:OpCodes.Brtrue,load);il.Emit(OpCodes.Ldc_I4,17);il.Emit(OpCodes.Ret);}
        else il.Emit(mode=="beq"?OpCodes.Beq:mode is "ceq" or "negated"?OpCodes.Brtrue:OpCodes.Brfalse,missing);
        il.MarkLabel(load);il.Emit(OpCodes.Ldloc_0);il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Ldelem_U4);il.Emit(OpCodes.Ret);
        il.MarkLabel(missing);il.Emit(OpCodes.Ldc_I4,17);il.Emit(OpCodes.Ret);
    },typeof(uint[]),typeof(bool));

    /// <summary>Direct, equality, inverted and stored-Boolean guards retain CLR results across repeated starts.</summary>
    [Theory][InlineData("brtrue")][InlineData("brfalse")][InlineData("ceq")][InlineData("cgt")]
    [InlineData("beq")][InlineData("bne")][InlineData("bool")][InlineData("negated")]
    public void Guarded_nullable_load_matches_CLR(string mode)
    {
        var method=Guarded(mode);var d=CilCompiler.BuildScheduledSequential(method,"Guarded",new SequentialOptions{MemoryDepth=2,InitialMemory=[91,2]}).Design;
        var sim=new HardwareGraphMachine(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);
        foreach(uint bound in new uint[]{0,1,0,1})
        {
            uint expected=(uint)method.Invoke(null,[new uint[]{91,2},bound])!;Assert.Equal(bound==0?17u:91u,expected);
            sim.SetInput("arg1",bound);sim.SetInput("start",1);sim.Tick();sim.SetInput("start",0);sim.SetInput("arg1",1-bound);
            for(int edge=0;edge<96&&sim.ReadOutput("done")==0;edge++)sim.Tick();
            Assert.Equal(1,(int)sim.ReadOutput("done"));Assert.Equal(0,(int)sim.ReadOutput("fault"));Assert.Equal(expected,(uint)sim.ReadOutput("result"));
        }
    }

    /// <summary>Mutating the tested local invalidates facts on Boolean locals and older evaluation-stack copies.</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public void Reassigned_local_cannot_reuse_a_stale_guard(bool stackCopy)
    {
        var method=TypedCilFixture.Create(il=>
        {
            var missing=il.DefineLabel();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Stloc_0);il.Emit(OpCodes.Ldloc_0);
            if(!stackCopy){il.Emit(OpCodes.Ldnull);il.Emit(OpCodes.Cgt_Un);il.Emit(OpCodes.Stloc_1);}
            il.Emit(OpCodes.Ldnull);il.Emit(OpCodes.Stloc_0);if(!stackCopy)il.Emit(OpCodes.Ldloc_1);
            il.Emit(OpCodes.Brfalse,missing);il.Emit(OpCodes.Ldloc_0);il.Emit(OpCodes.Ldlen);il.Emit(OpCodes.Conv_U4);il.Emit(OpCodes.Ret);
            il.MarkLabel(missing);il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Ret);
        },typeof(uint[]),typeof(bool));
        Assert.Contains("definitely",Assert.Throws<NotSupportedException>(()=>CilCompiler.BuildScheduledSequential(method,"StaleGuard")).Message);
    }

    /// <summary>A branch with identical target/fallthrough must not delete the common successor.</summary>
    [Fact]
    public void Identical_successors_remain_reachable()
    {
        var method=TypedCilFixture.Create(il=>
        {
            var next=il.DefineLabel();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Brfalse,next);il.MarkLabel(next);
            il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldlen);il.Emit(OpCodes.Conv_U4);il.Emit(OpCodes.Ret);
        });
        Assert.NotNull(CilCompiler.BuildScheduledSequential(method,"SameSuccessor"));
    }
}
