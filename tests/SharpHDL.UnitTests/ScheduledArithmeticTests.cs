using SharpHDL.Core;
using SharpHDL.HLS;
using SharpHDL.Simulation;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>Independent arithmetic, lane sharing, gating and illegal-binding checks.</summary>
public sealed class ScheduledArithmeticTests
{
    /// <summary>Two simultaneous operations use two instances, reused by a later state.</summary>
    [Theory]
    [InlineData(HardwareOperation.Add)]
    [InlineData(HardwareOperation.Subtract)]
    [InlineData(HardwareOperation.Multiply)]
    public void Capacity_two_shares_across_states(HardwareOperation operation)
    {
        var b=new ScheduledMachineBuilder("Shared");
        var x=b.Input("x",8);var y=b.Input("y",8);var enabled=b.Input("enabled",1);
        var first=b.State("first");var second=b.State("second");
        var alu=b.ArithmeticResource("alu",operation,8,2);
        var a=first.Compute(alu,x,y,enabled);
        var c=first.Compute(alu,y,x);
        var d=second.Compute(alu,x,b.Constant(8,3));
        first.Transition("second");second.Transition("first");
        b.Output("a",a);b.Output("c",c);b.Output("d",d);
        var design=b.Lower();
        Assert.Equal(2,design.Graph.Nodes.Count(n=>n.Operation==operation));
        var sim=new HardwareGraphMachine(design.Graph,design.Inputs,design.Outputs,design.NextState,design.ResetValues,design.Memories);
        for(int i=0;i<256;i++)
        {
            int other=255-i;
            sim.SetInput("x",i);sim.SetInput("y",other);sim.SetInput("enabled",i&1);
            Assert.Equal((i&1)==1?Reference(operation,i,other):0,(int)sim.ReadOutput("a"));
            Assert.Equal(Reference(operation,other,i),(int)sim.ReadOutput("c"));
            Assert.Equal(0,(int)sim.ReadOutput("d"));
            sim.Tick();
            Assert.Equal(0,(int)sim.ReadOutput("a"));Assert.Equal(0,(int)sim.ReadOutput("c"));
            Assert.Equal(Reference(operation,i,3),(int)sim.ReadOutput("d"));sim.Tick();
        }
    }

    /// <summary>Guards cannot silently overbook a physical instance.</summary>
    [Fact]
    public void Oversubscription_is_rejected_even_with_false_guards()
    {
        var b=new ScheduledMachineBuilder("Conflict");var s=b.State("only");
        var resource=b.ArithmeticResource("mul",HardwareOperation.Multiply,8);
        for(int i=0;i<2;i++)s.Compute(resource,b.Constant(8,1),b.Constant(8,2),b.Constant(1,0));
        Assert.Contains("mul",Assert.Throws<InvalidOperationException>(()=>b.Lower()).Message);
    }

    /// <summary>Only compatible owned operands and registered dependencies are accepted.</summary>
    [Fact]
    public void Illegal_bindings_are_actionable()
    {
        var b=new ScheduledMachineBuilder("Owner");var s=b.State("only");
        var foreign=new ScheduledMachineBuilder("Foreign");var other=foreign.State("only");
        var resource=b.ArithmeticResource("mul",HardwareOperation.Multiply,8);
        var foreignResource=foreign.ArithmeticResource("mul",HardwareOperation.Multiply,8);
        var x=b.Constant(8,3);var y=b.Constant(8,4);
        Assert.Throws<ArgumentException>(()=>b.ArithmeticResource("bad",HardwareOperation.Equal,8));
        Assert.Throws<ArgumentOutOfRangeException>(()=>b.ArithmeticResource("bad",HardwareOperation.Add,8,0));
        Assert.Throws<ArgumentException>(()=>s.Compute(foreignResource,x,y));
        Assert.Throws<ArgumentException>(()=>s.Compute(resource,b.Constant(7,3),y));
        Assert.Throws<ArgumentException>(()=>s.Compute(resource,x,y,b.Constant(2,1)));
        var bound=s.Compute(resource,x,y);
        Assert.Throws<ArgumentException>(()=>s.Compute(resource,bound.Add(x),y));
        var foreignBound=other.Compute(foreignResource,x,y);
        Assert.Throws<ArgumentException>(()=>b.Output("foreign",foreignBound));
        b.Output("result",bound);b.Lower();
        Assert.Throws<InvalidOperationException>(()=>s.Compute(resource,x,y));
    }

    private static int Reference(HardwareOperation operation,int x,int y)
        =>(operation switch {HardwareOperation.Add=>x+y,HardwareOperation.Subtract=>x-y,_=>x*y})&255;
}
