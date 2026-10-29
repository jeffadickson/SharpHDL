using System.Collections.ObjectModel;
using System.Numerics;
using SharpHDL.Core;
using SharpHDL.Simulation;

namespace SharpHDL.Verification;

/// <summary>
/// One-edge equivalence of combinational read-before-write single-port RAM transitions.
/// Admits one or two matching RAMs, depth 1..8, width 1..8, at most 64 RAM state bits,
/// 128 original nodes and eight outputs. Expansion must fit BitVectorEquivalence's
/// 256-node/128-input-bit profile. Registers, ROM and DSP operations remain unsupported.
/// Every pre-edge cell is symbolic; matching initial contents are admission metadata,
/// not an assumption restricting pre-edge state. No multi-edge/inductive proof is implied.
/// </summary>
public static class MemoryTransitionEquivalence
{
    /// <summary>
    /// Check outputs and every post-edge cell for all shared inputs and pre-edge cells.
    /// Timeout/resource budgets apply to each observable's solver check. SAT witnesses
    /// are replayed through original Core evaluation and HardwareMemoryTransitions.
    /// SMT queries are separated by reset commands so the retained stream is replayable.
    /// </summary>
    public static EquivalenceResult Check(HardwareGraph left,IReadOnlyDictionary<string,HardwareMemoryPort> leftPorts,
        IReadOnlyDictionary<string,int> leftOutputs,HardwareGraph right,IReadOnlyDictionary<string,HardwareMemoryPort> rightPorts,
        IReadOnlyDictionary<string,int> rightOutputs,int timeoutMilliseconds=5000,uint resourceLimit=0,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(left);ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(leftPorts);ArgumentNullException.ThrowIfNull(rightPorts);
        ArgumentNullException.ThrowIfNull(leftOutputs);ArgumentNullException.ThrowIfNull(rightOutputs);
        if(timeoutMilliseconds is <1 or >60000)throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
        EquivalenceResult Fail(EquivalenceStatus status,string message)=>new(status,message,
            new ReadOnlyDictionary<string,string>(new Dictionary<string,string>()),"","");
        string? problem=Validate(left,leftPorts,leftOutputs)??Validate(right,rightPorts,rightOutputs);
        if(problem is not null)return Fail(EquivalenceStatus.Unsupported,problem);
        var names=leftPorts.Keys.Order(StringComparer.Ordinal).ToArray();
        var outputs=leftOutputs.Keys.Order(StringComparer.Ordinal).ToArray();
        if(!names.SequenceEqual(rightPorts.Keys.Order(StringComparer.Ordinal))||!outputs.SequenceEqual(rightOutputs.Keys.Order(StringComparer.Ordinal)))
            return Fail(EquivalenceStatus.Unsupported,"RAM and output names must match ordinally.");
        foreach(string name in names)
        {
            var l=leftPorts[name];var r=rightPorts[name];
            if(l.Width!=r.Width||!l.Initial.SequenceEqual(r.Initial))return Fail(EquivalenceStatus.Unsupported,"RAM widths, depths and initial contents must match.");
        }
        if(cancellationToken.IsCancellationRequested)return Fail(EquivalenceStatus.Cancelled,"Caller cancelled before expansion.");
        var a=Expand(left,leftPorts,leftOutputs,names,outputs);var b=Expand(right,rightPorts,rightOutputs,names,outputs);
        string smt="",version="";
        for(int i=0;i<a.Outputs.Length;i++)
        {
            var result=BitVectorEquivalence.Check(a.Graph,a.Outputs[i],b.Graph,b.Outputs[i],timeoutMilliseconds,resourceLimit,cancellationToken);
            smt+="(reset)\n"+result.SmtLib;version=result.SolverVersion;
            if(result.Status==EquivalenceStatus.Different)
            {
                var values=result.Counterexample.ToDictionary(p=>p.Key,p=>BigInteger.Parse(p.Value,System.Globalization.CultureInfo.InvariantCulture),StringComparer.Ordinal);
                var before=names.Select((name,m)=>(name,cells:(IReadOnlyList<BigInteger>)Array.AsReadOnly(Enumerable.Range(0,leftPorts[name].Initial.Count)
                    .Select(cell=>values[CellName(m,cell)]).ToArray()))).ToDictionary(p=>p.name,p=>p.cells,StringComparer.Ordinal);
                IReadOnlyDictionary<int,BigInteger> Evaluate(HardwareGraph g)=>HardwareGraphEvaluator.Evaluate(g,
                    g.Nodes.Where(n=>n.Operation==HardwareOperation.Input).ToDictionary(n=>n.Id,n=>values[n.Name!]),before);
                var lv=Evaluate(left);var rv=Evaluate(right);
                var la=HardwareMemoryTransitions.Commit(left,leftPorts,before,lv);var ra=HardwareMemoryTransitions.Commit(right,rightPorts,before,rv);
                bool different=outputs.Any(o=>lv[leftOutputs[o]]!=rv[rightOutputs[o]])||names.Any(n=>!la[n].SequenceEqual(ra[n]));
                return result with {Status=different?EquivalenceStatus.Different:EquivalenceStatus.Unknown,
                    Reason=different?"SAT: one-edge memory/output difference replayed through original Core transitions.":"Expanded witness failed original memory-transition replay.",SmtLib=smt};
            }
            if(result.Status!=EquivalenceStatus.Equivalent)return result with{SmtLib=smt};
        }
        return new(EquivalenceStatus.Equivalent,"UNSAT for every output and post-edge RAM cell over all admitted inputs/pre-edge cells.",
            new ReadOnlyDictionary<string,string>(new Dictionary<string,string>()),smt,version);
    }

    private static string CellName(int memory,int cell)=>"ramstate"+memory+"_"+cell;

    private static string? Validate(HardwareGraph graph,IReadOnlyDictionary<string,HardwareMemoryPort> ports,IReadOnlyDictionary<string,int> outputs)
    {
        if(graph.Nodes.Count is <1 or >128||graph.StateInputNodeIds.Count!=0||outputs.Count is <1 or >8||ports.Count is <1 or >2)
            return "Expected 1..128 nodes, no register state, 1..8 outputs and 1..2 RAMs.";
        var nodes=graph.Nodes.ToDictionary(n=>n.Id);
        if(outputs.Values.Any(id=>!nodes.ContainsKey(id)))return "Missing output node.";
        if(graph.Nodes.Any(n=>n.Type.Width>32||n.Operation==HardwareOperation.Input&&(n.Name is null||n.Name.StartsWith("ramstate",StringComparison.Ordinal))))
            return "Node widths must fit 32 bits and input names must not use the reserved ramstate prefix.";
        int bits=0;
        foreach(var (name,p) in ports)
        {
            if(p is null||p.Name!=name||p.Width>8||p.Initial.Count>8)return "RAM profile is width/depth 1..8 with matching explicit names.";
            bits+=p.Width*p.Initial.Count;
            if(!nodes.TryGetValue(p.AddressNode,out _)||!nodes.TryGetValue(p.WriteEnableNode,out var enable)||enable.Type.Width!=1||
                !nodes.TryGetValue(p.WriteDataNode,out var data)||data.Type.Width!=p.Width)return "RAM port node/width mismatch.";
        }
        if(bits>64)return "RAM state exceeds the 64-bit budget.";
        foreach(var node in graph.Nodes.Where(n=>n.Operation==HardwareOperation.MemoryRead))
            if(node.MemoryName is null||!ports.TryGetValue(node.MemoryName,out var port)||node.Type.Width!=port.Width)return "RAM read has a missing or mismatched physical binding.";
        return null;
    }

    private static (HardwareGraph Graph,int[] Outputs) Expand(HardwareGraph graph,IReadOnlyDictionary<string,HardwareMemoryPort> ports,
        IReadOnlyDictionary<string,int> outputs,string[] names,string[] outputNames)
    {
        var nodes=new List<HardwareNode>();var mapping=new Dictionary<int,int>();var cells=new Dictionary<string,int[]>(StringComparer.Ordinal);
        int Add(HardwareOperation operation,int width,int[] operands,BigInteger? constant=null,string? name=null)
        {int id=nodes.Count;nodes.Add(new(id,operation,new(width),operands,constant,name));return id;}
        int K(int width,BigInteger value)=>Add(HardwareOperation.Constant,width,[],value);
        int Mux(int condition,int yes,int no,int width)=>Add(HardwareOperation.Select,width,[condition,yes,no]);
        for(int m=0;m<names.Length;m++)
        {var p=ports[names[m]];cells[names[m]]=Enumerable.Range(0,p.Initial.Count).Select(c=>Add(HardwareOperation.Input,p.Width,[],name:CellName(m,c))).ToArray();}
        foreach(var node in graph.Nodes)
        {
            if(node.Operation!=HardwareOperation.MemoryRead)
            {int id=nodes.Count;nodes.Add(node with{Id=id,Operands=node.Operands.Select(o=>mapping[o]).ToArray()});mapping[node.Id]=id;continue;}
            var words=cells[node.MemoryName!];int address=mapping[node.Operands[0]],width=nodes[address].Type.Width;
            int selected=K(node.Type.Width,0);
            for(int cell=0;cell<words.Length;cell++)if(cell<(BigInteger.One<<width))
                selected=Mux(Add(HardwareOperation.Equal,1,[address,K(width,cell)]),words[cell],selected,node.Type.Width);
            mapping[node.Id]=selected;
        }
        var observed=outputNames.Select(n=>mapping[outputs[n]]).ToList();
        foreach(string name in names)
        {
            var p=ports[name];int address=mapping[p.AddressNode],width=nodes[address].Type.Width;
            for(int cell=0;cell<p.Initial.Count;cell++)
            {
                int prior=cells[name][cell];
                if(cell>=(BigInteger.One<<width)){observed.Add(prior);continue;}
                int hit=Add(HardwareOperation.And,1,[mapping[p.WriteEnableNode],Add(HardwareOperation.Equal,1,[address,K(width,cell)])]);
                observed.Add(Mux(hit,mapping[p.WriteDataNode],prior,p.Width));
            }
        }
        return(new HardwareGraph(nodes),observed.ToArray());
    }
}
