#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SharpHDL;

public static partial class CilCompiler
{
    private static ScheduledRamPlan[] ShareScheduledRams(MethodInfo method,SequentialOptions options,ScheduledRamPlan[] plans)
    {
        var aliases=options.RamAliases;
        if(aliases is null||aliases.Count==0)return plans;
        var byParameter=plans.ToDictionary(p=>p.Parameter);
        foreach(var pair in aliases)
            if(!byParameter.ContainsKey(pair.Key)||!byParameter.ContainsKey(pair.Value))
                throw new ArgumentException("RAM aliases must name uint[] parameter indexes.",nameof(options));
        int Root(int parameter)
        {
            var visited=new HashSet<int>();
            while(aliases.TryGetValue(parameter,out int target))
            {
                if(!visited.Add(parameter))throw Error(method,0,"RAM alias bindings contain a cycle (including self bindings).");
                parameter=target;
            }
            return parameter;
        }
        var roots=plans.Select(p=>Root(p.Parameter)).Distinct().OrderBy(i=>i).Select((root,index)=>(root,tag:index+1)).ToDictionary(p=>p.root,p=>p.tag);
        return plans.Select(p=>
        {
            var root=byParameter[Root(p.Parameter)];
            if(p.Parameter!=root.Parameter)
            {
                if(options.MemoryDepths?.ContainsKey(p.Parameter)==true&&p.Depth!=root.Depth)
                    throw new ArgumentException("Shared RAM depths must agree with the canonical binding.",nameof(options));
                if(options.InitialMemories?.ContainsKey(p.Parameter)==true&&(root.Initial is null||!p.Initial!.SequenceEqual(root.Initial)))
                    throw new ArgumentException("Shared RAM initialization must agree with the canonical binding.",nameof(options));
            }
            return root with{Parameter=p.Parameter,Tag=roots[root.Parameter]};
        }).ToArray();
    }
}
