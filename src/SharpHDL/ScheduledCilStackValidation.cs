#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SharpHDL.HLS;

namespace SharpHDL;

public static partial class CilCompiler
{
    // CLI intermediate categories, not storage widths. Array lengths are native
    // unsigned values; the bounded RAM length may normalize through conv.i4/u4,
    // but must never accidentally acquire 32-bit shift/arithmetic semantics.
    [Flags]
    private enum ScheduledStackKind { Integer32=1, NullReference=4, ArrayLength=8, RamReference=16, RamMask=4080 }
    private static bool IsScheduledReference(ScheduledStackKind kind)
        =>kind!=0&&(kind&~(ScheduledStackKind.RamMask|ScheduledStackKind.NullReference))==0;

}
