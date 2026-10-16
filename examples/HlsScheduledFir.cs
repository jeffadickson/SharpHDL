using System;
using SharpHDL;
using SharpHDL.HLS;

namespace SharpHDL.Examples;

/// <summary>
/// Specialized hardware emitter for the cycle-aware single-port FIR HLS plan.
/// It lowers a resource-constrained schedule into an explicit clocked Module.
/// This is not yet a general-purpose Core IR memory scheduler.
/// </summary>
public static class HlsScheduledFir
{
    /// <summary>
    /// Generate a 16-bit unsigned AXI4-Stream FIR with 2–16 taps. One
    /// synchronous RAM port stores history and is shared across all taps.
    /// The resulting initiation interval is 2*taps clocks without stalls.
    /// Coefficients are captured atomically on input acceptance.
    /// </summary>
    /// <param name="taps">Number of FIR taps, from 2 to 16.</param>
    /// <returns>Validated-by-construction structural hardware graph.</returns>
    public static Module Build(int taps)
    {
        if(taps<2 || taps>16)
            throw new ArgumentOutOfRangeException(nameof(taps),"Supported hardware emitter range is 2..16 taps.");
        var plan=HlsSinglePortFirPlanner.Plan(taps);
        if(plan.InitiationInterval!=2*taps || plan.Steps.Count!=2*taps)
            throw new InvalidOperationException("Unexpected memory schedule.");
        int addressBits=1, depth=2;
        while(depth<taps){depth*=2;addressBits++;}
        int tapBits=1;
        while((1<<tapBits)<taps)tapBits++;
        var m=new Module("HlsScheduledFir"+taps);
        var input=Stream.Input(m,"s_axis",16,8);
        var output=Stream.Output(m,"m_axis",16,8);
        var coefficients=new Signal[taps];
        var captured=new Signal[taps];
        for(int i=0;i<taps;i++)
        {
            coefficients[i]=m.Input("coefficient"+i,16);
            captured[i]=m.Register("captured_coefficient"+i,16,0);
        }
        var state=m.Register("fir_state",2,0);
        var head=m.Register("fir_head",addressBits,0);
        var count=m.Register("fir_history_count",tapBits,0);
        var tap=m.Register("fir_tap",tapBits,1);
        var result=m.Register("fir_result",16,0);
        var last=m.Register("fir_last",1,0);
        var keep=m.Register("fir_keep",2,0);
        var user=m.Register("fir_user",8,0);
        var idle=state.Eq(Expr.Const(2,0));
        var issue=state.Eq(Expr.Const(2,1));
        var accumulate=state.Eq(Expr.Const(2,2));
        var outputState=state.Eq(Expr.Const(2,3));
        var finalTap=tap.Eq(Expr.Const(tapBits,taps-1));
        m.Drive(input.Ready,idle);
        m.Drive(output.Valid,outputState);
        m.Drive(output.Data,result);
        m.Drive(output.Last,last);
        m.Drive(output.Keep,keep);
        m.Drive(output.User,user);
        var accept=input.Valid & idle;
        var retire=outputState & output.Ready;

        // HEAD is advanced by the acceptance edge. A historical tap k is
        // at (HEAD_after_accept - k - 1) modulo RAM depth.
        var readAddress=head-(tap.Resize(addressBits)+Expr.Const(addressBits,1));
        var address=Expr.Mux(accept,head,readAddress);
        var readEnable=issue & ~count.Lt(tap);
        var ram=SynchronousRam.Create(m,"fir_history",16,depth,address,
            accept,input.Data,readEnable);

        Expr selected=captured[taps-1];
        for(int i=taps-2;i>=1;i--)
            selected=Expr.Mux(tap.Eq(Expr.Const(tapBits,i)),captured[i],selected);
        var history=Expr.Mux(~count.Lt(tap),ram.ReadData,Expr.Const(16,0));
        m.Next(result,Expr.Mux(accept,input.Data*coefficients[0],
            Expr.Mux(accumulate,result+history*selected,result)));
        m.NextWhen(head,accept,head+Expr.Const(addressBits,1));
        m.NextWhen(count,retire,Expr.Mux(count.Eq(Expr.Const(tapBits,taps-1)),
            count,count+Expr.Const(tapBits,1)));
        m.Next(tap,Expr.Mux(accept,Expr.Const(tapBits,1),
            Expr.Mux(accumulate & ~finalTap,tap+Expr.Const(tapBits,1),tap)));
        for(int i=0;i<taps;i++)m.NextWhen(captured[i],accept,coefficients[i]);
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
