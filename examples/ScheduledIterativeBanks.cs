using SharpHDL.HLS;

namespace SharpHDL.Examples;

/// <summary>Automatic typed-effect hazards and a real eight-edge shared shift-add multiplier.</summary>
public static class ScheduledIterativeBanks
{
    /// <summary>Build (left*right + left*3) mod 256 in twenty edges; only the first edge samples external inputs.</summary>
    public static ScheduledHardwareDesign Build()=>BuildWithPlan().Design;
    /// <summary>Return emitted hardware and its auditable automatically inferred schedule.</summary>
    public static (ScheduledHardwareDesign Design,ScheduledRegionPlan Plan) BuildWithPlan()
    {
        var b=new ScheduledMachineBuilder("ScheduledIterativeBanks");
        var left=b.Input("left",8);var right=b.Input("right",8);var banks=b.BankedMemory("data",8,2,4);
        var x=b.Register("x",8);var y=b.Register("y",8);var p=b.Register("p",8);var q=b.Register("q",8);var result=b.Register("result_state",8);
        var mul=b.IterativeMultiplier("mul",8);var address=b.Constant(2,0);var one=b.Constant(1,1);
        var a=b.Operation("z_write_left").Write(banks.Banks[0],address,left,one);
        var alias=banks.Banks[1]; // A second reference to the same physical bank must still alias.
        var c=b.Operation("a_write_right").Write(alias,address,right,one);
        var d=b.Operation("b_load_left").Read(banks.Banks[0],address,x,one);
        var e=b.Operation("c_load_right").Read(alias,address,y,one);
        var f=b.Operation("d_mul_first");f.Assign(p,f.Compute(mul,x.Value,y.Value));
        var g=b.Operation("e_mul_second");g.Assign(q,g.Compute(mul,x.Value,b.Constant(8,3)));
        var h=b.Operation("f_store_sum").Write(banks.Banks[0],b.Constant(2,1),p.Value.Add(q.Value),one);
        var i=b.Operation("g_load_sum").Read(banks.Banks[0],b.Constant(2,1),result,one);
        var plan=b.ScheduleRegion("work",a,c,d,e,f,g,h,i);
        b.Output("result",result.Value);b.Output("done",b.InState(plan.Done));
        return (b.Lower(),plan);
    }
}
