using SharpFuzz;
using SharpHDL.Examples;
using SharpHDL.Simulation;
using System.Buffers.Binary;
using System.Text;
using SharpHDL;

// SharpFuzz/AFL++ owns generation, coverage feedback and crash retention.
// This bounded harness translates bytes into protocol traffic and an independent array model.
Fuzzer.OutOfProcess.Run(stream =>
{
    var bytes=new byte[256];int length=stream.ReadAtLeast(bytes,1,throwOnEndOfStream:false);
    if(length>=8)
    {
        // BCL endian decoding avoids a custom input parser; the count remains a signed CLR bit pattern.
        uint operand=BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0,4));
        int count=BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4,4));
        var shift=CilCompiler.BuildScheduledSequential(typeof(ShiftFuzzKernel).GetMethod(nameof(ShiftFuzzKernel.Evaluate))!,"FuzzShifts").Design;
        var machine=new HardwareGraphMachine(shift.Graph,shift.Inputs,shift.Outputs,shift.NextState,shift.ResetValues,shift.Memories);
        machine.SetInput("arg0",operand);machine.SetInput("arg1",unchecked((uint)count));
        machine.SetInput("start",1);machine.Tick();machine.SetInput("start",0);
        for(int edge=0;edge<128&&machine.ReadOutput("done")==0;edge++)machine.Tick();
        if(machine.ReadOutput("done")!=1||machine.ReadOutput("fault")!=0||
            (uint)machine.ReadOutput("result")!=ShiftFuzzKernel.Evaluate(operand,count))
            throw new InvalidOperationException("Native/scheduled shift mismatch.");
    }
    var offset=PortablePdbSourceMap.ParseIlOffset(Encoding.ASCII.GetString(bytes,0,length));
    if(offset<0)throw new InvalidOperationException("A parsed IL offset must never be negative.");
    var d=ScheduledTable.Build(2);
    var sim=new HardwareGraphMachine(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);
    int[] table=new int[4];int phase=0,key=0,delta=0,result=0;bool update=false;
    sim.Reset();
    for(int i=0;i<length;i++)
    {
        int value=bytes[i];bool reset=(value&128)!=0;
        bool valid=(value&1)!=0,ready=(value&2)!=0;
        sim.SetInput("request_valid",valid?1:0);sim.SetInput("request_address",value&3);
        sim.SetInput("request_delta",value*257);sim.SetInput("request_update",(value>>2)&1);
        sim.SetInput("response_ready",ready?1:0);
        if((int)sim.ReadOutput("request_ready")!=(phase==0?1:0) ||
            (int)sim.ReadOutput("response_valid")!=(phase==3?1:0) ||
            phase==3 && (int)sim.ReadOutput("response_data")!=result)
            throw new InvalidOperationException($"Table mismatch at byte {i}.");
        if(reset){phase=0;sim.Reset();continue;}
        if(phase==0 && valid){key=value&3;delta=value*257;update=(value&4)!=0;phase=1;}
        else if(phase==1)phase=2;
        else if(phase==2){result=update?(table[key]+delta)&65535:table[key];if(update)table[key]=result;phase=3;}
        else if(phase==3 && ready)phase=0;
        sim.Tick();
    }
});

/// <summary>Real C# oracle and CIL input, kept outside the instrumented production assemblies.</summary>
internal static class ShiftFuzzKernel
{
    /// <summary>Combine all three CLR shift operations with modular addition.</summary>
    public static uint Evaluate(uint value,int count)
        =>unchecked((value<<count)+(value>>count)+(uint)((int)value>>count));
}
