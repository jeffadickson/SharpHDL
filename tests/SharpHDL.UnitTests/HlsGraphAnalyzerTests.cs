using System;
using System.Linq;
using SharpHDL;
using SharpHDL.Core;
using SharpHDL.HLS;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>
/// Proves the independent HLS assembly can consume real canonical IR
/// produced by the production SharpHDL Module compatibility frontend.
/// </summary>
public sealed class HlsGraphAnalyzerTests
{
    [Fact]
    public void Multiplier_cost_changes_planned_depth_without_changing_hardware()
    {
        var m=new Module("HlsCorePlanning");
        var a=m.Input("a",16);
        var b=m.Input("b",16);
        var output=m.Output("product",16);
        m.Drive(output,(a*b)+a);
        var export=m.ExportCoreGraph();
        var cheap=HlsGraphAnalyzer.Analyze(export.Graph,4,
            new HlsCostProfile{MultiplyCost=1});
        var expensive=HlsGraphAnalyzer.Analyze(export.Graph,4,
            new HlsCostProfile{MultiplyCost=8});
        Assert.Equal(1,cheap.MultiplyNodes);
        Assert.Equal(1,expensive.MultiplyNodes);
        Assert.True(expensive.CriticalPathCost>cheap.CriticalPathCost);
        Assert.True(expensive.SuggestedStages>cheap.SuggestedStages);
        Assert.Equal(0,expensive.DirectDspNodes);
        Assert.Contains(" * ",m.Verilog());
    }

    [Fact]
    public void Direct_dsp_has_separate_cost_and_is_counted()
    {
        var m=new Module("HlsDirectDspPlanning");
        var a=m.Input("a",16);
        var b=m.Input("b",16);
        var output=m.Output("product",32);
        m.Drive(output,Dsp48E2.MultiplyUnsigned(a,b));
        var plan=HlsGraphAnalyzer.Analyze(m.ExportCoreGraph().Graph,4);
        Assert.Equal(0,plan.MultiplyNodes);
        Assert.Equal(1,plan.DirectDspNodes);
        Assert.Equal(2,plan.CriticalPathCost);
        Assert.Equal(1,plan.SuggestedStages);
    }

    [Fact]
    public void Real_TinyRisc_core_graph_can_be_analyzed_by_independent_HLS()
    {
        var m=TinyRisc.Build(new ushort[]{0xf001,0xf000},"HlsTinyRisc");
        var graph=m.ExportCoreGraph().Graph;
        var plan=HlsGraphAnalyzer.Analyze(graph,6);
        Assert.Equal(graph.Nodes.Count,plan.Nodes.Count);
        Assert.True(plan.CriticalPathCost>0);
        Assert.True(plan.SuggestedStages>0);
        Assert.All(plan.Nodes,n=>Assert.True(n.Stage>=0));
    }

    [Fact]
    public void Invalid_planning_costs_fail_instead_of_inventing_timing()
    {
        var m=new Module("HlsBadBudget");
        var a=m.Input("a",8);
        var output=m.Output("output",8);
        m.Drive(output,a);
        var graph=m.ExportCoreGraph().Graph;
        Assert.Throws<ArgumentOutOfRangeException>(()=>HlsGraphAnalyzer.Analyze(graph,0));
        Assert.Throws<ArgumentOutOfRangeException>(()=>
            HlsGraphAnalyzer.Analyze(graph,4,new HlsCostProfile{MultiplyCost=0}));
    }
    [Fact]
    public void Constant_folder_preserves_modulo_arithmetic_and_source_node_ids()
    {
        var m=new Module("FoldCanonical");
        var input=m.Input("input",8);
        var output=m.Output("output",8);
        var subexpression=(Expr.Const(8,255)+Expr.Const(8,2))*
            Expr.Const(8,7);
        m.Drive(output,input+subexpression);
        var before=m.ExportCoreGraph();
        var after=HlsConstantFolder.Fold(before.Graph);
        Assert.Equal(before.Graph.Nodes.Select(n=>n.Id),after.Nodes.Select(n=>n.Id));
        Assert.True(after.Nodes.Count(n=>n.Operation==HardwareOperation.Constant)>
            before.Graph.Nodes.Count(n=>n.Operation==HardwareOperation.Constant));
        Assert.Equal(0,after.Nodes.Count(n=>n.Operation==HardwareOperation.Multiply));
        var sim=m.Simulate();
        sim.Tick(true);
        foreach(var x in new[]{0,1,7,100,255})
        {
            sim.Set(input,x);
            var inputs=new System.Collections.Generic.Dictionary<int,System.Numerics.BigInteger>
            { [before.Inputs["input"]]=x };
            var actual=SharpHDL.Simulation.HardwareGraphEvaluator.Evaluate(after,inputs);
            Assert.Equal(sim.Read(output),actual[before.Outputs["output"]]);
        }
        Assert.Equal(before.Graph.Nodes.Count,after.Nodes.Count);
    }

    [Fact]
    public void Constant_folder_does_not_fold_memory_reads_or_state_inputs()
    {
        var m=new Module("FoldMemory");
        var address=m.Input("address",2);
        var write=m.Input("write",1);
        var data=m.Input("data",8);
        var ram=SynchronousRam.Create(m,"ram",8,4,address,write,data,
            Expr.Const(1,1),new System.Numerics.BigInteger[]{1,2,3,4});
        var output=m.Output("output",8);
        m.Drive(output,ram.ReadData);
        var original=m.ExportCoreGraph();
        var folded=HlsConstantFolder.Fold(original.Graph);
        Assert.Contains(folded.Nodes,n=>n.Operation==HardwareOperation.MemoryRead);
        Assert.Contains(folded.Nodes,n=>n.Operation==HardwareOperation.Input &&
            n.Name=="ram_read_data");
    }

}
