#nullable enable
using System;
using SharpHDL.HLS;

namespace SharpHDL;

public static partial class CilCompiler
{
    // CLI small-integer conversions truncate first, then extend back to the
    // Int32 evaluation stack. Signedness belongs to the opcode, not the source
    // port's C# type. Checked/wide/native/floating conversions are not admitted.
    private static ScheduledValue ScheduledClrNarrow(ScheduledValue value,string opcode)
    {
        if(value.Width!=32)throw new ArgumentException("Narrow CIL conversion requires a 32-bit stack value.",nameof(value));
        var (width,signed)=opcode switch
        {
            "conv.i1"=>(8,true),"conv.u1"=>(8,false),
            "conv.i2"=>(16,true),"conv.u2"=>(16,false),
            _=>throw new ArgumentException("Unsupported narrow CIL conversion.",nameof(opcode))
        };
        var narrowed=value.Resize(width);
        return signed?narrowed.SignExtend(32):narrowed.Resize(32);
    }
}
