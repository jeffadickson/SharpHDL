using System;
using System.Linq;
using System.Numerics;
using SharpHDL;
using Xunit;

namespace SharpHDL.UnitTests;

public sealed class CilDataflowTraceTests
{
    [Fact]
    public void Cil_pipeline_has_graph_and_observable_backpressure()
    {
        var m = ScheduledPipeline.Build(out var handle);
        Assert.NotNull(handle.DataflowGraph);
        Assert.NotEmpty(handle.DataflowGraph.Nodes);
        Assert.NotEmpty(handle.DataflowGraph.Edges);
        Assert.Equal(handle.Latency, handle.DataflowGraph.Latency);
        Assert.Contains(handle.DataflowGraph.Nodes, n=>n.Name.Contains("Polynomial") || n.Name.Contains("polynomial"));
        var ports = m.Ports.ToDictionary(p=>p.Name);
        // AttachPipeline uses the same structural scheduler and graph emitter.
        // A probe may attach to the generated CIL pipeline through its visible handshakes.
        var sim = m.Simulate();
        sim.Set(ports["input_valid"], 0);
        sim.Set(ports["input_data"], 0);
        sim.Set(ports["output_tready"], 0);
        sim.Tick(true);
        var probe = new PipelineSignalProbe(handle,
            ports["input_valid"],ports["input_ready"],
            ports["output_tvalid"],ports["output_tready"]);
        sim.Set(ports["input_valid"],1);
        var first = probe.Sample(sim,0);
        Assert.True(first.InputAccepted);
        Assert.False(first.OutputAccepted);
        for(int i=0;i<handle.Latency;i++)sim.Tick();
        var full = probe.Sample(sim,handle.Latency);
        Assert.True(full.OutputStalled);
        Assert.True(full.InputStalled);
        Assert.All(full.StageOccupied, Assert.True);
        sim.Set(ports["output_tready"],1);
        var release = probe.Sample(sim,handle.Latency);
        Assert.True(release.OutputAccepted);
    }
}
