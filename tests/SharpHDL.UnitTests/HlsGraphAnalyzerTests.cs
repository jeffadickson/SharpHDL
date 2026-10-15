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
}
