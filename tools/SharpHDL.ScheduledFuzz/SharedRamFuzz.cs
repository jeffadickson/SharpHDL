using System.Buffers.Binary;
using SharpHDL;
using SharpHDL.Examples;
using SharpHDL.Simulation;

/// <summary>Explicit resource identity and bounds compared with real CLR object aliasing.</summary>
internal static class SharedRamFuzz
{
    internal static void Check(ReadOnlySpan<byte> bytes)
    {
        if(bytes.Length<6)return;
        uint word=BinaryPrimitives.ReadUInt32LittleEndian(bytes),index=(uint)(bytes[5]%4);bool shared=(bytes[4]&1)!=0;
        uint[] left=new uint[3],right=shared?left:new uint[3];uint expected=0;bool failed=false;
        try{expected=ScheduledSharedRam.Update(left,right,index,word);}catch(IndexOutOfRangeException){failed=true;}
        var d=CilCompiler.BuildScheduledSequential(typeof(ScheduledSharedRam).GetMethod(nameof(ScheduledSharedRam.Update))!,"FuzzShared",
            new SequentialOptions{MemoryDepth=3,RamAliases=shared?new Dictionary<int,int>{{1,0}}:null!}).Design;
        var sim=new HardwareGraphMachine(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);
        sim.SetInput("arg2",index);sim.SetInput("arg3",word);sim.SetInput("start",1);sim.Tick();sim.SetInput("start",0);
        for(int i=0;i<100&&sim.ReadOutput("done")==0;i++)sim.Tick();
        if(sim.ReadOutput("done")!=1||sim.ReadOutput("fault")!=(failed?1:0)||sim.ReadOutput("result")!=expected)
            throw new InvalidOperationException("Shared RAM CLR/Core oracle mismatch.");
    }
}
