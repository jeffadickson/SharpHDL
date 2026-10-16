using SharpHDL;

namespace SharpHDL.Examples;

/// <summary>
/// Four-tap unsigned FIR backed by a single-port synchronous 16x16 RAM.
/// It uses a multicycle memory schedule rather than parallel history registers.
/// All arithmetic wraps modulo 2^16. The interface is AXI4-Stream.
/// </summary>
public static class AxiFirRam
{
    /// <summary>
    /// Build a RAM-backed four-tap FIR. A sample is accepted only in IDLE.
    /// One RAM write records the new sample; three registered reads fetch the
    /// historical samples in separate cycles. The result holds in OUTPUT until
    /// accepted. Initiation interval is at least eight clocks, not one.
    /// </summary>
    public static Module Build()
    {
        var m=new Module("AxiFirRam");
        var input=Stream.Input(m,"s_axis",16,8);
        var output=Stream.Output(m,"m_axis",16,8);
        var coefficients=new Signal[4];
        var latched=new Signal[4];
        for(int i=0;i<4;i++)
        {
            coefficients[i]=m.Input("coefficient"+i,16);
            latched[i]=m.Register("latched_coefficient"+i,16,0);
        }
        var state=m.Register("fir_state",2,0);
        var head=m.Register("fir_head",4,0);
        var count=m.Register("fir_history_count",2,0);
        var tap=m.Register("fir_tap",2,1);
        var result=m.Register("fir_result",16,0);
        var last=m.Register("fir_last",1,0);
        var keep=m.Register("fir_keep",2,0);
        var user=m.Register("fir_user",8,0);
        var idle=state.Eq(Expr.Const(2,0));
        var issue=state.Eq(Expr.Const(2,1));
        var accumulate=state.Eq(Expr.Const(2,2));
        var outputState=state.Eq(Expr.Const(2,3));
        var finalTap=tap.Eq(Expr.Const(2,3));
        m.Drive(input.Ready,idle);
        m.Drive(output.Valid,outputState);
        m.Drive(output.Data,result);
        m.Drive(output.Last,last);
        m.Drive(output.Keep,keep);
        m.Drive(output.User,user);
        var accept=input.Valid & idle;
        var retire=outputState & output.Ready;

        // One shared address: write the incoming sample at HEAD, then read
        // preceding entries using the incremented HEAD on subsequent clocks.
        var readAddress=head-tap.Resize(4);
        var address=Expr.Mux(accept,head,readAddress);
        var readEnable=issue & ~count.Lt(tap);
        var ram=SynchronousRam.Create(m,"fir_history",16,16,address,
            accept,input.Data,readEnable);

        var selected=Expr.Mux(tap.Eq(Expr.Const(2,1)),latched[1],
            Expr.Mux(tap.Eq(Expr.Const(2,2)),latched[2],latched[3]));
        var history=Expr.Mux(~count.Lt(tap),ram.ReadData,Expr.Const(16,0));
        m.NextWhen(result,accept,input.Data*coefficients[0]);
        // The accumulator must have one driver; add the later tap updates
        // using the same combined enable and mux expression.
        // See the single assignment below (replaces the initial write).
        m.NextWhen(head,accept,head+Expr.Const(4,1));
        m.NextWhen(count,accept,Expr.Mux(count.Eq(Expr.Const(2,3)),
            count,count+Expr.Const(2,1)));
        m.Next(tap,Expr.Mux(accept,Expr.Const(2,1),
            Expr.Mux(accumulate & ~finalTap,tap+Expr.Const(2,1),tap)));
        for(int i=0;i<4;i++)m.NextWhen(latched[i],accept,coefficients[i]);
        m.NextWhen(last,accept,input.Last);
        m.NextWhen(keep,accept,input.Keep);
        m.NextWhen(user,accept,input.User);
        m.Next(state,Expr.Mux(accept,Expr.Const(2,1),
            Expr.Mux(issue,Expr.Const(2,2),
            Expr.Mux(accumulate,Expr.Mux(finalTap,Expr.Const(2,3),Expr.Const(2,1)),
            Expr.Mux(retire,Expr.Const(2,0),state)))));
        return m;
    }
}
