using SharpHDL.HLS;

namespace SharpHDL.Examples;

/// <summary>Unchecked byte/short casts compiled through real CIL into scheduled hardware.</summary>
public static class ScheduledNarrowCil
{
    /// <summary>Mix all four small-integer conversions without narrow locals or ports.</summary>
    public static uint Evaluate(uint value)=>unchecked((uint)(sbyte)value+(uint)(short)(value>>8)+
        (uint)(byte)(value>>16)+(uint)(ushort)(value>>1));
    /// <summary>Build the supported full-width-port scheduled CIL design.</summary>
    public static ScheduledHardwareDesign Build()=>CilCompiler.BuildScheduledSequential(
        typeof(ScheduledNarrowCil).GetMethod(nameof(Evaluate))!,"ScheduledCilNarrow").Design;
}
