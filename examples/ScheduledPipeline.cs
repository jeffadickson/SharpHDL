using SharpHDL;

public static class ScheduledPipeline
{
    // Two parallel paths reconverge at the second multiply; x must also be delayed for XOR.
    [HardwareProcess]
    public static void Polynomial(HwStream<ushort> input,HwStream<ushort> output)
    {
        while(true)
        {
            uint x=input.Read();
            uint a=x+2u;
            uint b=x*3u;
            output.Write(unchecked((ushort)((a*b)^x)));
        }
    }
    public static Module Build(out PipelineHandle schedule,int logicLevelsPerStage=1)
    {
        var m=new Module("ScheduledPolynomial"+logicLevelsPerStage);
        var input=HwStream<ushort>.Input(m,"input");
        var result=HwStream<ushort>.Internal(m,"result");
        schedule=CilCompiler.AttachPipeline(m,"polynomial",typeof(ScheduledPipeline).GetMethod("Polynomial"),input,result,
            new PipelineOptions{LogicLevelsPerStage=logicLevelsPerStage});
        HwStream<ushort>.ToAxi(m,result,Stream.Output(m,"output",16));
        return m;
    }
}
