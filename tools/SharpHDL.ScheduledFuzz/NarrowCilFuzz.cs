using System.Buffers.Binary;
using SharpHDL;
using SharpHDL.HLS;
using SharpHDL.Simulation;
using SharpHDL.Verification;

/// <summary>Coverage-guided full-width values exercise actual raw-IL conversion lowering.</summary>
internal static class NarrowCilFuzz
{
    private static readonly string[] Opcodes=["conv.i1","conv.u1","conv.i2","conv.u2"];
    private static readonly Lazy<ScheduledHardwareDesign[]> Designs=new(()=>Opcodes.Select(op=>
        CilCompiler.BuildScheduledSequential(NarrowCilFixture.Method(op),"FuzzNarrow"+op.Replace('.','_')).Design).ToArray());
    internal static void Check(ReadOnlySpan<byte> bytes)
    {
        if(bytes.Length<5)return;
        uint value=BinaryPrimitives.ReadUInt32LittleEndian(bytes);int index=bytes[4]%4;
        var d=Designs.Value[index];var sim=new HardwareGraphMachine(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);
        sim.SetInput("arg0",value);sim.SetInput("start",1);sim.Tick();sim.SetInput("start",0);sim.SetInput("arg0",~value);
        for(int edge=0;edge<64&&sim.ReadOutput("done")==0;edge++)sim.Tick();
        if(sim.ReadOutput("done")!=1||sim.ReadOutput("fault")!=0||
            (uint)sim.ReadOutput("result")!=NarrowCilFixture.Expected(value,Opcodes[index]))
            throw new InvalidOperationException("Raw CIL narrow conversion mismatch.");
    }
}
