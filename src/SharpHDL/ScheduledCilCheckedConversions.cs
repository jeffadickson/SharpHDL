#nullable enable
using System;
using SharpHDL.HLS;

namespace SharpHDL;

public static partial class CilCompiler
{
    // The evaluation stack is Int32 here. The .un suffix changes interpretation
    // of that same bit pattern, not its width or the managed source declaration.
    private static bool IsScheduledChecked(string opcode)=>opcode is
        "conv.ovf.i1" or "conv.ovf.u1" or "conv.ovf.i2" or "conv.ovf.u2" or "conv.ovf.i4" or "conv.ovf.u4" or
        "conv.ovf.i1.un" or "conv.ovf.u1.un" or "conv.ovf.i2.un" or "conv.ovf.u2.un" or "conv.ovf.i4.un" or "conv.ovf.u4.un";

    internal static (ScheduledValue Value,ScheduledValue Fits) ScheduledClrChecked(
        ScheduledMachineBuilder builder,ScheduledValue value,string opcode)
    {
        if(value.Width!=32)throw new ArgumentException("Checked CIL conversion requires a 32-bit stack value.",nameof(value));
        if(!IsScheduledChecked(opcode))throw new ArgumentException("Unsupported checked CIL conversion.",nameof(opcode));
        bool unsignedSource=opcode.EndsWith(".un",StringComparison.Ordinal);
        string target=opcode.Split('.')[2];bool signedTarget=target[0]=='i';
        int width=target[1] switch {'1'=>8,'2'=>16,_=>32};
        var converted=width==32?value:signedTarget?value.Resize(width).SignExtend(32):value.Resize(width).Resize(32);
        var zero=builder.Constant(32,0);var yes=builder.Constant(1,1);
        ScheduledValue fits;
        if(unsignedSource)
        {
            // UInt32 to UInt32 is total; other unsigned-source ranges start at 0.
            fits=!signedTarget&&width==32?yes:value.LessThan(builder.Constant(32,1UL<<(signedTarget?width-1:width)));
        }
        else if(signedTarget)
        {
            // A signed value fits iff narrowing and extending preserves every bit.
            fits=width==32?yes:converted.Equal(value);
        }
        else
        {
            var nonnegative=value.SignedLessThan(zero).Not();
            fits=width==32?nonnegative:nonnegative.And(value.LessThan(builder.Constant(32,1UL<<width)));
        }
        return (converted,fits);
    }
}
