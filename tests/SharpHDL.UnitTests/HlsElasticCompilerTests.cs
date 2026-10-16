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
            budget,new HlsCostProfile{MultiplyCost=budget==1?1:2,ArithmeticCost=1});
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
    [Fact]
    public void Compiled_Core_pipeline_and_emitted_Verilog_model_match_under_stalls()
    {
        var m=new Module("ElasticRtlSource");
        var input=m.Input("input",16);
        var output=m.Output("output",16);
        m.Drive(output,((input+Expr.Const(16,11))*input)+Expr.Const(16,9));
        var export=m.ExportCoreGraph();
        var pipeline=HlsElasticCompiler.Compile(export.Graph,
            export.Inputs["input"],export.Outputs["output"],2,
            new HlsCostProfile{MultiplyCost=2,ArithmeticCost=1});
        var rtlModule=pipeline.ToModule("ElasticRtlFixture");
        var rtl=rtlModule.Verilog();
        Assert.Contains("hls_valid_",rtl);
        Assert.Contains("h_ElasticRtlFixture",rtl);
        var core=new HardwareGraphMachine(pipeline.Graph,pipeline.Inputs,pipeline.Outputs,
            pipeline.NextState,pipeline.ResetValues,
            new Dictionary<string,SharpHDL.Core.HardwareMemoryPort>());
        var reference=rtlModule.Simulate();
        reference.Tick(true);
        var ports=rtlModule.Ports.ToDictionary(p=>p.Name,p=>p);
        var rng=new Random(2112);
        for(int cycle=0;cycle<400;cycle++)
        {
            int value=rng.Next(65536);
            int valid=rng.Next(4)==0?0:1;
            int ready=rng.Next(3)==0?0:1;
            foreach(var (name,data) in new[]{
                ("hls_input_data",value),
                ("hls_input_valid",valid),
                ("hls_output_ready",ready)})
            {
                core.SetInput(name,data);
                reference.Set(ports[name],data);
            }
            foreach(var name in new[]{"hls_input_ready","hls_output_valid","hls_output_data"})
                Assert.Equal(reference.Read(ports[name]),core.ReadOutput(name));
            core.Tick();
            reference.Tick();
        }
    }

    [Fact]
    public void Reset_discards_all_inflight_tokens_even_when_pipeline_is_stalled()
    {
        var m=new Module("ElasticResetSource");
        var x=m.Input("x",8);
        var y=m.Output("y",8);
        m.Drive(y,(x+Expr.Const(8,1))*(x+Expr.Const(8,2)));
        var exported=m.ExportCoreGraph();
        var compiled=HlsElasticCompiler.Compile(exported.Graph,
            exported.Inputs["x"],exported.Outputs["y"],2,
            new HlsCostProfile{ArithmeticCost=1,MultiplyCost=2});
        var machine=new HardwareGraphMachine(compiled.Graph,compiled.Inputs,
            compiled.Outputs,compiled.NextState,compiled.ResetValues,
            new Dictionary<string,SharpHDL.Core.HardwareMemoryPort>());
        machine.SetInput("hls_output_ready",0);
        machine.SetInput("hls_input_valid",1);
        machine.SetInput("hls_input_data",42);
        for(int cycle=0;cycle<compiled.Latency+3;cycle++)machine.Tick();
        Assert.Equal(BigInteger.One,machine.ReadOutput("hls_output_valid"));
        Assert.Equal(BigInteger.Zero,machine.ReadOutput("hls_input_ready"));
        machine.Reset();
        machine.SetInput("hls_input_valid",0);
        Assert.Equal(BigInteger.Zero,machine.ReadOutput("hls_output_valid"));
        Assert.Equal(BigInteger.One,machine.ReadOutput("hls_input_ready"));
        machine.SetInput("hls_output_ready",1);
        for(int i=0;i<compiled.Latency+3;i++)
        {
            Assert.Equal(BigInteger.Zero,machine.ReadOutput("hls_output_valid"));
            machine.Tick();
        }
    }

    /// <summary>
    /// Verify that a reset in the middle of a blocked transaction stream
    /// invalidates old tokens and allows fresh transactions to complete.
    /// </summary>
    [Fact]
    public void Reset_under_backpressure_recovers_with_only_post_reset_transactions()
    {
        var source=new Module("ElasticResetRecovery");
        var x=source.Input("x",16);
        var y=source.Output("y",16);
        source.Drive(y,(x+Expr.Const(16,5))*Expr.Const(16,3));
        var exported=source.ExportCoreGraph();
        var compiled=HlsElasticCompiler.Compile(exported.Graph,
            exported.Inputs["x"],exported.Outputs["y"],2,
            new HlsCostProfile{ArithmeticCost=1,MultiplyCost=2});
        var machine=new HardwareGraphMachine(compiled.Graph,compiled.Inputs,
            compiled.Outputs,compiled.NextState,compiled.ResetValues,
            new Dictionary<string,SharpHDL.Core.HardwareMemoryPort>());

        // Fill the pipeline and force the last stage to remain blocked.
        machine.SetInput("hls_output_ready",0);
        machine.SetInput("hls_input_valid",1);
        for(int i=0;i<compiled.Latency+3;i++)
        {
            machine.SetInput("hls_input_data",100+i);
            machine.Tick();
        }
        Assert.Equal(BigInteger.One,machine.ReadOutput("hls_output_valid"));
        machine.Reset();
        Assert.Equal(BigInteger.Zero,machine.ReadOutput("hls_output_valid"));

        // Drain only newly accepted tokens; pre-reset payloads must not leak.
        var expected=new Queue<BigInteger>();
        for(int cycle=0;cycle<120;cycle++)
        {
            bool sending=cycle<35;
            int value=cycle+1;
            machine.SetInput("hls_input_valid",sending?1:0);
            machine.SetInput("hls_input_data",value);
            machine.SetInput("hls_output_ready",cycle%5==0?0:1);
            bool takeOutput=machine.ReadOutput("hls_output_valid")!=0 && cycle%5!=0;
            bool takeInput=sending && machine.ReadOutput("hls_input_ready")!=0;
            if(takeOutput)
            {
                Assert.NotEmpty(expected);
                Assert.Equal(expected.Dequeue(),machine.ReadOutput("hls_output_data"));
            }
            if(takeInput)expected.Enqueue(((BigInteger)(value+5)*3)&65535);
            machine.Tick();
        }
        Assert.Empty(expected);
        Assert.Equal(BigInteger.Zero,machine.ReadOutput("hls_output_valid"));
    }

}
