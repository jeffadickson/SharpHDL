using SharpHDL;

public static class PacketAndMathExamples
{
    public static RouterDesign PacketRouter() { return RouterDesign.Build(queueDepth:4); }

    public static Module Cosine()
    {
        var m=new Module("CosineExample");
        var angle=Stream.Input(m,"s_axis",24,userWidth:8);
        var result=Stream.Output(m,"m_axis",32,userWidth:8);
        // Constructs a 21-stage CORDIC circuit, not a host Math.Cos call.
        Stream.Connect(m,HwMath.Cos(m,angle,"cosine"),result);
        return m;
    }
    public static Module Reciprocal()
    {
        var m=new Module("ReciprocalExample");
        var fraction=Stream.Input(m,"s_axis",16,userWidth:8);
        var result=Stream.Output(m,"m_axis",24,userWidth:8);
        // x is 1 + fraction/65536; output is unsigned Q4.20.
        Stream.Connect(m,HwMath.RomMath(m,fraction,"reciprocal",RomFunction.ReciprocalMantissa),result);
        return m;
    }
    public static Module PolynomialLog()
    {
        var m=new Module("PolynomialLogExample");
        var fraction=Stream.Input(m,"s_axis",16,userWidth:8);
        var result=Stream.Output(m,"m_axis",24,userWidth:8);
        Stream.Connect(m,HwMath.PolynomialMath(m,fraction,"log",RomFunction.Log2Mantissa),result);
        return m;
    }
}
