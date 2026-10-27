#nullable enable
using System;
using SharpHDL.HLS;

namespace SharpHDL;

public static partial class CilCompiler
{
    // SharpHDL's CLR-compatible Int32 policy uses only the five low count bits. Build a fixed
    // five-stage barrel shifter: negative and oversized counts are masked rather
    // than interpreted as hardware widths or host-time shift amounts.
    private static ScheduledValue ScheduledClrShift(ScheduledMachineBuilder builder,ScheduledValue value,ScheduledValue count,string opcode)
    {
        if(value.Width!=32||count.Width!=32)
            throw new ArgumentException("Scheduled CLR shifts require 32-bit stack values.");
        if(opcode is not ("shl" or "shr" or "shr.un"))
            throw new ArgumentException("Unsupported scheduled shift opcode.",nameof(opcode));
        var zero=builder.Constant(32,0);
        for(int bit=0;bit<5;bit++)
        {
            int distance=1<<bit;
            var selected=count.And(builder.Constant(32,distance)).Equal(zero).Not();
            var shifted=opcode=="shl"?value.ShiftLeft(distance):
                opcode=="shr"?value.ArithmeticShiftRight(distance):value.ShiftRight(distance);
            value=ScheduledValue.Select(selected,shifted,value);
        }
        return value;
    }
}
