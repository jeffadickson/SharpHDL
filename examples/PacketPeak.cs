using SharpHDL;

// A frame reducer: N signed samples in, one {sample_count, peak_magnitude} out.
public sealed class PacketPeakExample
{
    public Module Module; public Stream Input, Output;
    public static PacketPeakExample Build()
    {
        var m=new Module("PacketPeak");
        var input=Stream.Input(m,"s_axis",16,8);
        var output=Stream.Output(m,"m_axis",32,8);
        var reduced=Stream.Internal(m,"reduced",32,8);
        var peak=m.Register("peak",16,0);
        var count=m.Register("count",16,0);
        var magnitude=Expr.Mux(input.Data.Slice(15,15),Expr.Const(16,0)-input.Data,input.Data);
        var nextPeak=Expr.Mux(peak.Lt(magnitude),magnitude,peak);
        var nextCount=Expr.Mux(count.Eq(Expr.Const(16,65535)),count,count+Expr.Const(16,1));
        m.Drive(input.Ready,(~input.Last)|reduced.Ready);
        m.NextWhen(peak,input.Fire,Expr.Mux(input.Last,Expr.Const(16,0),nextPeak));
        m.NextWhen(count,input.Fire,Expr.Mux(input.Last,Expr.Const(16,0),nextCount));
        m.Drive(reduced.Valid,input.Valid & input.Last);
        m.Drive(reduced.Data,Expr.Concat(nextCount,nextPeak));
        m.Drive(reduced.Keep,Expr.Const(4,15));m.Drive(reduced.Last,Expr.Const(1,1));m.Drive(reduced.User,input.User);
        Stream.Connect(m,StreamBlocks.Map(m,reduced,"result",32,x=>x),output);
        return new PacketPeakExample{Module=m,Input=input,Output=output};
    }
}
