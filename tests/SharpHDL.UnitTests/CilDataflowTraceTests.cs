using System;
using System.Linq;
using System.Numerics;
using SharpHDL;
using Xunit;

namespace SharpHDL.UnitTests;

public sealed class CilDataflowTraceTests
{
    public static void Pipeline(HwStream<ushort> source, HwStream<ushort> sink)
    {
        while (true) { ushort x = source.Read(); sink.Write((ushort)(x + 3)); }
    }

    [Fact]
    public void Cil_pipeline_has_graph_and_observable_backpressure()
    {
        var m = new Module("CilFlow");
        var source = HwStream<ushort>.Input(m, "input");
        var sink = HwStream<ushort>.Output(m, "output");
        var handle = CilCompiler.AttachPipeline(m, "adder",
            typeof(CilDataflowTraceTests).GetMethod(nameof(Pipeline)), source, sink);
        Assert.NotNull(handle.DataflowGraph);
        Assert.NotEmpty(handle.DataflowGraph.Nodes);
        Assert.NotEmpty(handle.DataflowGraph.Edges);
        Assert.Equal(handle.Latency, handle.DataflowGraph.Latency);
        Assert.Contains(handle.DataflowGraph.Nodes, n => n.Name.Contains("Pipeline"));

        var sim = m.Simulate();
        var compiled = new CompiledSequentialSimulator(m);
        var outputs = m.Ports.Where(p => p.IsOutput).ToArray();
        void Compare()
        {
            foreach (var port in outputs)
                Assert.Equal(sim.Read(port), compiled.Read(port));
        }
        void Set(Signal port, int value)
        {
            sim.Set(port, value);
            compiled.Set(port, value);
        }
        void Tick(bool reset = false)
        {
            sim.Tick(reset);
            compiled.Tick(reset);
            Compare();
        }

        // Typed channels expose _ready, not the AXI-specific _tready spelling.
        // Use their actual Signal instances instead of duplicating naming rules.
        Set(source.Valid, 0);
        Set(source.Data, 0);
        Set(sink.Ready, 0);
        Tick(true);
        var probe = new PipelineSignalProbe(handle,
            source.Valid, source.Ready, sink.Valid, sink.Ready);
        Set(source.Valid, 1);
        var first = probe.Sample(sim, 0);
        Assert.True(first.InputAccepted);
        Assert.False(first.OutputAccepted);
        for (int i = 0; i < handle.Latency; i++) Tick();
        var full = probe.Sample(sim, handle.Latency);
        Assert.True(full.OutputStalled);
        Assert.True(full.InputStalled);
        Assert.All(full.StageOccupied, Assert.True);
        Assert.Equal(new BigInteger(3), sim.Read(sink.Data));

        // A stall must hold the buffered result; release then drains the circuit.
        Tick();
        Assert.Equal(new BigInteger(3), sim.Read(sink.Data));
        Set(source.Valid, 0);
        Set(sink.Ready, 1);
        Compare();
        Assert.True(probe.Sample(sim, handle.Latency + 1).OutputAccepted);
        for (int i = 0; i < handle.Latency; i++) Tick();
        Assert.Equal(BigInteger.Zero, sim.Read(sink.Valid));
    }
}
