namespace SharpHDL.Examples;

/// <summary>Aliases of one bound RAM observe sequential writes through the same physical memory.</summary>
public static class ScheduledArrayAliases
{
    /// <summary>Update through a copied alias and accumulate through the original reference.
    /// Earlier writes survive a later out-of-range access; hardware reports that access as a fault.</summary>
    public static uint Update(uint[] words,uint count,uint increment)
    {
        uint[] left=words;uint[] right=left;uint sum=0;
        for(uint i=0;i<count;i++)
        {
            right[i]=unchecked(left[i]+increment);
            sum=unchecked(sum+words[i]);
        }
        return sum;
    }
}
