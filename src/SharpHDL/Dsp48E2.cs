using System;

namespace SharpHDL
{
    /// <summary>
    /// Explicit access to one AMD UltraScale/UltraScale+ DSP48E2 multiplier.
    /// The generated RTL instantiates the real DSP48E2 through the external
    /// sharphdl_dsp48e2_mul_unsigned wrapper. This is NOT portable inference.
    /// </summary>
    public static class Dsp48E2
    {
        /// <summary>
        /// Build a fully combinational unsigned multiply with full precision.
        /// The DSP48E2 signed 27x18 datapath requires unsigned operand widths
        /// no greater than 26x17 so that both sign bits remain zero.
        /// Include rtl/amd/sharphdl_dsp48e2_mul_unsigned.v when compiling RTL.
        /// </summary>
        public static Expr MultiplyUnsigned(Expr a,Expr b)
        {
            if(a==null)throw new ArgumentNullException(nameof(a));
            if(b==null)throw new ArgumentNullException(nameof(b));
            if(a.Width>26||b.Width>17)
                throw new NotSupportedException("A single unsigned DSP48E2 multiply supports up to 26x17 bits; wider products require decomposition into multiple DSPs.");
            return new Expr(a.Width+b.Width,"dsp48e2",a,b)
                .Describe("Direct AMD DSP48E2 primitive; unsigned, combinational, no internal registers.");
        }
    }
}
