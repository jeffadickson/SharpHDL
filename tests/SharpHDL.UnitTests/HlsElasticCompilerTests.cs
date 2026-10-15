using System;
using System.Collections.Generic;
using System.Numerics;
using SharpHDL;
using SharpHDL.HLS;
using SharpHDL.Simulation;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>
/// Functional acceptance for the new HLS assembly's first actual compiler:
/// Core expression DAG to clocked, stallable, registered Core IR pipeline.
/// </summary>
public sealed class HlsElasticCompilerTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void Compiled_pipeline_preserves_order_and_values_under_random_backpressure(int budget)
    {
        var m=new Module("ElasticCoreSource");
        var input=m.Input("input",16);
        var output=m.Output("output",16);
        m.Drive(output,((input+Expr.Const(16,3))*input)+Expr.Const(16,7));
        var exported=m.ExportCoreGraph();
        var compiled=HlsElasticCompiler.Compile(exported.Graph,
            exported.Inputs["input"],exported.Outputs["output"],
            budget,new HlsCostProfile{MultiplyCost=2,ArithmeticCost=1});
        Assert.True(compiled.Latency>=1);
        Assert.True(compiled.PayloadRegisterBits>=16);
        Assert.Equal(1,compiled.InitiationInterval);
        var machine=new HardwareGraphMachine(compiled.Graph,compiled.Inputs,
            compiled.Outputs,compiled.NextState,compiled.ResetValues,
            new Dictionary<string,SharpHDL.Core.HardwareMemoryPort>());
        var rng=new Random(9713+budget);
        var expected=new Queue<BigInteger>();
        int accepted=0,completed=0;
        for(int cycle=0;cycle<700;cycle++)
        {
            bool draining=cycle>=600;
            int valid=draining?0:rng.Next(4)!=0?1:0;
            int ready=draining?1:rng.Next(3)==0?0:1;
            int value=rng.Next(65536);
            machine.SetInput("hls_input_data",value);
            machine.SetInput("hls_input_valid",valid);
            machine.SetInput("hls_output_ready",ready);
            bool inputReady=machine.ReadOutput("hls_input_ready")!=0;
            bool outputValid=machine.ReadOutput("hls_output_valid")!=0;
            if(outputValid&&ready!=0)
            {
                Assert.NotEmpty(expected);
                Assert.Equal(expected.Dequeue(),machine.ReadOutput("hls_output_data"));
                completed++;
            }
            if(inputReady&&valid!=0)
            {
                expected.Enqueue(((BigInteger)(value+3)*value+7)&65535);
                accepted++;
            }
            machine.Tick();
        }
        Assert.Empty(expected);
        Assert.True(accepted>150);
        Assert.Equal(accepted,completed);
        Assert.Equal(700,machine.Cycle);
    }

    [Fact]
    public void Rejects_unsupported_state_and_impossible_stage_budget()
    {
        var m=new Module("ElasticBad");
        var input=m.Input("input",8);
        var state=m.Register("state",8,0);
        var output=m.Output("output",8);
        m.Next(state,state+input);
        m.Drive(output,state);
        var export=m.ExportCoreGraph();
        Assert.Throws<NotSupportedException>(()=>HlsElasticCompiler.Compile(
            export.Graph,export.Inputs["input"],export.Outputs["output"]));
        var m2=new Module("ElasticBudget");
        var x=m2.Input("x",8);
        var y=m2.Output("y",8);
        m2.Drive(y,x*x);
        var graph=m2.ExportCoreGraph();
        Assert.Throws<NotSupportedException>(()=>HlsElasticCompiler.Compile(
            graph.Graph,graph.Inputs["x"],graph.Outputs["y"],1));
    }

    [Fact]
    public void Constant_folded_Core_graph_can_be_compiled_into_elastic_pipeline()
    {
        var m=new Module("FoldThenPipeline");
        var x=m.Input("x",8);
        var y=m.Output("y",8);
        m.Drive(y,x+(Expr.Const(8,250)+Expr.Const(8,20)));
        var export=m.ExportCoreGraph();
        var folded=HlsConstantFolder.Fold(export.Graph);
        var compiled=HlsElasticCompiler.Compile(folded,
            export.Inputs["x"],export.Outputs["y"],2);
        Assert.True(compiled.Latency>=1);
        Assert.Contains(compiled.Graph.Nodes,n=>n.Operation==
            SharpHDL.Core.HardwareOperation.Constant && n.ConstantValue==14);
    }
}
