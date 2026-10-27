using SharpHDL.Core;
using SharpHDL.HLS;
using SharpHDL.Simulation;
using Xunit;
using NSubstitute;

namespace SharpHDL.UnitTests;

/// <summary>Legality, stable arbitration and real banked-RAM/shared-multiplier execution.</summary>
public sealed class ScheduledOperationPlanTests
{
    private static readonly Dictionary<string,int> Capacity=new(){{"ram",1},{"mul",1}};

    /// <summary>Two banks execute in parallel while a single multiplier serializes contenders.</summary>
    [Fact]
    public void Plan_executes_banked_memory_and_shared_arithmetic()
    {
        var plan=SharpHDL.Examples.ScheduledPlannedBanks.Plan();
        Assert.Equal(6,plan.CycleCount);Assert.Equal(0,plan.Cycles["a"]);Assert.Equal(0,plan.Cycles["b"]);
        var design=SharpHDL.Examples.ScheduledPlannedBanks.Build();
        Assert.Single(design.Graph.Nodes,n=>n.Operation==HardwareOperation.Multiply);
        var machine=new HardwareGraphMachine(design.Graph,design.Inputs,design.Outputs,design.NextState,design.ResetValues,design.Memories);
        for(int replay=0;replay<2;replay++)
        {
            machine.SetInput("left",7);machine.SetInput("right",11);machine.Reset();
            for(int edge=0;edge<plan.CycleCount;edge++){Assert.Equal(0,(int)machine.ReadOutput("done"));machine.Tick();}
            Assert.Equal(1,(int)machine.ReadOutput("done"));Assert.Equal(98,(int)machine.ReadOutput("result"));
            machine.Tick();Assert.Equal(98,(int)machine.ReadOutput("result"));
        }
    }

    /// <summary>Illegal declarations and disconnected cycles cannot hang the planner.</summary>
    [Fact]
    public void Malformed_graphs_and_capacity_conflicts_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(()=>ScheduledOperationPlan.Create(null!,Capacity));
        Assert.Throws<ArgumentNullException>(()=>ScheduledOperationPlan.Create([],null!));
        Assert.Throws<ArgumentException>(()=>new ScheduledOperation("","ram"));
        Assert.Throws<ArgumentException>(()=>new ScheduledOperation("a","ram",""));
        Assert.Throws<ArgumentNullException>(()=>new ScheduledOperation("a","ram",null!));
        Assert.Throws<ArgumentException>(()=>ScheduledOperationPlan.Create([],Capacity));
        Assert.Throws<ArgumentException>(()=>ScheduledOperationPlan.Create([null!],Capacity));
        Assert.Throws<ArgumentException>(()=>ScheduledOperationPlan.Create([new("valid","ram"),null!],Capacity));
        Assert.Throws<ArgumentException>(()=>ScheduledOperationPlan.Create([new("a","missing")],Capacity));
        Assert.Throws<ArgumentException>(()=>ScheduledOperationPlan.Create([new("a","ram","missing")],Capacity));
        Assert.Throws<ArgumentException>(()=>ScheduledOperationPlan.Create([new("a","ram"),new("a","ram")],Capacity));
        Assert.Throws<ArgumentException>(()=>ScheduledOperationPlan.Create([new("a","ram")],new Dictionary<string,int>{{"ram",0}}));
        Assert.Throws<ArgumentException>(()=>ScheduledOperationPlan.Create([new("a","ram")],new Dictionary<string,int>{{"ram",1},{"unused",0}}));
        Assert.Throws<ArgumentException>(()=>ScheduledOperationPlan.Create([new("a","RAM")],new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase){{"ram",1}}));
        Assert.Throws<ArgumentException>(()=>ScheduledOperationPlan.Create([new("a","ram","a")],Capacity));
        Assert.Throws<ArgumentException>(()=>ScheduledOperationPlan.Create([new("root","ram"),new("a","ram","b"),new("b","ram","a")],Capacity));
        var ops=new[]{new ScheduledOperation("a","ram"),new ScheduledOperation("b","ram")};
        Assert.Contains("conflicts",Assert.Throws<ArgumentException>(()=>ScheduledOperationPlan.ValidateAssignments(ops,Capacity,new Dictionary<string,int>{{"a",0},{"b",0}})).Message);
        Assert.Throws<ArgumentException>(()=>ScheduledOperationPlan.ValidateAssignments(ops,Capacity,new Dictionary<string,int>{{"a",-1},{"b",1}}));
        Assert.Throws<ArgumentException>(()=>ScheduledOperationPlan.ValidateAssignments(ops,Capacity,new Dictionary<string,int>{{"a",0}}));
        Assert.Throws<ArgumentNullException>(()=>ScheduledOperationPlan.ValidateAssignments(ops,Capacity,null!));
        Assert.Throws<ArgumentException>(()=>ScheduledOperationPlan.ValidateAssignments(ops,Capacity,new Dictionary<string,int>{{"a",0},{"b",4095}}));
        Assert.Throws<ArgumentException>(()=>ScheduledOperationPlan.ValidateAssignments(ops,Capacity,new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase){{"A",0},{"b",1}}));
        Assert.Throws<ArgumentException>(()=>ScheduledOperationPlan.ValidateAssignments([new("a","ram"),new("b","mul","a")],Capacity,new Dictionary<string,int>{{"a",1},{"b",1}}));
    }

    /// <summary>Priority is ordinal across names, not merely invariant under input reversal.</summary>
    [Fact]
    public void Ready_contenders_use_exact_ordinal_priority()
    {
        var plan=ScheduledOperationPlan.Create(new[]{"z","a","Z","ä"}.Select(n=>new ScheduledOperation(n,"ram")),Capacity);
        Assert.Equal(new[]{"Z","a","z","ä"},plan.Cycles.Keys);
        Assert.Equal(new[]{0,1,2,3},plan.Cycles.Values);
    }

    /// <summary>Public operation and edge budgets reject one beyond their supported limits.</summary>
    [Fact]
    public void Graph_resource_budgets_are_enforced()
    {
        var operations=Enumerable.Range(0,4096).Select(i=>new ScheduledOperation("op"+i,"ram")).ToArray();
        var capacity=new Dictionary<string,int>{{"ram",4096}};
        Assert.Equal(1,ScheduledOperationPlan.Create(operations.Take(4095),capacity).CycleCount);
        Assert.Throws<ArgumentException>(()=>ScheduledOperationPlan.Create(operations,capacity));
        var names=Enumerable.Range(0,257).Select(i=>"n"+i).ToArray();
        Assert.Contains("edge budget",Assert.Throws<ArgumentException>(()=>ScheduledOperationPlan.Create(names.Select(n=>new ScheduledOperation(n,"ram",names)),capacity)).Message);
        var exact=Enumerable.Range(0,363).Select(i=>new ScheduledOperation("v"+i.ToString("D3"),"ram",
            Enumerable.Range(i==362?168:0,i==362?194:i).Select(j=>"v"+j.ToString("D3")).ToArray())).ToArray();
        // Complete DAG has 65,703 arcs. Remove 168 from its final node, then
        // add the one synthetic-root edge: exactly 65,536 accepted entries.
        Assert.Equal(363,ScheduledOperationPlan.Create(exact,capacity).CycleCount);
    }

    /// <summary>Independent work has an analytic makespan; reversed input cannot change arbitration.</summary>
    [FsCheck.Xunit.Property(MaxTest=100,Replay="1145655947,296144285")]
    public void Independent_work_matches_capacity_bound(byte size,byte lanes)
    {
        int count=1+size%64,capacity=1+lanes%8;
        var ops=Enumerable.Range(0,count).Select(i=>new ScheduledOperation("op"+i.ToString("D3"),"ram")).ToArray();
        var capacities=new Dictionary<string,int>{{"ram",capacity}};
        var first=ScheduledOperationPlan.Create(ops,capacities);var second=ScheduledOperationPlan.Create(ops.Reverse(),capacities);
        Assert.Equal((count+capacity-1)/capacity,first.CycleCount);
        Assert.Equal(first.Cycles,second.Cycles);
        foreach(var group in first.Cycles.GroupBy(p=>p.Value))Assert.InRange(group.Count(),1,capacity);
    }

    /// <summary>Snapshots do not leak mutation; explicit physical conflicts remain independently checked at lowering.</summary>
    [Fact]
    public void Plan_is_immutable_and_emission_failures_remain_visible()
    {
        string[] dependencies=["a"];var op=new ScheduledOperation("b","ram",dependencies);dependencies[0]="bad";
        var plan=ScheduledOperationPlan.Create([new("a","ram"),op],Capacity);
        Assert.Throws<NotSupportedException>(()=>((IDictionary<string,int>)plan.Cycles).Clear());
        Assert.Equal(1,plan.Cycles["b"]);Assert.Equal("a",op.Dependencies[0]);
        Assert.Throws<ArgumentNullException>(()=>plan.EmitStates(null!,"x",(_,_)=>{}));
        Assert.Throws<ArgumentNullException>(()=>plan.EmitStates(new("x"),"x",null!));
        Assert.Throws<ArgumentException>(()=>plan.EmitStates(new("x"),"",(_,_)=>{}));
        Assert.Throws<InvalidOperationException>(()=>plan.EmitStates(new("x"),"x",(_,_)=>throw new InvalidOperationException("callback")));
    }

    /// <summary>A callback cannot evade actual single-port checks by using two logical aliases.</summary>
    [Fact]
    public void Lowering_rejects_misdeclared_physical_resources()
    {
        var plan=ScheduledOperationPlan.Create([new("a","left"),new("b","right")],new Dictionary<string,int>{{"left",1},{"right",1}});
        var builder=new ScheduledMachineBuilder("Alias");var memory=builder.Memory("physical",8,4);
        plan.EmitStates(builder,"work",(state,_)=>state.Write(memory,builder.Constant(2,0),builder.Constant(8,1),builder.Constant(1,1)));
        Assert.Throws<InvalidOperationException>(()=>builder.Lower());
    }

    /// <summary>An inconsistent external configuration provider cannot make compilation spin.</summary>
    [Fact]
    public void Inconsistent_capacity_provider_fails_without_spinning()
    {
        var backing=new Dictionary<string,int>{{"ram",1}};
        var provider=Substitute.For<IReadOnlyDictionary<string,int>>();
        provider.Keys.Returns(backing.Keys);provider.GetEnumerator().Returns(_=>backing.GetEnumerator());
        provider["ram"].Returns(0); // Enumerated configuration and live lookup disagree.
        Assert.Contains("progress",Assert.Throws<InvalidOperationException>(()=>ScheduledOperationPlan.Create([new("a","ram")],provider)).Message);
    }
}
