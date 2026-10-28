using System.Numerics;
using SharpHDL.Core;
using SharpHDL.Verification;

/// <summary>SharpFuzz glue for profile rejection and bounded symbolic arithmetic; never treats Unknown as proof.</summary>
internal static class EquivalenceFuzz
{
    internal static void Check(ReadOnlySpan<byte> bytes)
    {
        int width=1+bytes[0]%64;
        bool badName=bytes.Length>1&&(bytes[1]&1)!=0,memory=bytes.Length>1&&(bytes[1]&2)!=0;
        var left=new HardwareGraph([new(0,HardwareOperation.Input,new(width),[],Name:badName?"x\0bad":"x"),
            new(1,HardwareOperation.Constant,new(width),[],BigInteger.One),
            memory?new(2,HardwareOperation.MemoryRead,new(width),[0],MemoryName:"ram"):new(2,HardwareOperation.Add,new(width),[0,1])]);
        var right=memory?left:new HardwareGraph([left.Nodes[0],left.Nodes[1],new(2,HardwareOperation.Add,new(width),[1,0])]);
        var result=BitVectorEquivalence.Check(left,2,right,2,timeoutMilliseconds:100);
        if(width>32||badName||memory)
        {
            if(result.Status!=EquivalenceStatus.Unsupported)throw new InvalidOperationException("Unsupported proof input was admitted.");
        }
        else if(result.Status is not (EquivalenceStatus.Equivalent or EquivalenceStatus.Unknown))
            throw new InvalidOperationException("Commutativity failed or native solver unavailable: "+result.Reason);
        // A budget-limited Unknown is retained as a nonproof outcome by the API;
        // this harness checks safety/classification, not proof completion for every fuzz input.
    }
}
