using System;
using System.Collections.Generic;
using System.Numerics;
using SharpHDL;
using SharpHDL.Simulation;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>
/// Migration acceptance: the real legacy Module lowers to Core IR, whose
/// independent Simulation evaluator agrees with legacy state/port behavior.
/// </summary>
public sealed class CoreGraphLoweringTests
{
    [Fact]
    public void Core_ir_and_reference_simulator_agree_on_clock_edges_and_outputs()
    {
        var m=new Module("CoreParity");
        var a=m.Input("a",8);
        var b=m.Input("b",8);
        var enable=m.Input("enable",1);
        var accumulator=m.Register("accumulator",8,17);
        var output=m.Output("output",8);
        m.Next(accumulator,Expr.Mux(enable,accumulator+(a*b),accumulator));
        m.Drive(output,accumulator^b);

        var export=m.ExportCoreGraph();
        Assert.Equal(1,export.Graph.SchemaVersion);
        Assert.Equal(new BigInteger(17),export.ResetValues["accumulator"]);
        Assert.Contains("accumulator",export.NextState.Keys);
        Assert.Contains("output",export.Outputs.Keys);

        var sim=m.Simulate();
        sim.Tick(true);
        var rng=new Random(4627);
        for(int edge=0;edge<300;edge++)
        {
            var av=rng.Next(256);
            var bv=rng.Next(256);
            var en=edge%5==0?0:1;
            sim.Set(a,av);sim.Set(b,bv);sim.Set(enable,en);
            var inputs=new Dictionary<int,BigInteger>
            {
                [export.Inputs["a"]]=av,
                [export.Inputs["b"]]=bv,
                [export.Inputs["enable"]]=en,
                [export.Inputs["accumulator"]]=sim.Read(accumulator)
            };
            var result=HardwareGraphEvaluator.Evaluate(export.Graph,inputs);
            Assert.Equal(sim.Read(output),result[export.Outputs["output"]]);
            var expectedNext=result[export.NextState["accumulator"]];
            sim.Tick();
            Assert.Equal(expectedNext,sim.Read(accumulator));
        }
    }

    [Fact]
    public void Unsupported_legacy_expression_fails_explicitly()
    {
        var m=new Module("UnsupportedCoreLowering");
        var input=m.Input("input",8);
        var output=m.Output("output",8);
        m.Drive(output,Expr.Mux(input.Eq(Expr.Const(8,0)),Expr.Const(8,0),input % Expr.Const(8,3)));
        var ex=Assert.Throws<NotSupportedException>(()=>m.ExportCoreGraph());
        Assert.Contains("does not yet support",ex.Message);
    }

    [Fact]
    public void Core_evaluator_rejects_missing_or_out_of_range_inputs()
    {
        var m=new Module("InputValidation");
        var input=m.Input("input",8);
        var output=m.Output("output",8);
        m.Drive(output,~input);
        var graph=m.ExportCoreGraph();
        Assert.Throws<ArgumentException>(()=>
            HardwareGraphEvaluator.Evaluate(graph.Graph,new Dictionary<int,BigInteger>()));
        Assert.Throws<ArgumentOutOfRangeException>(()=>
            HardwareGraphEvaluator.Evaluate(graph.Graph,new Dictionary<int,BigInteger>
            { [graph.Inputs["input"]]=256 }));
    }
}
