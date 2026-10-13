using SharpHDL;
public static class DrillController
{
    public static Module Build()
    {
        var m=new Module("DrillController");
        var start=m.Input("start",1);var enable=m.Input("enable",1);var stop=m.Input("stop",1);
        var count=m.Register("count",4,0);
        count.Describe("Unsigned number of completed practice ticks, range 0..4; holds while paused.");
        var fsm=new StateMachine(m,"drill","Ready","Running","Done");
        fsm.When("Ready",start,"Running")
           .When("Running",stop,"Ready") // Highest priority in Running.
           .When("Running",enable & count.Eq(Expr.Const(4,3)),"Done")
           .When("Done",~start,"Ready");
        fsm.Build();
        m.Next(count,Expr.Mux(fsm.Is("Ready"),Expr.Const(4,0),
            Expr.Mux(fsm.Is("Running") & enable & ~stop,count+Expr.Const(4,1),count)));
        m.Drive(m.Output("phase",2),fsm.State);
        m.Drive(m.Output("ticks",4),count);
        m.Drive(m.Output("running",1),fsm.Is("Running"));
        m.Drive(m.Output("done",1),fsm.Is("Done"));
        return m;
    }
}
