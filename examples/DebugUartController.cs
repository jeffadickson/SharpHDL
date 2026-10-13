using SharpHDL;

// Command state machine behind an 8N1 UART's byte ready/valid interface.
public sealed class DebugUartControllerExample
{
    public Module Module; public Stream Rx,Tx; public Signal Control,Counter;
    public static DebugUartControllerExample Build()
    {
        var m=new Module("DebugUartController");
        var rx=Stream.Input(m,"rx",8,8);var tx=Stream.Output(m,"tx",8,8);
        var control=m.Register("control",8,1);var counter=m.Register("counter",32,0);
        var controlOut=m.Output("control_out",8);var countOut=m.Output("counter_out",32);
        m.Drive(controlOut,control);m.Drive(countOut,counter);
        m.NextWhen(counter,control.Slice(0,0),counter+Expr.Const(32,1));
        var state=m.Register("state",2,0); // 0: command, 1: echo byte, 2: control byte, 3: reply
        var reply=m.Register("reply",32,0);var length=m.Register("length",3,1);
        var index=m.Register("index",2,0);var tag=m.Register("tag",8,0);
        var idle=state.Eq(Expr.Const(2,0));var echo=state.Eq(Expr.Const(2,1));
        var write=state.Eq(Expr.Const(2,2));var sending=state.Eq(Expr.Const(2,3));
        var cmdEcho=rx.Data.Eq(Expr.Const(8,1));var cmdCount=rx.Data.Eq(Expr.Const(8,2));
        var cmdWrite=rx.Data.Eq(Expr.Const(8,3));var cmdControl=rx.Data.Eq(Expr.Const(8,4));
        var cmdPing=rx.Data.Eq(Expr.Const(8,0));
        m.Drive(rx.Ready,~sending);m.Drive(tx.Valid,sending);
        var last=index.Resize(3).Eq(length-Expr.Const(3,1));
        Expr byteValue=reply.Slice(31,24);
        for(int i=2;i>=0;i--)byteValue=Expr.Mux(index.Eq(Expr.Const(2,i)),reply.Slice(8*i+7,8*i),byteValue);
        m.Drive(tx.Data,byteValue);m.Drive(tx.Keep,Expr.Const(1,1));m.Drive(tx.Last,last);m.Drive(tx.User,tag);
        var received=rx.Fire;var completed=tx.Fire & last;
        var nextCommand=Expr.Mux(cmdEcho,Expr.Const(2,1),Expr.Mux(cmdWrite,Expr.Const(2,2),Expr.Const(2,3)));
        m.Next(state,Expr.Mux(received,Expr.Mux(idle,nextCommand,Expr.Const(2,3)),Expr.Mux(completed,Expr.Const(2,0),state)));
        var immediate=Expr.Mux(cmdCount,counter,Expr.Mux(cmdControl,control.Resize(32),Expr.Mux(cmdPing,Expr.Const(32,0xa5),Expr.Const(32,0xe0))));
        var response=Expr.Mux(idle,immediate,Expr.Mux(echo,rx.Data.Resize(32),Expr.Const(32,6)));
        m.NextWhen(reply,received,response);
        m.NextWhen(length,received,Expr.Mux(idle & cmdCount,Expr.Const(3,4),Expr.Const(3,1)));
        m.Next(index,Expr.Mux(received,Expr.Const(2,0),Expr.Mux(tx.Fire & ~last,index+Expr.Const(2,1),index)));
        m.NextWhen(tag,received & idle,rx.User);
        m.NextWhen(control,received & write,rx.Data);
        return new DebugUartControllerExample{Module=m,Rx=rx,Tx=tx,Control=controlOut,Counter=countOut};
    }
}
