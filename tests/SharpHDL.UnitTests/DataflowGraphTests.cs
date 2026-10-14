using System;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using SharpHDL;
using Xunit;

namespace SharpHDL.UnitTests;

public sealed class DataflowGraphTests
{
    static (Module,PipelineHandle) Make()
    {
        var m = new Module("GraphProbe");
        var input = HwStream<ushort>.Input(m, "source");
        var output = HwStream<ushort>.Output(m, "sink");
        var x = input.Data;
        var sum = (x + Expr.Const(16, 3)).Named("sum");
        var product = (sum * Expr.Const(16, 5)).Named("product");
        return (m, StructuralPipeline.Attach(m, "probe", input, output, product,
            new PipelineOptions { LogicLevelsPerStage = 1 }));
    }

    [Fact]
    public void Graph_has_topological_ids_edges_and_stages()
    {
        var (module, handle) = Make();
        var graph = handle.DataflowGraph;
        Assert.NotNull(graph);
        Assert.Equal(2, graph.Latency);
        Assert.Equal(1, graph.InitiationInterval);
        Assert.Equal(graph.Nodes.Count, graph.Nodes.Select(n => n.Id).Distinct().Count());
        Assert.All(graph.Edges, e =>
        {
            Assert.True(e.SourceId < e.TargetId);
            Assert.Contains(graph.Nodes, n => n.Id == e.SourceId);
            Assert.Contains(graph.Nodes, n => n.Id == e.TargetId);
        });
        Assert.Contains(graph.Nodes, n => n.Name == "sum" && n.Stage == 1);
        Assert.Contains(graph.Nodes, n => n.Name == "product" && n.Stage == 2);
        Assert.Equal(graph.ToJson(), Make().Item2.DataflowGraph.ToJson());
        using var json = JsonDocument.Parse(graph.ToJson());
        Assert.Equal(graph.Nodes.Count, json.RootElement.GetProperty("Nodes").GetArrayLength());
        Assert.Contains("h_GraphProbe", module.Verilog());
    }
}
