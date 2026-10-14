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
    // Stateless signed gain selection followed by saturation; all paths emit one token.
    [HardwareProcess]
    public static void ClipGain(HwStream<short> input,HwStream<short> output)
    {
        while(true)
        {
            int x=input.Read();
            int y=x<0?x*3:x*5;
            if(y>12000)y=12000;
            else if(y< -12000)y= -12000;
            output.Write((short)y);
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
