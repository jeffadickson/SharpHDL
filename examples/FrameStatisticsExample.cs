using SharpHDL;
public static class FrameStatisticsExample
{
    public static Module Build(FrameReductionOptions options=null) {
        options=options??new FrameReductionOptions();
        var m=new Module("FrameStatistics");
        var input=Stream.Input(m,"s_axis",options.SampleWidth,8);
        var result=FrameReduction.Build(m,input,"statistics",options);
        Stream.Connect(m,result,Stream.Output(m,"m_axis",options.ResultWidth,8));
        return m;
    }
}
