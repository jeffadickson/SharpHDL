using System.Buffers.Binary;
using System.Numerics;
using SharpHDL;
using SharpHDL.Examples;
using SharpHDL.Simulation;

/// <summary>Coverage-guided guarded-origin dispatch checked against real CLR array side effects.</summary>
internal static class GuardedRamFuzz
{
    internal static void Check(ReadOnlySpan<byte> bytes)
    {
        if(bytes.Length<6)return;
        uint select=(uint)(bytes[4]%4),index=(uint)(bytes[5]%4),word=BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        uint[] left=[7,9],right=[21,22,23];uint expected=0;bool failed=false;
        try {expected=ScheduledMultiRam.Update(left,right,select,index,word);}catch(IndexOutOfRangeException){failed=true;}
        var d=CilCompiler.BuildScheduledSequential(typeof(ScheduledMultiRam).GetMethod(nameof(ScheduledMultiRam.Update))!,"FuzzGuardedRams",
            new SequentialOptions{MaxCycles=400,MemoryDepths=new Dictionary<int,int>{{0,2},{1,3}},InitialMemories=new Dictionary<int,BigInteger[]>{{0,[7,9]},{1,[21,22,23]}}}).Design;
        var sim=new HardwareGraphMachine(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);
        sim.SetInput("arg2",select);sim.SetInput("arg3",index);sim.SetInput("arg4",word);sim.SetInput("start",1);sim.Tick();sim.SetInput("start",0);
        for(int i=0;i<402&&sim.ReadOutput("done")==0;i++)sim.Tick();
        if(sim.ReadOutput("done")!=1||sim.ReadOutput("fault")!=(failed?1:0)||sim.ReadOutput("result")!=expected)
            throw new InvalidOperationException("Guarded multiple-RAM fuzz oracle mismatch.");
    }
}
