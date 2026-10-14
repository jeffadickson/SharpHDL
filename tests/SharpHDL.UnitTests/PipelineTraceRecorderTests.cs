using System;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using SharpHDL;
using Xunit;

namespace SharpHDL.UnitTests;

public sealed class PipelineTraceRecorderTests
{
    [Fact]
    public void Recorded_cycle_samples_are_real_and_schema_is_versioned()
    {
        var m=new Module("RecordedTrace");
        var input=HwStream<ushort>.Input(m,"source");
        var output=HwStream<ushort>.Output(m,"sink");
        var graphExpr=(input.Data+Expr.Const(16,7))*Expr.Const(16,2);
        var handle=StructuralPipeline.Attach(m,"pipe",input,output,graphExpr);
        var sim=m.Simulate();
        sim.Set(input.Data,0);sim.Set(input.Valid,0);sim.Set(output.Ready,0);sim.Tick(true);
        var recorder=new PipelineTraceRecorder(new PipelineSignalProbe(handle,
            input.Valid,input.Ready,output.Valid,output.Ready));
        sim.Set(input.Valid,1);
        recorder.Capture(sim,0);
        Assert.True(recorder.Samples[0].InputAccepted);
        sim.Tick();
        recorder.Capture(sim,1);
        Assert.True(recorder.Samples[1].StageOccupied[0]);
        Assert.Throws<InvalidOperationException>(()=>recorder.Capture(sim,1));
        using var json=JsonDocument.Parse(recorder.ToJson(handle.DataflowGraph));
        Assert.Equal(1,json.RootElement.GetProperty("SchemaVersion").GetInt32());
        Assert.Equal("pre-edge",json.RootElement.GetProperty("Observation").GetString());
        Assert.Equal(2,json.RootElement.GetProperty("Samples").GetArrayLength());
    }
}
