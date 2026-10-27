using System.Numerics;
using SharpHDL.HLS;
using SharpHDL.Simulation;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>Behavioral and malformed-input checks prompted by surviving semantic mutants.</summary>
public sealed class ScheduledSemanticTests
{
    /// <summary>Earlier assignments/transitions win, unguarded states hold, and commits are simultaneous.</summary>
    [Fact]
    public void Priority_hold_and_simultaneous_commit_follow_contract()
    {
        var b=new ScheduledMachineBuilder("Priority");var go=b.Input("go",1);
        var a=b.Register("a",8,17);var c=b.Register("c",8,42);
        var first=b.State("first");var second=b.State("second");var wrong=b.State("wrong");
        first.Assign(a,c.Value,go).Assign(a,b.Constant(8,99),go).Assign(c,a.Value,go)
            .Transition("second",go).Transition("wrong",go);
        second.Transition("first",go);wrong.Assign(a,b.Constant(8,255));
        b.Output("a_out",a.Value);var d=b.Lower();var sim=Machine(d);
        Assert.Equal(2,d.Graph.Nodes.Single(n=>n.Id==d.Inputs["scheduler_state"]).Type.Width);
        sim.Tick();Assert.Equal(17,(int)sim.ReadRegister("a"));Assert.Equal(0,(int)sim.ReadRegister("scheduler_state"));
        sim.SetInput("go",1);sim.Tick();
        Assert.Equal(42,(int)sim.ReadRegister("a"));Assert.Equal(17,(int)sim.ReadRegister("c"));
        Assert.Equal(1,(int)sim.ReadRegister("scheduler_state"));
        sim.SetInput("go",0);sim.Tick();Assert.Equal(1,(int)sim.ReadRegister("scheduler_state"));
        sim.Reset();Assert.Equal(17,(int)sim.ReadRegister("a"));Assert.Equal(42,(int)sim.ReadRegister("c"));
    }

    /// <summary>Power-of-two control-state counts use the exact minimum bit width.</summary>
    [Theory]
    [InlineData(1,1)] [InlineData(2,1)] [InlineData(4,2)] [InlineData(8,3)]
    public void State_width_is_minimal(int count,int width)
    {
        var b=new ScheduledMachineBuilder("States");for(int i=0;i<count;i++)b.State("s"+i);
        var d=b.Lower();Assert.Equal(width,d.Graph.Nodes.Single(n=>n.Id==d.Inputs["scheduler_state"]).Type.Width);
    }

    /// <summary>Constant/register ranges and declaration limits reject both sides of each boundary.</summary>
    [Fact]
    public void Numeric_boundaries_and_declaration_lifetime_are_checked()
    {
        Assert.Contains("name",Assert.Throws<ArgumentException>(()=>new ScheduledMachineBuilder(" ")).Message.ToLowerInvariant());
        var b=new ScheduledMachineBuilder("Limits");
        foreach(int width in new[]{0,4097})
        {
            Assert.Throws<ArgumentOutOfRangeException>(()=>b.Constant(width,0));
            Assert.Throws<ArgumentOutOfRangeException>(()=>b.Register("bad",width));
            Assert.Throws<ArgumentOutOfRangeException>(()=>b.Memory("bad",width,1));
        }
        foreach(int value in new[]{-1,256})
        {
            Assert.Throws<ArgumentOutOfRangeException>(()=>b.Constant(8,value));
            Assert.Throws<ArgumentOutOfRangeException>(()=>b.Register("bad",8,value));
        }
        b.Constant(4096,(BigInteger.One<<4096)-1);b.Register("wide",4096);
        b.Memory("one",1,1);b.Memory("max",1,65536);
        Assert.Throws<ArgumentOutOfRangeException>(()=>b.Memory("bad",1,65537));
        var s=b.State("only");Assert.Throws<ArgumentException>(()=>b.State("only"));
        Assert.Throws<ArgumentException>(()=>b.State(" "));
        b.InState(s);Assert.Contains("states",Assert.Throws<InvalidOperationException>(()=>b.State("late")).Message);
        b.Lower();
        Assert.Throws<InvalidOperationException>(()=>b.Input("late",1));
        Assert.Throws<InvalidOperationException>(()=>b.Register("late",1));
        Assert.Throws<InvalidOperationException>(()=>b.Memory("late",1,1));
        Assert.Throws<InvalidOperationException>(()=>b.Resource("late"));
        Assert.Throws<InvalidOperationException>(()=>b.State("late"));
        Assert.Throws<InvalidOperationException>(()=>b.Output("late",b.Constant(1,0)));
    }

    /// <summary>Every argument/guard/owner boundary is checked before mutation.</summary>
    [Fact]
    public void Malformed_operands_and_foreign_handles_are_rejected()
    {
        var b=new ScheduledMachineBuilder("Local");var f=new ScheduledMachineBuilder("Foreign");
        var s=b.State("only");var foreign=f.State("only");var r=b.Register("r",8);
        var ram=b.Memory("ram",8,2);var x=b.Constant(8,1);var one=b.Constant(1,1);var two=b.Constant(2,1);
        Assert.Throws<ArgumentNullException>(()=>x.Add(null!));
        Assert.Throws<ArgumentException>(()=>x.Add(one));
        Assert.Throws<ArgumentNullException>(()=>ScheduledValue.Select(null!,x,x));
        Assert.Throws<ArgumentNullException>(()=>ScheduledValue.Select(one,null!,x));
        Assert.Throws<ArgumentNullException>(()=>ScheduledValue.Select(one,x,null!));
        Assert.Throws<ArgumentException>(()=>ScheduledValue.Select(two,x,x));
        Assert.Throws<ArgumentException>(()=>ScheduledValue.Select(one,x,one));
        Assert.Throws<ArgumentException>(()=>s.Assign(r,one));
        Assert.Throws<ArgumentException>(()=>s.Assign(r,x,two));
        Assert.Throws<ArgumentException>(()=>s.Assign(f.Register("r",8),x));
        Assert.Throws<ArgumentException>(()=>s.Read(f.Memory("ram",8,2),one,r,one));
        Assert.Throws<ArgumentException>(()=>s.Read(ram,one,f.Register("read",8),one));
        Assert.Throws<ArgumentException>(()=>s.Read(ram,one,b.Register("small",1),one));
        Assert.Throws<ArgumentException>(()=>s.Read(ram,one,r,two));
        Assert.Throws<ArgumentException>(()=>s.Write(f.Memory("write",8,2),one,x,one));
        Assert.Throws<ArgumentException>(()=>s.Write(ram,one,one,one));
        Assert.Throws<ArgumentException>(()=>s.Write(ram,one,x,two));
        Assert.Throws<ArgumentException>(()=>s.Write(ram,f.Input("address",1),x,one));
        Assert.Throws<ArgumentException>(()=>s.Use(f.Resource("foreign")));
        Assert.Throws<ArgumentException>(()=>s.Transition(" "));
        Assert.Throws<ArgumentException>(()=>b.InState(foreign));
        Assert.Throws<ArgumentException>(()=>b.Input(" ",1));
        b.Resource("resource");Assert.Throws<ArgumentException>(()=>b.Resource("resource"));
        Assert.Throws<InvalidOperationException>(()=>new ScheduledMachineBuilder("Empty").Lower());
    }

    /// <summary>Generic reservations enforce capacity two, and memory diagnostics identify the resource.</summary>
    [Fact]
    public void Reservation_and_memory_conflicts_have_context()
    {
        var b=new ScheduledMachineBuilder("Capacity");var resource=b.Resource("service",2);
        var s=b.State("only");s.Use(resource).Use(resource);b.Lower();
        var bad=new ScheduledMachineBuilder("Overflow");var r=bad.Resource("service",2);
        bad.State("only").Use(r).Use(r).Use(r);
        Assert.Contains("service",Assert.Throws<InvalidOperationException>(()=>bad.Lower()).Message);
        var memory=new ScheduledMachineBuilder("Address");var ram=memory.Memory("table",8,2);
        memory.State("read").Read(ram,memory.Constant(2,0),memory.Register("data",8),memory.Constant(1,1));
        Assert.Contains("table",Assert.Throws<InvalidOperationException>(()=>memory.Lower()).Message);
    }

    private static HardwareGraphMachine Machine(ScheduledHardwareDesign d)
        =>new(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);
}
