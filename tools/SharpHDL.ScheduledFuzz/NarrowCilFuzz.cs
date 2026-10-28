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
    private static readonly Type[] StorageTypes=[typeof(sbyte),typeof(byte),typeof(short),typeof(ushort),typeof(char)];
    private static readonly Lazy<ScheduledHardwareDesign[]> StorageDesigns=new(()=>StorageTypes.Select(type=>
        CilCompiler.BuildScheduledSequential(NarrowCilFixture.Storage(type),"FuzzStorage"+type.Name).Design).ToArray());
    private static readonly Lazy<(ScheduledHardwareDesign Design,int[] Offsets)> CheckedDesign=new(()=>
    {
        var method=NarrowCilFixture.CheckedDispatch(out var offsets);
        return (CilCompiler.BuildScheduledSequential(method,"FuzzChecked").Design,offsets);
    });
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
        int storageIndex=bytes[4]%StorageTypes.Length;d=StorageDesigns.Value[storageIndex];
        sim=new HardwareGraphMachine(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);
        sim.SetInput("arg0",value);sim.SetInput("start",1);sim.Tick();sim.SetInput("start",0);
        for(int edge=0;edge<64&&sim.ReadOutput("done")==0;edge++)sim.Tick();
        string storageOpcode=storageIndex==4?"conv.u2":Opcodes[storageIndex];
        if(sim.ReadOutput("done")!=1||sim.ReadOutput("fault")!=0||
            (uint)sim.ReadOutput("result")!=NarrowCilFixture.Expected(value,storageOpcode))
            throw new InvalidOperationException("Raw CIL local storage mismatch.");
        int checkedIndex=bytes[4]%12;var checkedCase=CheckedDesign.Value;d=checkedCase.Design;
        string checkedOpcode=NarrowCilFixture.CheckedOpcodes[checkedIndex].Name!;
        string target=checkedOpcode.Split('.')[2];int width=int.Parse(target[1..])*8;
        long source=checkedOpcode.EndsWith(".un",StringComparison.Ordinal)?value:unchecked((int)value);
        long minimum=target[0]=='i'?-(1L<<(width-1)):0,maximum=target[0]=='i'?(1L<<(width-1))-1:(1L<<width)-1;
        bool fits=minimum<=source&&source<=maximum;
        sim=new HardwareGraphMachine(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);
        sim.SetInput("arg0",value);sim.SetInput("arg1",checkedIndex);sim.SetInput("start",1);sim.Tick();sim.SetInput("start",0);
        for(int edge=0;edge<64&&sim.ReadOutput("done")==0;edge++)sim.Tick();
        if(sim.ReadOutput("done")!=1||sim.ReadOutput("conversion_overflow")!=(fits?0:1)||sim.ReadOutput("fault")!=(fits?0:1)||
            sim.ReadOutput("fault_il")!=(fits?0:checkedCase.Offsets[checkedIndex])||sim.ReadOutput("result")!=(fits?value:0))
            throw new InvalidOperationException("Raw CIL checked conversion mismatch.");
    }
}
