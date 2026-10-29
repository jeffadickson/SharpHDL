using System.Numerics;
using SharpHDL.Core;
using SharpHDL.Simulation;
using Xunit;

namespace SharpHDL.Verification.Tests;

/// <summary>Native proofs, original-transition counterexample replay and exhaustive tiny references.</summary>
public sealed class MemoryTransitionEquivalenceTests
{
    private static (HardwareGraph Graph,Dictionary<string,HardwareMemoryPort> Ports,Dictionary<string,int> Outputs) Design(bool invert=false,bool bypass=false,int depth=3)
    {
        HardwareNode[] nodes=[new(0,HardwareOperation.Input,new(2),[],Name:"address"),
            new(1,HardwareOperation.Input,new(2),[],Name:"word"),new(2,HardwareOperation.Input,new(1),[],Name:"write"),
            new(3,HardwareOperation.MemoryRead,new(2),[0],MemoryName:"ram"),
            new(4,HardwareOperation.Not,new(2),[1]),new(5,HardwareOperation.Select,new(2),[2,1,3])];
        return(new(nodes),new(){{"ram",new("ram",2,0,2,invert?4:1,Enumerable.Repeat(BigInteger.Zero,depth))}},new(){{"read",bypass?5:3}});
    }

    [Fact]
    public void Identical_transition_is_proved_for_arbitrary_prestate()
    {
        var a=Design();var result=MemoryTransitionEquivalence.Check(a.Graph,a.Ports,a.Outputs,a.Graph,a.Ports,a.Outputs);
        Assert.Equal(EquivalenceStatus.Equivalent,result.Status);Assert.Contains("check-sat",result.SmtLib);
        Retain("identity",result);
    }

    [Fact]
    public void Structurally_different_write_functions_are_proved_equal()
    {
        var a=Design(invert:true);var b=Design();
        var nodes=b.Graph.Nodes.Concat(new HardwareNode[]{new(6,HardwareOperation.Constant,new(2),[],3),
            new(7,HardwareOperation.Xor,new(2),[1,6])});
        b.Ports["ram"]=new("ram",2,0,2,7,[0,0,0]);
        var r=MemoryTransitionEquivalence.Check(a.Graph,a.Ports,a.Outputs,new(nodes),b.Ports,b.Outputs);
        Assert.Equal(EquivalenceStatus.Equivalent,r.Status);Retain("not-xor-equivalence",r);
    }

    [Fact]
    public void Exhausted_native_work_budget_is_unknown()
    {
        var a=Design();var b=Design();
        var left=new HardwareGraph(a.Graph.Nodes.Concat(new[]{new HardwareNode(6,HardwareOperation.Multiply,new(2),[1,1])}));
        var right=new HardwareGraph(b.Graph.Nodes.Concat(new[]{new HardwareNode(6,HardwareOperation.Constant,new(2),[],1),new HardwareNode(7,HardwareOperation.And,new(2),[1,6])}));
        a.Ports["ram"]=new("ram",2,0,2,6,[0,0,0]);b.Ports["ram"]=new("ram",2,0,2,7,[0,0,0]);
        var r=MemoryTransitionEquivalence.Check(left,a.Ports,a.Outputs,right,b.Ports,b.Outputs,resourceLimit:1);
        Assert.Equal(EquivalenceStatus.Unknown,r.Status);Retain("resource-unknown",r);
    }

    [Fact]
    public void Last_cell_only_difference_cannot_be_omitted()
    {
        var a=Design();var b=Design();
        var graph=new HardwareGraph(b.Graph.Nodes.Concat(new HardwareNode[]{new(6,HardwareOperation.Constant,new(2),[],2),
            new(7,HardwareOperation.Equal,new(1),[0,6]),new(8,HardwareOperation.Not,new(1),[7]),new(9,HardwareOperation.And,new(1),[2,8])}));
        b.Ports["ram"]=new("ram",2,0,9,1,[0,0,0]);
        var r=MemoryTransitionEquivalence.Check(a.Graph,a.Ports,a.Outputs,graph,b.Ports,b.Outputs);
        Assert.Equal(EquivalenceStatus.Different,r.Status);Assert.Equal("2",r.Counterexample["address"]);Retain("last-cell-defect",r);
    }

    [Fact]
    public void Unaddressable_cells_are_preserved_and_an_unread_second_RAM_is_compared()
    {
        var a=Design();var narrow=new HardwareGraph(a.Graph.Nodes.Select(n=>n.Id==0?n with{Type=new(1)}:n));
        Assert.Equal(EquivalenceStatus.Equivalent,MemoryTransitionEquivalence.Check(narrow,a.Ports,a.Outputs,narrow,a.Ports,a.Outputs).Status);
        var b=Design();a.Ports.Add("other",new("other",2,0,2,1,[0,0]));b.Ports.Add("other",new("other",2,0,2,4,[0,0]));
        var r=MemoryTransitionEquivalence.Check(a.Graph,a.Ports,a.Outputs,b.Graph,b.Ports,b.Outputs);
        Assert.Equal(EquivalenceStatus.Different,r.Status);Retain("unread-second-memory-defect",r);
    }

    [Fact]
    public void Excess_state_and_missing_read_binding_are_explicitly_unsupported()
    {
        var a=Design();var wide=new HardwareGraph(a.Graph.Nodes.Select(n=>n.Id==1||n.Id>=3?n with{Type=new(8)}:n));
        var ports=new Dictionary<string,HardwareMemoryPort>{{"ram",new("ram",8,0,2,1,new BigInteger[8])},{"other",new("other",8,0,2,1,new BigInteger[8])}};
        var r=MemoryTransitionEquivalence.Check(wide,ports,a.Outputs,wide,ports,a.Outputs);
        Assert.Equal(EquivalenceStatus.Unsupported,r.Status);Assert.Contains("64-bit",r.Reason);
        var absent=new Dictionary<string,HardwareMemoryPort>{{"other",new("other",2,0,2,1,[0,0,0])}};
        Assert.Equal(EquivalenceStatus.Unsupported,MemoryTransitionEquivalence.Check(a.Graph,absent,a.Outputs,a.Graph,absent,a.Outputs).Status);
    }

    [Fact]
    public void Maximum_state_budget_is_admitted_and_one_differing_output_is_sufficient()
    {
        var maximum=Design(depth:8);
        var wide=new HardwareGraph(maximum.Graph.Nodes.Select(n=>n.Id==0?n with{Type=new(3)}:n.Id==1||n.Id>=3?n with{Type=new(8)}:n));
        var ports=new Dictionary<string,HardwareMemoryPort>{{"ram",new("ram",8,0,2,1,new BigInteger[8])}};
        Assert.Equal(EquivalenceStatus.Equivalent,MemoryTransitionEquivalence.Check(wide,ports,maximum.Outputs,wide,ports,maximum.Outputs).Status);
        var a=Design();var b=Design(bypass:true);
        HardwareGraph Extra(HardwareGraph graph)=>new(graph.Nodes.Concat(new[]{new HardwareNode(6,HardwareOperation.Constant,new(2),[],0)}));
        a.Outputs.Add("same",6);b.Outputs.Add("same",6);
        Assert.Equal(EquivalenceStatus.Different,MemoryTransitionEquivalence.Check(Extra(a.Graph),a.Ports,a.Outputs,Extra(b.Graph),b.Ports,b.Outputs).Status);
    }

    [Theory][InlineData(true,false)][InlineData(false,true)]
    public void Write_or_collision_defect_produces_an_original_Core_witness(bool invert,bool bypass)
    {
        var a=Design();var b=Design(invert,bypass);
        var r=MemoryTransitionEquivalence.Check(a.Graph,a.Ports,a.Outputs,b.Graph,b.Ports,b.Outputs);
        Assert.Equal(EquivalenceStatus.Different,r.Status);Assert.Contains("original Core",r.Reason);
        var v=r.Counterexample.ToDictionary(p=>p.Key,p=>BigInteger.Parse(p.Value));
        IReadOnlyDictionary<string,IReadOnlyList<BigInteger>> before=new Dictionary<string,IReadOnlyList<BigInteger>>
        {{"ram",Enumerable.Range(0,3).Select(c=>v["ramstate0_"+c]).ToArray()}};
        var inputs=new Dictionary<int,BigInteger>{{0,v["address"]},{1,v["word"]},{2,v["write"]}};
        var lv=HardwareGraphEvaluator.Evaluate(a.Graph,inputs,before);var rv=HardwareGraphEvaluator.Evaluate(b.Graph,inputs,before);
        var lm=HardwareMemoryTransitions.Commit(a.Graph,a.Ports,before,lv);var rm=HardwareMemoryTransitions.Commit(b.Graph,b.Ports,before,rv);
        Assert.True(lv[a.Outputs["read"]]!=rv[b.Outputs["read"]]||!lm["ram"].SequenceEqual(rm["ram"]));
        Retain(invert?"write-defect":"write-first-defect",r);
    }

    [Fact]
    public void Original_transition_matches_exhaustive_independent_array_reference()
    {
        var a=Design(depth:2);
        for(int state=0;state<16;state++)for(int address=0;address<4;address++)for(int word=0;word<4;word++)for(int enabled=0;enabled<2;enabled++)
        {
            BigInteger[] prior=[state&3,state>>2];
            var before=new Dictionary<string,IReadOnlyList<BigInteger>>{{"ram",prior}};
            var evaluated=HardwareGraphEvaluator.Evaluate(a.Graph,new Dictionary<int,BigInteger>{{0,address},{1,word},{2,enabled}},before);
            Assert.Equal(address<2?prior[address]:0,evaluated[3]);
            var after=HardwareMemoryTransitions.Commit(a.Graph,a.Ports,before,evaluated);
            var expected=prior.ToArray();if(enabled==1&&address<2)expected[address]=word;
            Assert.Equal(expected,after["ram"]);Assert.Equal(new BigInteger[]{state&3,state>>2},prior);
        }
    }

    [Fact]
    public void Admission_and_cancelled_results_are_not_proofs()
    {
        var a=Design();var large=Design(depth:9);
        Assert.Equal(EquivalenceStatus.Unsupported,MemoryTransitionEquivalence.Check(large.Graph,large.Ports,large.Outputs,large.Graph,large.Ports,large.Outputs).Status);
        var missing=new Dictionary<string,int>{{"read",999}};
        Assert.Equal(EquivalenceStatus.Unsupported,MemoryTransitionEquivalence.Check(a.Graph,a.Ports,missing,a.Graph,a.Ports,a.Outputs).Status);
        using var cancel=new CancellationTokenSource();cancel.Cancel();
        Assert.Equal(EquivalenceStatus.Cancelled,MemoryTransitionEquivalence.Check(a.Graph,a.Ports,a.Outputs,a.Graph,a.Ports,a.Outputs,cancellationToken:cancel.Token).Status);
        var differentInit=Design();differentInit.Ports["ram"]=new("ram",2,0,2,1,[1,0,0]);
        Assert.Equal(EquivalenceStatus.Unsupported,MemoryTransitionEquivalence.Check(a.Graph,a.Ports,a.Outputs,differentInit.Graph,differentInit.Ports,differentInit.Outputs).Status);
    }

    private static void Retain(string name,EquivalenceResult result)
    {
        string? root=Environment.GetEnvironmentVariable("SHARPHDL_PROOF_EVIDENCE");if(root is null)return;
        if(!Directory.Exists(root))throw new DirectoryNotFoundException(root);
        File.WriteAllText(Path.Combine(root,"memory-"+name+".smt2"),result.SmtLib);
        File.WriteAllText(Path.Combine(root,"memory-"+name+".json"),System.Text.Json.JsonSerializer.Serialize(result));
    }
}
