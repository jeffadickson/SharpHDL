using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using SharpHDL;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>
/// Production CIL Read/Write frontend compiled through independent Core IR
/// scheduling and bound back to real Module streams. Legacy scheduler remains
/// an independent reference, not part of the Core path.
/// </summary>
public sealed class CoreCilPipelineTests
{
    [HardwareProcess]
    public static void Arithmetic(HwStream<uint> input,HwStream<uint> output)
    {
        while(true)
        {
            uint x=input.Read();
            output.Write((x+11u)*x+9u);
        }
    }

    [HardwareProcess]
    public static void Branch(HwStream<uint> input,HwStream<uint> output)
    {
        while(true)
        {
            uint x=input.Read();
            output.Write((x&1u)==0u?x+7u:x*3u);
        }
    }

    [Theory]
    [InlineData(nameof(Arithmetic),1)]
    [InlineData(nameof(Arithmetic),3)]
    [InlineData(nameof(Branch),1)]
    [InlineData(nameof(Branch),3)]
    public void Core_CIL_pipeline_preserves_tokens_through_random_stalls(string methodName,int budget)
    {
        var module=new Module("CoreCil"+methodName);
        var input=HwStream<uint>.Input(module,"in");
        var output=HwStream<uint>.Output(module,"out");
        var method=typeof(CoreCilPipelineTests).GetMethod(methodName)!;
        var options=new PipelineOptions{
            UseCoreCompiler=true,LogicLevelsPerStage=budget,MaxStages=128};
        var handle=CilCompiler.AttachPipeline(module,"core",method,input,output,options);
        Assert.Contains("Canonical Core IR",handle.Report);
        Assert.Equal(1,handle.InitiationInterval);
        Assert.True(handle.Latency>=1);
        if(budget==1)Assert.True(handle.Latency>1);
        Assert.Equal(handle.Latency,handle.StageValid.Count);
        Assert.NotEmpty(handle.DataflowGraph.Nodes);
        Assert.Contains("core_valid_",module.Verilog());
        var sim=module.Simulate();
        sim.Tick(true);
        var rng=new Random(777);
        var expected=new Queue<BigInteger>();
        int accepted=0,completed=0;
        for(int cycle=0;cycle<900;cycle++)
        {
            int value=rng.Next();
            bool offer=cycle<750&&rng.Next(4)!=0;
            bool ready=cycle>=750||rng.Next(3)!=0;
            sim.Set(input.Data,value);
            sim.Set(input.Valid,offer?1:0);
            sim.Set(output.Ready,ready?1:0);
            if(ready && !sim.Read(output.Valid).IsZero)
            {
                Assert.NotEmpty(expected);
                Assert.Equal(expected.Dequeue(),sim.Read(output.Data));
                completed++;
            }
            if(offer && !sim.Read(input.Ready).IsZero)
            {
                var x=(uint)value;
                uint result=methodName==nameof(Arithmetic)?
                    unchecked((x+11u)*x+9u):
                    (x&1u)==0u?unchecked(x+7u):unchecked(x*3u);
                expected.Enqueue(result);
                accepted++;
            }
            sim.Tick();
        }
        Assert.Empty(expected);
        Assert.True(accepted>150);
        Assert.Equal(accepted,completed);
    }

    [Fact]
    public void Core_CIL_rejects_unsupported_DSP_hint_without_mutating_module()
    {
        var module=new Module("RejectCoreDsp");
        var input=HwStream<uint>.Input(module,"in");
        var output=HwStream<uint>.Output(module,"out");
        var method=typeof(HardwareSourceProvenanceTests)
            .GetMethod(nameof(HardwareSourceProvenanceTests.MultiplyDspPreferred))!;
        var error=Assert.Throws<NotSupportedException>(()=>
            CilCompiler.AttachPipeline(module,"mul",method,input,output,
                new PipelineOptions{UseCoreCompiler=true}));
        Assert.Contains("DSP hints",error.Message);
    }
}
