using SharpHDL;

/// <summary>Two independent CIL actors, connected through a two-token FIFO.</summary>
public static class StreamingProcesses
{
    [HardwareProcess]
    public static void AddTwo(HwStream<ushort> input,HwStream<ushort> output)
    {
        while(true)
        {
            ushort value=input.Read();
            output.Write(unchecked((ushort)(value+2)));
        }
    }
    [HardwareProcess]
    public static void TimesThree(HwStream<ushort> input,HwStream<ushort> output)
    {
        while(true) output.Write(unchecked((ushort)(input.Read()*3)));
    }
    public static Module Build()
    {
        var m=new Module("StreamingProcesses");
        var input=HwStream<ushort>.Input(m,"input");
        var middle=HwStream<ushort>.Internal(m,"added");
        var queued=HwStream<ushort>.Fifo(m,middle,"queue",2);
        var result=HwStream<ushort>.Internal(m,"result");
        CilCompiler.AttachProcess(m,"add_two",typeof(StreamingProcesses).GetMethod("AddTwo"),input,middle);
        CilCompiler.AttachProcess(m,"times_three",typeof(StreamingProcesses).GetMethod("TimesThree"),queued,result);
        HwStream<ushort>.ToAxi(m,result,Stream.Output(m,"output",16));
        return m;
    }
}
