using System;
using SharpHDL;
using System.Numerics;

public static class MixedScience
{
    public static AxisBeat Calibrate(AxisBeat b) {
        int x=(int)Expr.SignedValue(b.Data,16);
        int y=Math.Max(-32768,Math.Min(32767,2*x-10));
        return new AxisBeat(y & 65535,b.Keep,b.Last,b.User);
    }
    public static AxisBeat Energy(AxisBeat b) {
        return ScientificStreams.SampleEnergy(b,new AxisFormat(16,8));
    }
    public static AxisPipeline Build() {
        var format=new AxisFormat(16,8);
        var skid=new Module("SensorSkid");
        var input=Stream.Input(skid,"s_axis",16,8);
        Stream.Connect(skid,StreamBlocks.SkidBuffer(skid,input,"capture"),Stream.Output(skid,"m_axis",16,8));
        var square=new Module("SensorEnergy");
        var si=Stream.Input(square,"s_axis",16,8);
        Stream.Connect(square,ScientificStreams.SampleEnergy(square,si,"energy"),Stream.Output(square,"m_axis",32,8));
        return new AxisPipeline("mixed_science")
            .Add(AxisComponent.FromSharp(skid,"s_axis","m_axis",b=>b))
            .Add(AxisComponent.External("sensor_calibrate","examples/MixedAxis/calibrate.v","s_axis","m_axis",format,format,Calibrate)
                .Parameter("GAIN",2).Parameter("OFFSET",-10))
            .Add(AxisComponent.FromSharp(square,"s_axis","m_axis",Energy));
    }
}
