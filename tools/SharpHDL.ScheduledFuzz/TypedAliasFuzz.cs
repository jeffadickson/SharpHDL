using System.Buffers.Binary;
using System.Reflection.Emit;
using SharpHDL;
using SharpHDL.Examples;
using SharpHDL.Simulation;
using SharpHDL.Verification;

/// <summary>SharpFuzz supplies inputs; exact invalid-role invariants and CLR array effects supply the oracles.</summary>
internal static class TypedAliasFuzz
{
    internal static void Check(ReadOnlySpan<byte> bytes)
    {
        if(bytes.Length<5)return;
        uint increment=BinaryPrimitives.ReadUInt32LittleEndian(bytes),count=(uint)(bytes[4]%3);
        uint[] memory=[uint.MaxValue,17];uint expected=ScheduledArrayAliases.Update(memory,count,increment);
        var d=CilCompiler.BuildScheduledSequential(typeof(ScheduledArrayAliases).GetMethod(nameof(ScheduledArrayAliases.Update))!,
            "FuzzAlias",new SequentialOptions{MemoryDepth=2,InitialMemory=[uint.MaxValue,17],MaxCycles=200}).Design;
        var sim=new HardwareGraphMachine(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);
        sim.SetInput("arg1",count);sim.SetInput("arg2",increment);sim.SetInput("start",1);sim.Tick();sim.SetInput("start",0);
        for(int edge=0;edge<202&&sim.ReadOutput("done")==0;edge++)sim.Tick();
        if(sim.ReadOutput("done")!=1||sim.ReadOutput("fault")!=0||sim.ReadOutput("result")!=expected)
            throw new InvalidOperationException("CLR/alias RAM fuzz mismatch.");
        int mode=bytes[4]%3,index=bytes[0]+2;
        var invalid=TypedCilFixture.Create(il=>
        {
            if(mode==0){il.Emit(OpCodes.Ldarg,(short)index);il.Emit(OpCodes.Ret);}
            else if(mode==1){il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Ldelem_U4);il.Emit(OpCodes.Ret);}
            else {il.Emit(OpCodes.Ldloc_0);il.Emit(OpCodes.Ldlen);il.Emit(OpCodes.Conv_U4);il.Emit(OpCodes.Ret);}
        },typeof(uint[]));
        try { CilCompiler.BuildScheduledSequential(invalid,"FuzzInvalidAlias"); }
        catch(NotSupportedException error) when(error.Message.Contains("IL_",StringComparison.Ordinal)){return;}
        throw new InvalidOperationException("An invalid variable/reference role was accepted.");
    }
}
