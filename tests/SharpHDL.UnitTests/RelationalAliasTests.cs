using System.Reflection.Emit;
using SharpHDL.Verification;
using SharpHDL.Simulation;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>Guard facts follow proven copies, never copies that differ on another path.</summary>
public sealed class RelationalAliasTests
{
    [Theory][InlineData(-1)][InlineData(0)][InlineData(1)]
    public void Copied_nullable_guard_refines_only_current_equal_locals(int overwrite)
    {
        var m=TypedCilFixture.Create(il=>
        {
            var join=il.DefineLabel();var missing=il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Brfalse,join);il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Stloc_0);il.MarkLabel(join);
            il.Emit(OpCodes.Ldloc_0);il.Emit(OpCodes.Stloc_1);
            if(overwrite>=0){il.Emit(OpCodes.Ldnull);il.Emit(overwrite==0?OpCodes.Stloc_0:OpCodes.Stloc_1);}
            il.Emit(overwrite==1?OpCodes.Ldloc_0:OpCodes.Ldloc_1);il.Emit(OpCodes.Brfalse,missing);
            il.Emit(overwrite==1?OpCodes.Ldloc_1:OpCodes.Ldloc_0);il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Ldelem_U4);il.Emit(OpCodes.Ret);
            il.MarkLabel(missing);il.Emit(OpCodes.Ldc_I4,17);il.Emit(OpCodes.Ret);
        },typeof(uint[]),typeof(uint[]));
        if(overwrite>=0){Assert.Contains("definitely",Assert.Throws<NotSupportedException>(()=>CilCompiler.BuildScheduledSequential(m,"Detached")).Message);return;}
        var d=CilCompiler.BuildScheduledSequential(m,"CopiedGuard",new SequentialOptions{MemoryDepth=2,InitialMemory=[91,2]}).Design;
        var sim=new HardwareGraphMachine(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);
        foreach(uint choice in new uint[]{0,1,0,1})
        {
            uint expected=(uint)m.Invoke(null,[new uint[]{91,2},choice])!;
            sim.SetInput("arg1",choice);sim.SetInput("start",1);sim.Tick();sim.SetInput("start",0);
            for(int i=0;i<96&&sim.ReadOutput("done")==0;i++)sim.Tick();
            Assert.Equal(expected,(uint)sim.ReadOutput("result"));Assert.Equal(1,(int)sim.ReadOutput("done"));Assert.Equal(0,(int)sim.ReadOutput("fault"));
        }
    }

    [Fact]
    public void Equality_on_only_one_path_is_not_a_join_invariant()
    {
        var m=TypedCilFixture.Create(il=>
        {
            var join=il.DefineLabel();var missing=il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Stloc_0);
            il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Brfalse,join);il.Emit(OpCodes.Ldloc_0);il.Emit(OpCodes.Stloc_1);il.MarkLabel(join);
            il.Emit(OpCodes.Ldloc_0);il.Emit(OpCodes.Brfalse,missing);
            il.Emit(OpCodes.Ldloc_1);il.Emit(OpCodes.Ldlen);il.Emit(OpCodes.Conv_U4);il.Emit(OpCodes.Ret);
            il.MarkLabel(missing);il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Ret);
        },typeof(uint[]),typeof(uint[]));
        Assert.Contains("definitely",Assert.Throws<NotSupportedException>(()=>CilCompiler.BuildScheduledSequential(m,"OnePath")).Message);
    }
}
