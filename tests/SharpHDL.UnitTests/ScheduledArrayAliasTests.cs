using System.Reflection.Emit;
using SharpHDL.Simulation;
using SharpHDL.Verification;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>Alias provenance is a null/bound lattice for one immutable hardware RAM binding.</summary>
public sealed class ScheduledArrayAliasTests
{
    /// <summary>Both paths and a backedge can copy the same bound array without allocating another memory.</summary>
    [FsCheck.Xunit.Property(MaxTest=60,Replay="1145655947,296144285")]
    public void Copied_aliases_share_RAM_and_match_CLR(byte seed)
    {
        var method=TypedCilFixture.Create(il=>
        {
            var other=il.DefineLabel();var join=il.DefineLabel();var loop=il.DefineLabel();var end=il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Ldc_I4_1);il.Emit(OpCodes.And);il.Emit(OpCodes.Brtrue,other);
            il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Stloc_0);il.Emit(OpCodes.Br,join);
            il.MarkLabel(other);il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Stloc_0);
            il.MarkLabel(join);il.Emit(OpCodes.Ldloc_0);il.Emit(OpCodes.Stloc_1);
            il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Ldc_I4_3);il.Emit(OpCodes.And);il.Emit(OpCodes.Stloc_2);
            il.MarkLabel(loop);il.Emit(OpCodes.Ldloc_2);il.Emit(OpCodes.Brfalse,end);
            il.Emit(OpCodes.Ldloc_1);il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Ldloc_0);il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_U4);il.Emit(OpCodes.Ldc_I4_1);il.Emit(OpCodes.Add);il.Emit(OpCodes.Stelem_I4);
            il.Emit(OpCodes.Ldloc_2);il.Emit(OpCodes.Ldc_I4_1);il.Emit(OpCodes.Sub);il.Emit(OpCodes.Stloc_2);il.Emit(OpCodes.Br,loop);
            il.MarkLabel(end);il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Ldelem_U4);il.Emit(OpCodes.Ret);
        },typeof(uint[]),typeof(uint[]),typeof(uint));
        uint[] native=[7,9];uint expected=(uint)method.Invoke(null,[native,(uint)seed])!;
        var d=CilCompiler.BuildScheduledSequential(method,"AliasCopy",new SequentialOptions{MemoryDepth=2,InitialMemory=[7,9],MaxCycles=300}).Design;
        Assert.Single(d.Memories);
        var sim=new HardwareGraphMachine(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);
        sim.SetInput("arg1",seed);sim.SetInput("start",1);sim.Tick();sim.SetInput("start",0);sim.SetInput("arg1",255-seed);
        for(int edge=0;edge<302&&sim.ReadOutput("done")==0;edge++)sim.Tick();
        Assert.Equal(1,(int)sim.ReadOutput("done"));Assert.Equal(0,(int)sim.ReadOutput("fault"));
        Assert.Equal(7u+(uint)(seed&3),expected);Assert.Equal(expected,(uint)sim.ReadOutput("result"));
    }

    /// <summary>Null/bound tag tests and reference equality agree with the actual CLR on both paths.</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public void Nullable_alias_branch_and_equality_match_CLR(bool bound)
    {
        var method=TypedCilFixture.Create(il=>
        {
            var join=il.DefineLabel();il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Brfalse,join);
            il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Stloc_0);il.MarkLabel(join);
            var nonnull=il.DefineLabel();il.Emit(OpCodes.Ldloc_0);il.Emit(OpCodes.Brtrue,nonnull);
            il.Emit(OpCodes.Ldloc_0);il.Emit(OpCodes.Ldnull);il.Emit(OpCodes.Ceq);il.Emit(OpCodes.Ret);
            il.MarkLabel(nonnull);il.Emit(OpCodes.Ldloc_0);il.Emit(OpCodes.Ldnull);il.Emit(OpCodes.Cgt_Un);
            il.Emit(OpCodes.Ldloc_0);il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ceq);il.Emit(OpCodes.Add);il.Emit(OpCodes.Ret);
        },typeof(uint[]));
        uint expected=bound?2u:1u;Assert.Equal(expected,(uint)method.Invoke(null,[new uint[2],bound?1u:0u])!);
        var d=CilCompiler.BuildScheduledSequential(method,"AliasNull").Design;
        var sim=new HardwareGraphMachine(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);
        sim.SetInput("arg1",bound?1:0);sim.SetInput("start",1);sim.Tick();sim.SetInput("start",0);
        for(int edge=0;edge<64&&sim.ReadOutput("done")==0;edge++)sim.Tick();
        Assert.Equal(1,(int)sim.ReadOutput("done"));Assert.Equal(expected,(uint)sim.ReadOutput("result"));
    }

    /// <summary>Unsafe dereferences diagnose initialized nulls, uncertain joins, numeric fabrication and rebinding.</summary>
    [Theory][InlineData("null")][InlineData("join")][InlineData("integer")][InlineData("rebind")][InlineData("order")]
    public void Unsafe_aliases_reject(string mode)
    {
        var method=TypedCilFixture.Create(il=>
        {
            if(mode=="integer"){il.Emit(OpCodes.Ldc_I4_1);il.Emit(OpCodes.Stloc_0);}
            if(mode=="rebind"){il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Starg_S,(byte)0);}
            if(mode=="join")
            {
                var join=il.DefineLabel();il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Brfalse,join);
                il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Stloc_0);il.MarkLabel(join);
            }
            if(mode=="order"){il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Cgt_Un);}
            else {il.Emit(OpCodes.Ldloc_0);il.Emit(OpCodes.Ldlen);il.Emit(OpCodes.Conv_U4);}
            il.Emit(OpCodes.Ret);
        },typeof(uint[]));
        var error=Assert.Throws<NotSupportedException>(()=>CilCompiler.BuildScheduledSequential(method,"RejectedAlias"));
        Assert.Contains("IL_",error.Message);Assert.Contains("Evaluate",error.Message);
    }

    /// <summary>A reachable backedge widening a previously bound local must revisit its dereference.</summary>
    [Fact]
    public void Backedge_null_assignment_invalidates_prior_dereference()
    {
        var method=TypedCilFixture.Create(il=>
        {
            var loop=il.DefineLabel();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Stloc_0);il.MarkLabel(loop);
            il.Emit(OpCodes.Ldloc_0);il.Emit(OpCodes.Ldlen);il.Emit(OpCodes.Pop);il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Stloc_0);il.Emit(OpCodes.Br,loop);
        },typeof(uint[]));
        Assert.Contains("definitely",Assert.Throws<NotSupportedException>(()=>CilCompiler.BuildScheduledSequential(method,"Backedge")).Message);
    }

    /// <summary>Provenance state has an explicit size limit rather than unbounded graph/local products.</summary>
    [Fact]
    public void Excessive_variable_state_rejects()
    {
        var method=TypedCilFixture.Create(il=>{il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Ret);},Enumerable.Repeat(typeof(uint[]),1023).ToArray());
        Assert.Contains("1024",Assert.Throws<NotSupportedException>(()=>CilCompiler.BuildScheduledSequential(method,"TooMany")).Message);
    }

    /// <summary>A straight alias store/load is compiled during the test, including mutation runs.</summary>
    [Fact]
    public void Bound_alias_storage_preserves_identity_and_length()
    {
        var method=TypedCilFixture.Create(il=>
        {
            il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Stloc_0);il.Emit(OpCodes.Ldloc_0);il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_U4);il.Emit(OpCodes.Ret);
        },typeof(uint[]));
        var d=CilCompiler.BuildScheduledSequential(method,"AliasLength",new SequentialOptions{MemoryDepth=3}).Design;
        Assert.Single(d.Memories);
        var sim=new HardwareGraphMachine(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);
        sim.SetInput("start",1);sim.Tick();sim.SetInput("start",0);
        for(int edge=0;edge<32&&sim.ReadOutput("done")==0;edge++)sim.Tick();
        Assert.Equal(1,(int)sim.ReadOutput("done"));Assert.Equal(3,(int)sim.ReadOutput("result"));
    }

    /// <summary>Reference conditional equality uses identity tags and resets local aliases on each start.</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public void Reference_equality_branches_and_alias_reset_match_CLR(bool unequal)
    {
        var method=TypedCilFixture.Create(il=>
        {
            var join=il.DefineLabel();var yes=il.DefineLabel();il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Brfalse,join);
            il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Stloc_0);il.MarkLabel(join);
            il.Emit(OpCodes.Ldloc_0);il.Emit(OpCodes.Ldarg_0);il.Emit(unequal?OpCodes.Bne_Un:OpCodes.Beq,yes);
            il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Ret);il.MarkLabel(yes);il.Emit(OpCodes.Ldc_I4_1);il.Emit(OpCodes.Ret);
        },typeof(uint[]));
        var d=CilCompiler.BuildScheduledSequential(method,"AliasEqual").Design;
        var sim=new HardwareGraphMachine(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);
        foreach(uint bound in new uint[]{1,0,1,0})
        {
            uint expected=(uint)method.Invoke(null,[new uint[16],bound])!;
            Assert.Equal(unequal?1-bound:bound,expected);
            sim.SetInput("arg1",bound);sim.SetInput("start",1);sim.Tick();sim.SetInput("start",0);sim.SetInput("arg1",1-bound);
            for(int edge=0;edge<64&&sim.ReadOutput("done")==0;edge++)sim.Tick();
            Assert.Equal(1,(int)sim.ReadOutput("done"));Assert.Equal(expected,(uint)sim.ReadOutput("result"));
        }
    }
}
