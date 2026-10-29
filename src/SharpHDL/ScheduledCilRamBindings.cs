#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;

namespace SharpHDL;

public static partial class CilCompiler
{
    private sealed record ScheduledRamPlan(int Parameter,int Tag,int Depth,int AddressWidth,BigInteger[]? Initial);

    private static ScheduledRamPlan[] PlanScheduledRams(MethodInfo method,SequentialOptions options)
    {
        var indexes=method.GetParameters().Select((p,i)=>(p,i)).Where(x=>x.p.ParameterType==typeof(uint[])).Select(x=>x.i).ToArray();
        if(indexes.Length>8)throw Error(method,0,"Scheduled methods support at most eight distinct physical RAM origins.");
        if(options.InitialMemory is not null&&(indexes.Length!=1||options.InitialMemories?.Count>0))
            throw Error(method,0,"Legacy InitialMemory requires exactly one RAM and cannot be combined with InitialMemories.");
        foreach(int key in (options.MemoryDepths?.Keys??Array.Empty<int>()).Concat(options.InitialMemories?.Keys??Array.Empty<int>()))
            if(!indexes.Contains(key))throw new ArgumentException("Per-RAM options must name a uint[] parameter index.",nameof(options));
        var plans=indexes.Select((parameter,ordinal)=>
        {
            int depth=options.MemoryDepths?.ContainsKey(parameter)==true?options.MemoryDepths[parameter]:options.MemoryDepth;
            if(depth<1||depth>65536)throw new ArgumentOutOfRangeException(nameof(options),"RAM depth must be in 1..65536.");
            BigInteger[]? initial=options.InitialMemory;
            if(options.InitialMemories?.ContainsKey(parameter)==true)
                initial=options.InitialMemories[parameter]??throw new ArgumentException("RAM initial words cannot be null.",nameof(options));
            int width=1;while((1<<width)<depth)width++;
            return new ScheduledRamPlan(parameter,ordinal+1,depth,width,initial);
        }).ToArray();
        return ShareScheduledRams(method,options,plans);
    }
}
