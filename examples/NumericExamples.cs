using System;
using SharpHDL;

public sealed class NumericExample
{
    public Module Module;
    public Stream Input,Output;
}
public static class NumericExamples
{
    // Signed Q8.16 gain stage: 24-bit input * 24-bit constant -> 48-bit Q16.32 product.
    // Explicitly round back to Q8.16, saturating instead of wrapping at +/-128.
    public static NumericExample QuantizedGain()
    {
        var m=new Module("QuantizedGain24");var input=Stream.Input(m,"input",24,8);var output=Stream.Output(m,"output",24,8);
        input.Data.Describe("Signed fixed point: [23] sign, [22:16] integer, [15:0] fractional; value = signed(bits)/65536.");
        var cut=StreamBlocks.SkidBuffer(m,input,"input_cut");
        var gain=StreamBlocks.Map(m,cut,"gain",24,bits=>{
            var sample=new SFixed<W24,F16>(bits);
            var product=sample*SFixed<W24,F16>.Constant(1.5);
            return product.Convert<W24,F16>(Rounding.NearestEven,Overflow.Saturate).Bits;
        });
        Stream.Connect(m,StreamBlocks.SkidBuffer(m,gain,"output_cut"),output);
        output.Data.Describe("Signed Q8.16 gain result: round nearest even, saturate to [-128, 128-2^-16].");
        return new NumericExample{Module=m,Input=input,Output=output};
    }
    // One atomic complex sample per beat: real low24, imaginary high24.
    public static NumericExample ComplexPower()
    {
        var f=FloatFormat.Compact24;var m=new Module("ComplexPower24");
        var input=Stream.Input(m,"input",48,8);var output=Stream.Output(m,"output",24,8);
        var power=FloatMath.Binary(m,input,"power",f,(real,imaginary)=>real*real+imaginary*imaginary);
        Stream.Connect(m,StreamBlocks.SkidBuffer(m,power,"output_cut"),output);output.Data.Describe(f.Layout);
        return new NumericExample{Module=m,Input=input,Output=output};
    }
}
