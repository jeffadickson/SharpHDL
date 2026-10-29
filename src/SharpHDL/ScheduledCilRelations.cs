#nullable enable
using System.Numerics;

namespace SharpHDL;

public static partial class CilCompiler
{
    // Each row is the set of locals proven equal to that local on every path.
    // BigInteger is a bounded bit set here (at most the admitted local count).
    private static void AssignScheduledRelation(BigInteger[] relations,int target,int source)
    {
        BigInteger members=source<0?BigInteger.Zero:relations[source];
        BigInteger bit=BigInteger.One<<target;
        for(int i=0;i<relations.Length;i++)relations[i]&=~bit;
        relations[target]=members|bit;
        for(int i=0;i<relations.Length;i++)
            if((members&(BigInteger.One<<i))!=0)relations[i]|=bit;
    }
}
