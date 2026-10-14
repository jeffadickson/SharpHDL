using System;
namespace SharpHDL
{
    /// <summary>Bit-exact streaming scientific primitives with explicit sample formats.</summary>
    public static class ScientificStreams
    {
        /// <summary>Square a signed two's-complement sample without overflow.
        /// Output is unsigned, twice the input width. For Q-format input with F
        /// fractional bits, output has 2F fractional bits. No rounding occurs.
        /// Full-byte samples produce full TKEEP; partial samples produce zero TKEEP.
        /// Every input beat still produces one output beat, preserving LAST and USER.</summary>
        public static Stream SampleEnergy(Module module,Stream input,string name)
        {
            if(module==null || input==null)throw new ArgumentNullException();
            if(input.Width>64)throw new ArgumentException("SampleEnergy supports byte-aligned samples up to 64 bits.");
            int width=input.Width*2;
            var mapped=StreamBlocks.Map(module,input,name+"_holding",width+8,d=>{
                var x=d.SignExtend(width);
                var complete=input.Keep.Eq(Expr.Const(input.Keep.Width,Expr.Mask(input.Keep.Width)));
                return Expr.Concat(complete.Resize(8),x*x);
            });
            var output=Stream.Internal(module,name,width,input.UserWidth);
            module.Drive(output.Data,mapped.Data.Slice(width-1,0));
            output.Data.Describe("Unsigned exact square of signed "+input.Width+"-bit sample; twice the input fractional-bit count. No rounding or saturation.");
            module.Drive(output.Keep,Expr.Mux(mapped.Data.Slice(width,width),Expr.Const(width/8,Expr.Mask(width/8)),Expr.Const(width/8,0)));
            output.Keep.Describe("All bytes valid for a complete input sample; zero for a partial sample. Beat still transports LAST and USER.");
            module.Drive(output.Last,mapped.Last);module.Drive(output.User,mapped.User);
            module.Drive(output.Valid,mapped.Valid);module.Drive(mapped.Ready,output.Ready);
            return output;
        }
        public static AxisBeat SampleEnergy(AxisBeat input,AxisFormat format)
        {
            if(format==null)throw new ArgumentNullException(nameof(format));
            if(format.DataWidth>64)throw new ArgumentException("SampleEnergy supports samples up to 64 bits.");
            format.Check(input);
            var x=Expr.SignedValue(input.Data,format.DataWidth);
            return new AxisBeat(x*x,input.Keep==Expr.Mask(format.DataWidth/8)?Expr.Mask(format.DataWidth/4):0,input.Last,input.User);
        }
    }
}
