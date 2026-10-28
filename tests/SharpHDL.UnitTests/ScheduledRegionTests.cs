using SharpHDL.Core;
using SharpHDL.Examples;
using SharpHDL.HLS;
using SharpHDL.Simulation;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>Typed-effect scheduling, independent interval checks and real registered arithmetic.</summary>
public sealed class ScheduledRegionTests
{
    /// <summary>Generated register programs match independent sequential semantics despite reversed lexical priorities.</summary>
    [FsCheck.Xunit.Property(MaxTest=100,Replay="1145655947,296144285")]
    public void Generated_register_programs_preserve_source_order(byte[] commands)
    {
        byte[] code=(commands??[]).Take(16).ToArray();if(code.Length==0)code=[0];
        var b=new ScheduledMachineBuilder("GeneratedRegion");var registers=Enumerable.Range(0,4).Select(i=>b.Register("r"+i,8,i)).ToArray();
        int[] expected=[0,1,2,3];var program=new List<ScheduledOperationBlock>();
        for(int i=0;i<code.Length;i++)
        {
            int target=code[i]&3,source=(code[i]>>2)&3,delta=code[i]>>4;
            program.Add(b.Operation("op_"+(16-i).ToString("D2")).Assign(registers[target],registers[source].Value.Add(b.Constant(8,delta))));
            expected[target]=(expected[source]+delta)&255;
        }
        var plan=b.ScheduleRegion("work",program.ToArray());
        for(int i=0;i<4;i++)b.Output("out"+i,registers[i].Value);
        var design=b.Lower();var machine=new HardwareGraphMachine(design.Graph,design.Inputs,design.Outputs,design.NextState,design.ResetValues,design.Memories);
        for(int edge=0;edge<plan.CycleCount;edge++)machine.Tick();
        for(int i=0;i<4;i++)Assert.Equal(expected[i],(int)machine.ReadOutput("out"+i));
    }
    /// <summary>Minimum/maximum iterative widths use a real shared adder and sample operands once.</summary>
    [Theory][InlineData(2)][InlineData(4)][InlineData(8)][InlineData(32)]
    public void Iterative_width_bounds_and_operand_capture_are_real(int width)
    {
        var b=new ScheduledMachineBuilder("WidthBound");var x=b.Input("x",width);var y=b.Input("y",width);var target=b.Register("p",width);
        var mul=b.IterativeMultiplier("mul",width);var op=b.Operation("multiply");op.Assign(target,op.Compute(mul,x,y));
        var plan=b.ScheduleRegion("r",op);b.Output("result",target.Value);b.Output("done",b.InState(plan.Done));var design=b.Lower();
        Assert.Equal(width,plan.CycleCount);Assert.Equal(1,design.Graph.Nodes.Count(n=>n.Operation==HardwareOperation.Add));
        var machine=new HardwareGraphMachine(design.Graph,design.Inputs,design.Outputs,design.NextState,design.ResetValues,design.Memories);
        ulong mask=width==32?uint.MaxValue:(1UL<<width)-1;machine.SetInput("x",mask);machine.SetInput("y",mask);machine.Tick();
        machine.SetInput("x",0);machine.SetInput("y",0);
        for(int edge=1;edge<width;edge++)machine.Tick();
        Assert.Equal(1,(int)machine.ReadOutput("done"));Assert.Equal((mask*mask)&mask,(ulong)machine.ReadOutput("result"));
    }
    /// <summary>Source-order aliases override lexical ordering; a shared iterative unit occupies every phase.</summary>
    [Fact]
    public void Real_iterative_banks_have_automatic_dependencies_and_twenty_edge_latency()
    {
        var (design,plan)=ScheduledIterativeBanks.BuildWithPlan();
        Assert.Equal(20,plan.CycleCount);Assert.Equal(2,plan.Starts["d_mul_first"]);Assert.Equal(10,plan.Starts["e_mul_second"]);
        Assert.Contains("z_write_left",plan.Dependencies["b_load_left"]);Assert.Contains("d_mul_first",plan.Dependencies["f_store_sum"]);
        Assert.Equal(new[]{"d_mul_first","e_mul_second"},plan.Dependencies["f_store_sum"]);
        Assert.Equal(8,plan.Latencies["d_mul_first"]);plan.ValidateAssignments(plan.Starts);
        Assert.DoesNotContain(design.Graph.Nodes,n=>n.Operation==HardwareOperation.Multiply);
        Assert.Equal(2,design.Graph.Nodes.Count(n=>n.Operation==HardwareOperation.Add));
        var machine=new HardwareGraphMachine(design.Graph,design.Inputs,design.Outputs,design.NextState,design.ResetValues,design.Memories);
        machine.SetInput("left",7);machine.SetInput("right",11);machine.Tick();
        machine.SetInput("left",255);machine.SetInput("right",0); // Later input changes must not affect captured operands.
        for(int edge=1;edge<20;edge++){Assert.Equal(0,(int)machine.ReadOutput("done"));machine.Tick();}
        Assert.Equal(1,(int)machine.ReadOutput("done"));Assert.Equal(98,(int)machine.ReadOutput("result"));
        for(int edge=0;edge<3;edge++)machine.Tick();Assert.Equal(98,(int)machine.ReadOutput("result"));
        var bad=plan.Starts.ToDictionary(p=>p.Key,p=>p.Value);bad["e_mul_second"]=3;
        Assert.Throws<ArgumentException>(()=>plan.ValidateAssignments(bad));
        bad=plan.Starts.ToDictionary(p=>p.Key,p=>p.Value);bad["f_store_sum"]=17;
        Assert.Throws<ArgumentException>(()=>plan.ValidateAssignments(bad));
    }
    /// <summary>Reproducible random operands/reset phases are compared with independent CLR modular arithmetic.</summary>
    [FsCheck.Xunit.Property(MaxTest=50,Replay="1145655947,296144285")]
    public void Reset_and_random_operands_preserve_iterative_results(byte left,byte right,byte resetPhase)
    {
        var design=ScheduledIterativeBanks.Build();var machine=new HardwareGraphMachine(design.Graph,design.Inputs,design.Outputs,design.NextState,design.ResetValues,design.Memories);
        machine.SetInput("left",17);machine.SetInput("right",23);
        for(int edge=0;edge<resetPhase%21;edge++)machine.Tick();machine.Reset();
        machine.SetInput("left",left);machine.SetInput("right",right);
        for(int edge=0;edge<20;edge++)machine.Tick();
        Assert.Equal(1,(int)machine.ReadOutput("done"));Assert.Equal((left*right+left*3)&255,(int)machine.ReadOutput("result"));
    }
    /// <summary>RAW/WAR/WAW dependencies are inferred from expression leaves and actual targets.</summary>
    [Fact]
    public void Register_hazards_are_extracted_from_actual_bodies()
    {
        var b=new ScheduledMachineBuilder("Hazards");var x=b.Register("x",8);var y=b.Register("y",8);
        var a=b.Operation("z_read").Assign(y,x.Value);var c=b.Operation("a_write").Assign(x,b.Constant(8,3));
        var d=b.Operation("b_overwrite").Assign(x,y.Value);var plan=b.ScheduleRegion("r",a,c,d);
        Assert.Contains("z_read",plan.Dependencies["a_write"]);Assert.Contains("a_write",plan.Dependencies["b_overwrite"]);
        Assert.Contains("z_read",plan.Dependencies["b_overwrite"]);Assert.Equal(0,plan.Starts["z_read"]);
        Assert.Equal(1,plan.Starts["a_write"]);Assert.Equal(2,plan.Starts["b_overwrite"]);
        Assert.Throws<InvalidOperationException>(()=>a.Assign(x,b.Constant(8,0)));b.Lower();
    }
    /// <summary>Different literal addresses omit alias ordering; unknown addresses conservatively alias through object identity.</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public void Memory_aliasing_uses_physical_identity_and_address_information(bool dynamic)
    {
        var b=new ScheduledMachineBuilder("Alias");var memory=b.Memory("ram",8,4);var alias=memory;var address=b.Input("address",2);var target=b.Register("r",8);
        var a=b.Operation("z_write").Write(memory,b.Constant(2,0),b.Constant(8,7),b.Constant(1,1));
        var c=b.Operation("a_read").Read(alias,dynamic?address:b.Constant(2,1),target,b.Constant(1,1));
        var plan=b.ScheduleRegion("r",a,c);
        Assert.Equal(dynamic,plan.Dependencies["a_read"].Contains("z_write"));
        Assert.Equal(dynamic?0:1,plan.Starts["z_write"]); // Independent addresses arbitrate by name, not source order.
        Assert.Equal(dynamic?1:0,plan.Starts["a_read"]);
        plan.ExecutionStates[0].Write(memory,b.Constant(2,0),b.Constant(8,1),b.Constant(1,1));
        Assert.Throws<InvalidOperationException>(()=>b.Lower()); // Lower independently checks the actual single port.
    }
    /// <summary>Physical reservation gates remain independently checked when emitted control states are modified.</summary>
    [Fact]
    public void Lower_rechecks_iterative_interval_reservations()
    {
        var b=new ScheduledMachineBuilder("Intervals");var x=b.Input("x",4);var p=b.Register("p",4);var resource=b.IterativeMultiplier("mul",4);
        var op=b.Operation("multiply");op.Assign(p,op.Compute(resource,x,x));var plan=b.ScheduleRegion("r",op);
        Assert.Equal(4,plan.CycleCount);plan.ExecutionStates[2].Use(plan.ReservationResources["mul"]);
        Assert.Throws<InvalidOperationException>(()=>b.Lower());
    }
    /// <summary>Unsupported shapes and forgotten/foreign operation bodies cannot silently produce hardware.</summary>
    [Fact]
    public void Invalid_and_unsupported_programs_fail_closed()
    {
        var b=new ScheduledMachineBuilder("Invalid");var reg=b.Register("r",8);var op=b.Operation("op").Assign(reg,b.Constant(8,1));
        Assert.Throws<InvalidOperationException>(()=>b.Lower());
        Assert.Throws<ArgumentException>(()=>b.ScheduleRegion("r",op,op));
        Assert.Throws<ArgumentException>(()=>b.ScheduleRegion("",op));
        var other=new ScheduledMachineBuilder("Other");var foreign=other.Operation("foreign");
        Assert.Throws<ArgumentException>(()=>b.ScheduleRegion("r",foreign));
        foreach(int width in new[]{1,33})Assert.Throws<ArgumentOutOfRangeException>(()=>b.IterativeMultiplier("bad",width));
        var mul=b.IterativeMultiplier("mul",8);var extra=b.Operation("iterative");extra.Assign(reg,extra.Compute(mul,reg.Value,reg.Value).Add(b.Constant(8,1)));
        Assert.Throws<NotSupportedException>(()=>b.ScheduleRegion("r",op,extra));
        var manual=new ScheduledMachineBuilder("Manual");var unit=manual.IterativeMultiplier("mul",8);var state=manual.State("state");
        Assert.Throws<InvalidOperationException>(()=>state.Compute(unit,manual.Constant(8,1),manual.Constant(8,2)));
        Assert.Throws<InvalidOperationException>(()=>manual.Operation("late"));
    }
    /// <summary>Every assignment is checked independently, including mixed valid/invalid entries and the exact last legal edge.</summary>
    [Fact]
    public void Assignment_validation_checks_all_entries_and_exact_budget()
    {
        var b=new ScheduledMachineBuilder("Bounds");var p=b.Register("p",8);var q=b.Register("q",8);
        var first=b.Operation("first").Assign(p,b.Constant(8,1));var second=b.Operation("second").Assign(q,b.Constant(8,2));
        var plan=b.ScheduleRegion("work",first,second);
        Assert.Equal(1,plan.CycleCount);Assert.Empty(plan.Dependencies["first"]);Assert.Empty(plan.Dependencies["second"]);
        plan.ValidateAssignments(new Dictionary<string,int>{{"first",4094},{"second",0}});
        foreach(int invalid in new[]{-1,4095,int.MaxValue})
            Assert.Throws<ArgumentException>(()=>plan.ValidateAssignments(new Dictionary<string,int>{{"first",invalid},{"second",0}}));
        Assert.Throws<ArgumentException>(()=>plan.ValidateAssignments(new Dictionary<string,int>{{"first",0}}));
        Assert.Throws<ArgumentException>(()=>plan.ValidateAssignments(new Dictionary<string,int>{{"first",0},{"second",0},{"extra",0}}));
        Assert.Throws<ArgumentException>(()=>plan.ValidateAssignments(new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase){{"FIRST",0},{"second",0}}));
        Assert.Throws<ArgumentNullException>(()=>plan.ValidateAssignments(null!));
        b.Lower();
    }
    /// <summary>Different-latency operators sharing a service reserve forward intervals, not reversed windows.</summary>
    [Fact]
    public void Mixed_operator_latencies_share_a_service_without_false_conflicts()
    {
        var b=new ScheduledMachineBuilder("MixedLatency");var service=b.Resource("service");var small=b.IterativeMultiplier("small",4);var large=b.IterativeMultiplier("large",8);
        var p=b.Register("p",4);var q=b.Register("q",8);
        var a=b.Operation("a_small");a.Assign(p,a.Compute(small,b.Constant(4,3),b.Constant(4,3))).Use(service);
        var c=b.Operation("b_large");c.Assign(q,c.Compute(large,b.Constant(8,7),b.Constant(8,7))).Use(service);
        var plan=b.ScheduleRegion("r",a,c);Assert.Equal(0,plan.Starts["a_small"]);Assert.Equal(4,plan.Starts["b_large"]);Assert.Equal(12,plan.CycleCount);
        plan.ValidateAssignments(plan.Starts);b.Output("small_result",p.Value);b.Output("large_result",q.Value);var design=b.Lower();
        var sim=new HardwareGraphMachine(design.Graph,design.Inputs,design.Outputs,design.NextState,design.ResetValues,design.Memories);
        for(int edge=0;edge<12;edge++)sim.Tick();Assert.Equal(9,(int)sim.ReadOutput("small_result"));Assert.Equal(49,(int)sim.ReadOutput("large_result"));
    }
    /// <summary>An absent memory effect cannot create a phantom alias dependency.</summary>
    [Fact]
    public void Memory_effects_do_not_serialize_unrelated_register_writes()
    {
        var b=new ScheduledMachineBuilder("Independent");var ram=b.Memory("ram",8,4);var p=b.Register("p",8);
        var a=b.Operation("a_write").Write(ram,b.Constant(2,0),b.Constant(8,1),b.Constant(1,1));var c=b.Operation("z_register").Assign(p,b.Constant(8,9));
        var plan=b.ScheduleRegion("r",a,c);Assert.Empty(plan.Dependencies["z_register"]);Assert.Equal(1,plan.CycleCount);b.Lower();
    }
}
