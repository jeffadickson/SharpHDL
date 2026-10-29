#nullable enable
namespace SharpHDL.Examples;

/// <summary>Compile with RamAliases[1]=0 to model the same physical array through both parameters.</summary>
public static class ScheduledSharedRam
{
    /// <summary>A cross-parameter write, equality test and persistent read expose the physical binding contract.</summary>
    public static uint Update(uint[] left,uint[] right,uint index,uint word)
    {
        left[index]=word;
        return unchecked(right[index]+(left==right?100u:0u));
    }
}
