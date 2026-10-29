#nullable enable
namespace SharpHDL.Examples;

/// <summary>Guarded dispatch across two distinct physical RAM bindings.</summary>
public static class ScheduledMultiRam
{
    /// <summary>Write through a guarded alias. Earlier left-array writes survive a later selected-array bounds fault.</summary>
    public static uint Update(uint[] left,uint[] right,uint select,uint index,uint word)
    {
        uint[]? chosen=null;
        if(select==1)chosen=left;
        if(select==2)chosen=right;
        if(chosen is null)return unchecked(left[0]+right[0]);
        left[0]=word;
        chosen[index]=unchecked(word+1);
        return unchecked(chosen[index]+(uint)chosen.Length);
    }
}
