using System.Reflection.Emit;
using SharpHDL.Simulation;
using SharpHDL.Verification;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>Close the seven explicitly retained positive-variant and diagnostic contracts.</summary>
public sealed class ScheduledAliasGapTests
{
    /// <summary>Each variant gets a fresh compiled design so mutation activation cannot be hidden by a cache.</summary>
    [Theory][InlineData("pop")][InlineData("signedload")][InlineData("signedlength")]
    public void Positive_variants_match_CLR(string variant)
    {
        var method=TypedCilFixture.Create(il=>
        {
            il.Emit(OpCodes.Ldarg_0);
            if(variant=="pop"){il.Emit(OpCodes.Pop);il.Emit(OpCodes.Ldc_I4_7);}
            else if(variant=="signedload"){il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Ldelem_I4);}
            else {il.Emit(OpCodes.Ldlen);il.Emit(OpCodes.Conv_I4);}
            il.Emit(OpCodes.Ret);
        });
        uint expected=(uint)method.Invoke(null,[new uint[]{uint.MaxValue,3},0u])!;
        Assert.Equal(variant=="pop"?7u:variant=="signedload"?uint.MaxValue:2u,expected);
        var d=CilCompiler.BuildScheduledSequential(method,"GapVariant",new SequentialOptions{MemoryDepth=2,InitialMemory=[uint.MaxValue,3]}).Design;
        var sim=new HardwareGraphMachine(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);
        sim.SetInput("start",1);sim.Tick();sim.SetInput("start",0);
        for(int i=0;i<64&&sim.ReadOutput("done")==0;i++)sim.Tick();
        Assert.Equal(1,(int)sim.ReadOutput("done"));Assert.Equal(0,(int)sim.ReadOutput("fault"));
        Assert.Equal(expected,(uint)sim.ReadOutput("result"));
    }

    /// <summary>Method/offset alone is insufficient: a rejected program must retain its actionable reason.</summary>
    [Theory][InlineData("height","Inconsistent stack height")][InlineData("stack","Invalid/excessive evaluation stack")]
    [InlineData("native","normalize native array length")][InlineData("comparison","Comparison requires matching")]
    public void Diagnostics_preserve_actionable_reasons(string mode,string reason)
    {
        var method=TypedCilFixture.Create(il=>
        {
            if(mode=="height")
            {
                var join=il.DefineLabel();il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Brtrue,join);
                il.Emit(OpCodes.Ldc_I4_0);il.MarkLabel(join);il.Emit(OpCodes.Ldc_I4_0);
            }
            else if(mode=="stack")il.Emit(OpCodes.Pop);
            else if(mode=="native")
            {var end=il.DefineLabel();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldlen);il.Emit(OpCodes.Brtrue,end);il.MarkLabel(end);il.Emit(OpCodes.Ldc_I4_0);}
            else {il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Ceq);}
            il.Emit(OpCodes.Ret);
        });
        var error=Assert.Throws<NotSupportedException>(()=>CilCompiler.BuildScheduledSequential(method,"GapDiagnostic"));
        Assert.Contains("Evaluate",error.Message);Assert.Contains("IL_",error.Message);Assert.Contains(reason,error.Message);
    }
}
