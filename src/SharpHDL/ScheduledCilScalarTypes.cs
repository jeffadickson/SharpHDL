#nullable enable
using System;
using SharpHDL.HLS;

namespace SharpHDL;

public static partial class CilCompiler
{
    // Storage and stack representations are distinct CLI concepts. Small
    // storage truncates on writes and extends on reads. Preserve the existing
    // canonical Boolean contract (one-bit ports, Int32 local/argument slots).
    private readonly record struct ScheduledScalarType(int StorageWidth,int PortWidth,bool Signed)
    {
        internal ScheduledValue Load(ScheduledValue storage)=>Signed?storage.SignExtend(32):storage.Resize(32);
        internal ScheduledValue Store(ScheduledValue stack)=>stack.Resize(StorageWidth);
    }

    private static bool TryScheduledScalar(Type type,out ScheduledScalarType scalar)
    {
        scalar=type==typeof(sbyte)?new(8,8,true):type==typeof(byte)?new(8,8,false):
            type==typeof(short)?new(16,16,true):type==typeof(ushort)||type==typeof(char)?new(16,16,false):
            type==typeof(int)?new(32,32,true):type==typeof(uint)?new(32,32,false):new(32,1,false);
        return type==typeof(sbyte)||type==typeof(byte)||type==typeof(short)||type==typeof(ushort)||
            type==typeof(char)||type==typeof(int)||type==typeof(uint)||type==typeof(bool);
    }
}
