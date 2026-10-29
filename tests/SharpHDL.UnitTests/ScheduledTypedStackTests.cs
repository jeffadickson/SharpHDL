using System.Reflection.Emit;
using SharpHDL.Simulation;
using SharpHDL.Verification;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>Same-height invalid programs must fail before hardware allocation, with method/IL context.</summary>
public sealed class ScheduledTypedStackTests
{
    /// <summary>Reject reference/integer confusion at every numeric and memory operand role.</summary>
    [Theory][InlineData("array")][InlineData("index")][InlineData("value")][InlineData("add")]
    [InlineData("convert")][InlineData("return")][InlineData("store")][InlineData("ordered")][InlineData("length")]
    public void Incompatible_operands_reject_with_context(string kind)
    {
        var method=TypedCilFixture.Create(il=>
        {
            if(kind=="array"){il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Ldelem_U4);}
            else if(kind=="index"){il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldelem_U4);}
            else if(kind=="value"){il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Stelem_I4);il.Emit(OpCodes.Ldc_I4_0);}
            else if(kind=="length"){il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldlen);il.Emit(OpCodes.Ldc_I4,32);il.Emit(OpCodes.Shl);il.Emit(OpCodes.Conv_U4);}
            else
            {
                il.Emit(OpCodes.Ldarg_0);
                if(kind=="add"){il.Emit(OpCodes.Ldc_I4_1);il.Emit(OpCodes.Add);}
                if(kind=="convert")il.Emit(OpCodes.Conv_U4);
                if(kind=="store"){il.Emit(OpCodes.Stloc_0);il.Emit(OpCodes.Ldc_I4_0);}
                if(kind=="ordered"){il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Clt);}
            }
            il.Emit(OpCodes.Ret);
        },typeof(uint));
        var error=Assert.Throws<NotSupportedException>(()=>CilCompiler.BuildScheduledSequential(method,"BadTyped"));
        Assert.Contains("IL_",error.Message);Assert.Contains("Evaluate",error.Message);
        Assert.True(error.Message.Contains("stack",StringComparison.OrdinalIgnoreCase)||error.Message.Contains("reference",StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Merge categories must agree even when both paths have the same stack depth.</summary>
    [Fact]
    public void Same_height_reference_integer_merge_rejects()
    {
        var method=TypedCilFixture.Create(il=>
        {
            var other=il.DefineLabel();var join=il.DefineLabel();il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Brtrue,other);
            il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Br,join);il.MarkLabel(other);il.Emit(OpCodes.Ldc_I4_0);
            il.MarkLabel(join);il.Emit(OpCodes.Pop);il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Ret);
        });
        Assert.Contains("merge",Assert.Throws<NotSupportedException>(()=>CilCompiler.BuildScheduledSequential(method,"Merge")).Message);
    }

    /// <summary>All declared-variable uses validate, including unreachable code.</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public void Invalid_variable_indexes_reject_before_lowering(bool local)
    {
        var method=TypedCilFixture.Create(il=>
        {
            var end=il.DefineLabel();il.Emit(OpCodes.Br,end);
            il.Emit(local?OpCodes.Ldloc_S:OpCodes.Ldarg_S,(byte)9);il.Emit(OpCodes.Pop);
            il.MarkLabel(end);il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Ret);
        },typeof(uint));
        Assert.Contains("index",Assert.Throws<NotSupportedException>(()=>CilCompiler.BuildScheduledSequential(method,"Index")).Message);
    }

    /// <summary>Generated invalid variable operands must diagnose rather than indexing host arrays.</summary>
    [FsCheck.Xunit.Property(MaxTest=100,Replay="1145655947,296144285")]
    public void Generated_out_of_range_variables_have_IL_diagnostics(byte index,bool local)
    {
        var method=TypedCilFixture.Create(il=>
        {
            il.Emit(local?OpCodes.Ldloc:OpCodes.Ldarg,(short)(index+2));il.Emit(OpCodes.Ret);
        },typeof(uint));
        var error=Assert.Throws<NotSupportedException>(()=>CilCompiler.BuildScheduledSequential(method,"GeneratedIndex"));
        Assert.Contains("index",error.Message);Assert.Contains("IL_0000",error.Message);
    }

    /// <summary>Duplicated bound references and explicit bounded length normalization preserve CLR behavior.</summary>
    [Fact]
    public void Reference_dup_and_normalized_length_are_valid()
    {
        var method=TypedCilFixture.Create(il=>
        {
            il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Dup);il.Emit(OpCodes.Ldlen);il.Emit(OpCodes.Conv_U4);
            il.Emit(OpCodes.Pop);il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Ldelem_U4);il.Emit(OpCodes.Ret);
        });
        Assert.Equal(71u,(uint)method.Invoke(null,[new uint[]{71,3},0u])!);
        var d=CilCompiler.BuildScheduledSequential(method,"TypedRam",new SequentialOptions{MemoryDepth=2,InitialMemory=[71,3]}).Design;
        var sim=new HardwareGraphMachine(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);
        sim.SetInput("start",1);sim.Tick();sim.SetInput("start",0);
        for(int edge=0;edge<64&&sim.ReadOutput("done")==0;edge++)sim.Tick();
        Assert.Equal(1,(int)sim.ReadOutput("done"));Assert.Equal(0,(int)sim.ReadOutput("fault"));Assert.Equal(71,(int)sim.ReadOutput("result"));
    }

    /// <summary>Reference categories remain stable across a reachable backedge with a live stack value.</summary>
    [Fact]
    public void Reference_carried_across_a_loop_has_a_consistent_type()
    {
        var method=TypedCilFixture.Create(il=>
        {
            var loop=il.DefineLabel();var end=il.DefineLabel();il.Emit(OpCodes.Ldarg_0);il.MarkLabel(loop);
            il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Brfalse,end);il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Sub);il.Emit(OpCodes.Starg_S,(byte)1);il.Emit(OpCodes.Br,loop);
            il.MarkLabel(end);il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Ldelem_U4);il.Emit(OpCodes.Ret);
        });
        Assert.Equal(71u,(uint)method.Invoke(null,[new uint[]{71,3},3u])!);
        var d=CilCompiler.BuildScheduledSequential(method,"TypedLoop",new SequentialOptions{MemoryDepth=2,InitialMemory=[71,3]}).Design;
        var sim=new HardwareGraphMachine(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);
        sim.SetInput("arg1",3);sim.SetInput("start",1);sim.Tick();sim.SetInput("start",0);
        for(int edge=0;edge<128&&sim.ReadOutput("done")==0;edge++)sim.Tick();
        Assert.Equal(1,(int)sim.ReadOutput("done"));Assert.Equal(71,(int)sim.ReadOutput("result"));
    }
}
